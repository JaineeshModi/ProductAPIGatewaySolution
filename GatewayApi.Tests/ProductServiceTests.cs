using GatewayApi.Models;
using GatewayApi.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace GatewayApi.Tests;

public class ProductServiceTests
{
    private readonly Mock<IHttpClientFactory> _httpFactoryMock = new();
    private readonly IConfiguration _config;

    public ProductServiceTests()
    {
        var configDict = new Dictionary<string, string?> 
        { 
            ["ERPUrl"] = "http://localhost:5001", 
            ["WarehouseUrl"] = "http://localhost:5002", 
            ["CacheTTLMinutes"] = "5" 
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();
    }

    [Fact]
    public async Task GetProductsAsync_CacheHit_ReturnsCached()
    {
        // Arrange: use real MemoryCache so we can simulate a cache hit easily
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cached = new List<Product> { new Product { Id = "c1", Name = "Cached" } };
        memoryCache.Set("products_all", cached);

        var handler = new RecordingHandler();
        var client = new HttpClient(handler);
        _httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new ProductService(_httpFactoryMock.Object, memoryCache, _config, new NullLogger<ProductService>());

        // Act
        var result = await service.GetProductsAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("c1", result.First().Id);
        // No HTTP calls should have been made because of cache hit
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetProductsAsync_CacheMiss_FetchesAndCaches()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new RecordingHandler();
        handler.SetResponse("/products", JsonSerializer.Serialize(new[] { new { id = "1", name = "P1", description = "D1" } }));
        handler.SetResponse("/stock/1", JsonSerializer.Serialize(new { stock = 7 }));
        var client = new HttpClient(handler);
        _httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new ProductService(_httpFactoryMock.Object, memoryCache, _config, new NullLogger<ProductService>());

        // Act
        var result = (await service.GetProductsAsync()).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("1", result[0].Id);
        Assert.Equal(7, result[0].Stock);

        // Cached
        Assert.True(memoryCache.TryGetValue("products_all", out object? cached));
        var cachedList = Assert.IsAssignableFrom<List<Product>>(cached);
        Assert.Single(cachedList);
    }

    [Fact]
    public async Task GetProductAsync_Found_ReturnsMerged()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new RecordingHandler();
        handler.SetResponse("/products/1", JsonSerializer.Serialize(new { id = "1", name = "P1", description = "D1" }));
        handler.SetResponse("/stock/1", JsonSerializer.Serialize(new { stock = 3 }));
        var client = new HttpClient(handler);
        _httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new ProductService(_httpFactoryMock.Object, memoryCache, _config, new NullLogger<ProductService>());

        var product = await service.GetProductAsync("1");

        Assert.NotNull(product);
        Assert.Equal("1", product!.Id);
        Assert.Equal(3, product.Stock);
    }

    [Fact]
    public async Task CreateProductAsync_Idempotent_SkipsOnDuplicate()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new RecordingHandler();
        handler.SetResponse("/products", ""); // respond OK
        var client = new HttpClient(handler);
        _httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new ProductService(_httpFactoryMock.Object, memoryCache, _config, new NullLogger<ProductService>());
        var product = new Product { Id = "p1" };
        var key = "test-key";

        // Act: Call twice
        await service.CreateProductAsync(product, key);
        await service.CreateProductAsync(product, key); // Should skip

        // Assert: only one POST was issued
        var postRequests = handler.Requests.Where(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/products")).ToList();
        Assert.Single(postRequests);
    }

    [Fact]
    public async Task UpdateProductAsync_Idempotent_SkipsOnDuplicate()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new RecordingHandler();
        handler.SetResponse("/products/1", ""); // respond OK to PUT
        var client = new HttpClient(handler);
        _httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new ProductService(_httpFactoryMock.Object, memoryCache, _config, new NullLogger<ProductService>());
        var product = new Product { Id = "1" };
        var key = "key-xyz";

        await service.UpdateProductAsync("1", product, key);
        await service.UpdateProductAsync("1", product, key);

        var putRequests = handler.Requests.Where(r => r.Method == HttpMethod.Put && r.RequestUri!.AbsolutePath.Contains("/products/1")).ToList();
        Assert.Single(putRequests);
    }

    [Fact]
    public async Task DeleteProductAsync_CallsDeleteOnce()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var handler = new RecordingHandler();
        handler.SetResponse("/products/1", ""); // respond OK to DELETE
        var client = new HttpClient(handler);
        _httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new ProductService(_httpFactoryMock.Object, memoryCache, _config, new NullLogger<ProductService>());

        await service.DeleteProductAsync("1");

        var deleteRequests = handler.Requests.Where(r => r.Method == HttpMethod.Delete && r.RequestUri!.AbsolutePath.Contains("/products/1")).ToList();
        Assert.Single(deleteRequests);
    }
}

// Simple RecordingHandler to inspect outgoing requests and return canned responses
public class RecordingHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();
    private readonly Dictionary<string, string> _responses = new(StringComparer.OrdinalIgnoreCase);

    public void SetResponse(string relativePath, string content)
    {
        // ensure path begins with '/'
        if (!relativePath.StartsWith("/")) relativePath = "/" + relativePath;
        _responses[relativePath] = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        // pick best match by suffix (handles /products and /products/1)
        var match = _responses.Keys.OrderByDescending(k => k.Length).FirstOrDefault(k => path.EndsWith(k, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            var content = _responses[match];
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        }

        // default empty array
        var defaultResp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };
        return Task.FromResult(defaultResp);
    }
}
