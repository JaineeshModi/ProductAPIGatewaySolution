using GatewayApi.Models;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GatewayApi.Services
{
    public class ProductService : IProductService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _memoryCache;
        private readonly IConfiguration _configuration;
        private readonly ConcurrentDictionary<string, bool> _idempotencyStore = new(); // In-mem dedupe; for prod: use distributed caching like Redis
        private readonly ILogger<ProductService> _logger;
        public ProductService(
            IHttpClientFactory httpClientFactory,
            IMemoryCache memoryCache,
            IConfiguration configuration,
            ILogger<ProductService> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _memoryCache = memoryCache;
            _configuration = configuration;
            _logger = logger;

            _logger.LogInformation("ProductService initialized. ERPUrl={ERPUrl}, WarehouseUrl={WarehouseUrl}", 
                _configuration["ERPUrl"], _configuration["WarehouseUrl"]);
        }

        public async Task CreateProductAsync(Product product, string idempotencyKey)
        {
            _logger.LogInformation("CreateProductAsync called. idempotencyKey={IdempotencyKey}, productId={ProductId}", idempotencyKey, product?.Id);

            var operation = "POST/products";
            var bodyHash = ComputeHash(JsonSerializer.Serialize(product)); //Simple hashing
            var dedupeKey = $"{idempotencyKey}:{operation}:{bodyHash}";

            try
            {
                if (_idempotencyStore.ContainsKey(dedupeKey))
                {
                    _logger.LogInformation("CreateProductAsync: duplicate request detected, skipping. dedupeKey={DedupeKey}", dedupeKey);
                    return; // Idempotent: skip the record
                }

                _idempotencyStore[dedupeKey] = true;

                var url = _configuration["ERPUrl"] + "/products";
                _logger.LogDebug("Posting to ERP at {Url}", url);

                var resp = await _httpClient.PostAsJsonAsync(url, product);

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("CreateProductAsync: ERP POST returned non-success {StatusCode} for productId={ProductId}", resp.StatusCode, product?.Id);
                }
                else
                {
                    _logger.LogInformation("CreateProductAsync: product created successfully. productId={ProductId}", product?.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateProductAsync failed for productId={ProductId}", product?.Id);
                throw;
            }
        }

        public async Task DeleteProductAsync(string id)
        {
            _logger.LogInformation("DeleteProductAsync called. productId={ProductId}", id);

            try
            {
                var url = _configuration["ERPUrl"] + $"/products/{id}";
                _logger.LogDebug("Deleting product at {Url}", url);

                var resp = await _httpClient.DeleteAsync(url);

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("DeleteProductAsync: ERP DELETE returned non-success {StatusCode} for productId={ProductId}", resp.StatusCode, id);
                }
                else
                {
                    _logger.LogInformation("DeleteProductAsync: product deleted successfully. productId={ProductId}", id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteProductAsync failed for productId={ProductId}", id);
                throw;
            }
        }

        public async Task<Product?> GetProductAsync(string id)
        {
            _logger.LogInformation("GetProductAsync called. productId={ProductId}", id);

            try
            {
                var erpUrl = _configuration["ERPUrl"] + $"/products/{id}";
                _logger.LogDebug("Fetching product from ERP at {Url}", erpUrl);

                var erpResp = await _httpClient.GetAsync(erpUrl);
                if (!erpResp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GetProductAsync: ERP GET returned non-success {StatusCode} for productId={ProductId}", erpResp.StatusCode, id);
                    return null;
                }

                var erpProductJson = await erpResp.Content.ReadFromJsonAsync<JsonElement>();
                var name = GetStringProperty(erpProductJson, "name", "Name");
                var description = GetStringProperty(erpProductJson, "description", "Description");

                var stockUrl = _configuration["WarehouseUrl"] + $"/stock/{id}";
                _logger.LogDebug("Fetching stock from Warehouse at {Url}", stockUrl);

                var stockResp = await _httpClient.GetAsync(stockUrl);
                var stock = 0;
                if (stockResp.IsSuccessStatusCode)
                {
                    var stockJson = await stockResp.Content.ReadFromJsonAsync<JsonElement>();
                    stock = GetIntProperty(stockJson, "stock", "Stock");
                }
                else
                {
                    _logger.LogDebug("GetProductAsync: Warehouse GET returned non-success {StatusCode} for productId={ProductId}", stockResp.StatusCode, id);
                }

                _logger.LogInformation("GetProductAsync: product fetched. productId={ProductId}, name={Name}, stock={Stock}", id, name, stock);

                return new Product { Id = id, Name = name, Description = description, Stock = stock };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetProductAsync failed for productId={ProductId}", id);
                throw;
            }
        }

        public async Task<IEnumerable<Product>> GetProductsAsync()
        {
            const string cacheKey = "products_all";

            _logger.LogInformation("GetProductsAsync called. Checking cache...");

            if (_memoryCache.TryGetValue(cacheKey, out IEnumerable<Product>? cached) && cached != null)
            {
                _logger.LogInformation("Cache HIT for key {CacheKey}. Returning {Count} items.", cacheKey, cached.Count());
                return cached;
            }

            _logger.LogInformation("Cache MISS for key {CacheKey}. Fetching from upstream...", cacheKey);

            try
            {
                var erpUrl = _configuration["ERPUrl"] + "/products";
                _logger.LogDebug("Fetching all products from ERP at {Url}", erpUrl);

                //Fan-Out to ERPMockApi and WarehouseMockApi systems
                var erpProducts = await _httpClient.GetFromJsonAsync<JsonElement[]>(erpUrl) ?? Array.Empty<JsonElement>();
                _logger.LogInformation("GetProductsAsync: fetched {Count} items from ERP", erpProducts.Length);

                var merged = new List<Product>();

                foreach (var erpProduct in erpProducts)
                {
                    var id = GetStringProperty(erpProduct, "id", "Id");
                    if (string.IsNullOrEmpty(id))
                    {
                        _logger.LogWarning("GetProductsAsync: skipping ERP entry with missing id: {Raw}", erpProduct.ToString());
                        continue;
                    }

                    var stockUrl = _configuration["WarehouseUrl"] + $"/stock/{id}";
                    _logger.LogDebug("Fetching stock for productId={ProductId} from {Url}", id, stockUrl);

                    var stockResp = await _httpClient.GetAsync(stockUrl);
                    var stock = 0;
                    if (stockResp.IsSuccessStatusCode)
                    {
                        var stockJson = await stockResp.Content.ReadFromJsonAsync<JsonElement>();
                        stock = GetIntProperty(stockJson, "stock", "Stock");
                    }
                    else
                    {
                        _logger.LogDebug("GetProductsAsync: Warehouse GET returned non-success {StatusCode} for productId={ProductId}", stockResp.StatusCode, id);
                    }

                    var name = GetStringProperty(erpProduct, "name", "Name");
                    var description = GetStringProperty(erpProduct, "description", "Description");

                    merged.Add(new Product { Id = id, Name = name, Description = description, Stock = stock });
                }

                _logger.LogInformation("GetProductsAsync: merged total {Count} products", merged.Count);

                var rawTtl = _configuration["CacheTTLMinutes"] ?? "5";
                _logger.LogInformation("CacheTTLMinutes from config = '{RawValue}'", rawTtl);

                if (!int.TryParse(rawTtl, out var minutes) || minutes <= 0)
                {
                    minutes = 5;
                    _logger.LogWarning("Invalid CacheTTLMinutes '{RawValue}' → using default 5 minutes", rawTtl);
                }

                var ttl = TimeSpan.FromMinutes(minutes);
                _logger.LogInformation("Setting cache with TTL = {TtlTotalMinutes} minutes", ttl.TotalMinutes);

                var options = new MemoryCacheEntryOptions
                {
                    SlidingExpiration = ttl,                   
                    AbsoluteExpirationRelativeToNow = ttl * 2
                };

                _memoryCache.Set(cacheKey, merged, options);

                _logger.LogInformation("Cache SET for key {CacheKey} with {Count} items", cacheKey, merged.Count);
                return merged;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch products - cache not set");
                throw;
            }
        }

        public async Task UpdateProductAsync(string id, Product product, string idempotencyKey)
        {
            _logger.LogInformation("UpdateProductAsync called. productId={ProductId}, idempotencyKey={IdempotencyKey}", id, idempotencyKey);

            var operation = $"PUT/products/{id}";
            var bodyHash = ComputeHash(JsonSerializer.Serialize(product)); //Simple hashing
            var dedupeKey = $"{idempotencyKey}:{operation}:{bodyHash}";

            try
            {
                if (_idempotencyStore.ContainsKey(dedupeKey))
                {
                    _logger.LogInformation("UpdateProductAsync: duplicate request detected, skipping. dedupeKey={DedupeKey}", dedupeKey);
                    return; // Idempotent: skip the record
                }
                _idempotencyStore[dedupeKey] = true;

                var url = _configuration["ERPUrl"] + $"/products/{id}";
                _logger.LogDebug("Putting to ERP at {Url}", url);

                var resp = await _httpClient.PutAsJsonAsync(url, product);

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("UpdateProductAsync: ERP PUT returned non-success {StatusCode} for productId={ProductId}", resp.StatusCode, id);
                }
                else
                {
                    _logger.LogInformation("UpdateProductAsync: product updated successfully. productId={ProductId}", id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateProductAsync failed for productId={ProductId}", id);
                throw;
            }
        }

        private string ComputeHash(string input)
        {
            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
            _logger.LogDebug("Computed body hash: {HashPrefix} (len={Len})", hash?.Substring(0, Math.Min(8, hash.Length)), hash?.Length);
            return hash;
        }

        private string? GetStringProperty(JsonElement el, params string[] names)
        {
            if (el.ValueKind != JsonValueKind.Object) 
            {
                _logger.LogDebug("GetStringProperty: element is not an object (ValueKind={ValueKind})", el.ValueKind);
                return null;
            }

            // direct lookups first
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.String) return prop.GetString();
                    if (prop.ValueKind == JsonValueKind.Number) return prop.GetRawText();
                    if (prop.ValueKind == JsonValueKind.Null) return null;
                    return prop.ToString();
                }
            }

            // case-insensitive fallback
            foreach (var prop in el.EnumerateObject())
            {
                foreach (var name in names)
                {
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        var v = prop.Value;
                        if (v.ValueKind == JsonValueKind.String) return v.GetString();
                        if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
                        if (v.ValueKind == JsonValueKind.Null) return null;
                        return v.ToString();
                    }
                }
            }

            _logger.LogDebug("GetStringProperty: none of the names {Names} were found in object", names);
            return null;
        }

        private int GetIntProperty(JsonElement el, params string[] names)
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                _logger.LogDebug("GetIntProperty: element is not an object (ValueKind={ValueKind})", el.ValueKind);
                return 0;
            }

            // numeric direct
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var v))
                    return v;
            }

            // string numeric fallback
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    if (int.TryParse(prop.GetString(), out var parsed))
                    {
                        _logger.LogDebug("GetIntProperty: parsed numeric string for {Name} -> {Parsed}", name, parsed);
                        return parsed;
                    }
                }
            }

            // case-insensitive fallback
            foreach (var prop in el.EnumerateObject())
            {
                foreach (var name in names)
                {
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        var v = prop.Value;
                        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var iv)) return iv;
                        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var parsed))
                        {
                            _logger.LogDebug("GetIntProperty: parsed numeric string (case-insensitive) for {Name} -> {Parsed}", name, parsed);
                            return parsed;
                        }
                    }
                }
            }

            _logger.LogDebug("GetIntProperty: none of the names {Names} yielded an int, returning 0", names);
            return 0;
        }
    }
}
