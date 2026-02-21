using GatewayApi.Models;

namespace GatewayApi.Services
{
    public interface IProductService
    {
        Task<IEnumerable<Product>> GetProductsAsync();
        Task<Product?> GetProductAsync(string id);
        Task CreateProductAsync(Product product, string idempotencyKey);
        Task UpdateProductAsync(string id, Product product, string idempotencyKey);
        Task DeleteProductAsync(string id);
    }
}
