using Asp.Versioning;
using GatewayApi.Models;
using GatewayApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GatewayApi.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/products")]
    [Authorize] // Requires auth
    
    public class ProductsController : ControllerBase
    {
        private readonly IProductService _productService;
        private readonly ILogger<ProductsController> _logger;

        public ProductsController(IProductService productService, ILogger<ProductsController> logger)
        {
            _productService = productService;
            _logger = logger;
        }

        // GET /v1/products
        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            _logger.LogInformation("GetProducts called");
            var products = await _productService.GetProductsAsync();
            _logger.LogInformation("GetProducts returning {Count} products", products?.Count() ?? 0);
            return Ok(products);
        }

        // GET /v1/products/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProcuctById(string id)
        {
            _logger.LogInformation("GetProductById called. productId={ProductId}", id);
            var product = await _productService.GetProductAsync(id);

            if (product == null)
            {
                _logger.LogInformation("GetProductById: product not found. productId={ProductId}", id);
                return NotFound();
            }

            _logger.LogInformation("GetProductById: product found. productId={ProductId}", id);
            return Ok(product);
        }

        // POST /v1/products
        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] Product product)
        {
            _logger.LogInformation("CreateProduct called. productId={ProductId}", product?.Id);

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("CreateProduct: invalid model state for productId={ProductId}", product?.Id);
                return BadRequest(ModelState);
            }

            var key = Request.Headers["Idempotency-Key"].ToString();
            _logger.LogDebug("CreateProduct: Idempotency-Key={Key}", key);

            await _productService.CreateProductAsync(product, key);

            _logger.LogInformation("CreateProduct: created productId={ProductId}", product?.Id);
            return CreatedAtAction(nameof(GetProcuctById), new { id = product.Id }, product);
        }

        // PUT /v1/products/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(string id, [FromBody] Product product)
        {
            _logger.LogInformation("UpdateProduct called. productId={ProductId}", id);

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("UpdateProduct: invalid model state for productId={ProductId}", id);
                return BadRequest(ModelState);
            }

            var key = Request.Headers["Idempotency-Key"].ToString();
            _logger.LogDebug("UpdateProduct: Idempotency-Key={Key}", key);

            await _productService.UpdateProductAsync(id, product, key);

            _logger.LogInformation("UpdateProduct: updated productId={ProductId}", id);
            return Ok();
        }

        // DELETE /v1/products/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(string id)
        {
            _logger.LogInformation("DeleteProduct called. productId={ProductId}", id);
            await _productService.DeleteProductAsync(id);
            _logger.LogInformation("DeleteProduct: deleted productId={ProductId}", id);
            return NoContent();
        }

    }
}