using Asp.Versioning;
using GatewayApi.Models;
using GatewayApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Collections.Generic;

namespace GatewayApi.Controllers
{
    [ApiController]
    [ApiVersion("2.0")]
    [Route("api/v{version:apiVersion}/products")]
    [Authorize]
    public class ProductsV2Controller : ControllerBase
    {
        private readonly IProductService _productService;
        private readonly ILogger<ProductsV2Controller> _logger;

        public ProductsV2Controller(IProductService productService, ILogger<ProductsV2Controller> logger)
        {
            _productService = productService;
            _logger = logger;
        }
        
        // GET /v2/products
        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            _logger.LogInformation("GetProducts (v2) called");
            var products = await _productService.GetProductsAsync();
            _logger.LogInformation("GetProducts (v2) returning {Count} products", products?.Count() ?? 0);

            // v2: with optional Tags
            var v2Products = products.Select(p => new ProductV2
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Stock = p.Stock,
                Tags = new List<string> { "v2-enriched", p.Name.ToLower().Contains("widget") ? "gadget" : "item" }
            });

            return Ok(v2Products);
        }

        // GET /v2/products/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProductById(string id)
        {
            _logger.LogInformation("GetProductById (v2) called. productId={ProductId}", id);
            var product = await _productService.GetProductAsync(id);
            if (product == null) 
            {
                _logger.LogInformation("GetProductById (v2): product not found. productId={ProductId}", id);
                return NotFound();
            }

            var v2Product = new ProductV2
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                Stock = product.Stock,
                Tags = new List<string> { "v2-enriched", product.Name.ToLower().Contains("widget") ? "gadget" : "item" }
            };

            _logger.LogInformation("GetProductById (v2): product found. productId={ProductId}", id);
            return Ok(v2Product);
        }

        // POST /v2/products
        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] Product product)
        {
            _logger.LogInformation("CreateProduct (v2) called. productId={ProductId}", product?.Id);

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("CreateProduct (v2): invalid model state for productId={ProductId}", product?.Id);
                return BadRequest(ModelState);
            }

            var key = Request.Headers["Idempotency-Key"].ToString();
            _logger.LogDebug("CreateProduct (v2): Idempotency-Key={Key}", key);

            await _productService.CreateProductAsync(product, key);

            _logger.LogInformation("CreateProduct (v2): created productId={ProductId}", product?.Id);
            return CreatedAtAction(nameof(GetProductById), new { id = product.Id }, product);
        }

        // PUT /v2/products/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(string id, [FromBody] Product product)
        {
            _logger.LogInformation("UpdateProduct (v2) called. productId={ProductId}", id);

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("UpdateProduct (v2): invalid model state for productId={ProductId}", id);
                return BadRequest(ModelState);
            }

            var key = Request.Headers["Idempotency-Key"].ToString();
            _logger.LogDebug("UpdateProduct (v2): Idempotency-Key={Key}", key);

            await _productService.UpdateProductAsync(id, product, key);
            _logger.LogInformation("UpdateProduct (v2): updated productId={ProductId}", id);

            return Ok();
        }

        // DELETE /v2/products/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(string id)
        {
            _logger.LogInformation("DeleteProduct (v2) called. productId={ProductId}", id);
            await _productService.DeleteProductAsync(id);
            _logger.LogInformation("DeleteProduct (v2): deleted productId={ProductId}", id);
            return NoContent();
        }
    }
}