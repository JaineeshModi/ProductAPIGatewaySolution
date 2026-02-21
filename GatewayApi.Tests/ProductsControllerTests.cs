using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GatewayApi.Controllers;
using GatewayApi.Models;
using GatewayApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GatewayApi.Tests
{
    public class ProductsControllerTests
    {
        private ProductsController CreateController(Mock<IProductService> svcMock, IHeaderDictionary? headers = null)
        {
            var logger = new NullLogger<ProductsController>();
            var controller = new ProductsController(svcMock.Object, logger);
            var httpContext = new DefaultHttpContext();
            if (headers != null)
            {
                foreach (var kv in headers)
                    httpContext.Request.Headers[kv.Key] = kv.Value;
            }
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        [Fact]
        public async Task GetProducts_ReturnsOkWithProducts()
        {
            var svcMock = new Mock<IProductService>();
            svcMock.Setup(s => s.GetProductsAsync()).ReturnsAsync(new List<Product> { new Product { Id = "1", Name = "A" } });

            var controller = CreateController(svcMock);

            var result = await controller.GetProducts();
            var ok = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsAssignableFrom<IEnumerable<Product>>(ok.Value);
            Assert.Single(list);
        }

        [Fact]
        public async Task GetProcuctById_Found_ReturnsOk()
        {
            var svcMock = new Mock<IProductService>();
            svcMock.Setup(s => s.GetProductAsync("1")).ReturnsAsync(new Product { Id = "1", Name = "A" });

            var controller = CreateController(svcMock);

            var result = await controller.GetProcuctById("1");
            var ok = Assert.IsType<OkObjectResult>(result);
            var product = Assert.IsType<Product>(ok.Value);
            Assert.Equal("1", product.Id);
        }

        [Fact]
        public async Task GetProcuctById_NotFound_ReturnsNotFound()
        {
            var svcMock = new Mock<IProductService>();
            svcMock.Setup(s => s.GetProductAsync("unknown")).ReturnsAsync((Product?)null);

            var controller = CreateController(svcMock);

            var result = await controller.GetProcuctById("unknown");
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task CreateProduct_Valid_ReturnsCreatedAndCallsService()
        {
            var svcMock = new Mock<IProductService>();
            svcMock.Setup(s => s.CreateProductAsync(It.IsAny<Product>(), It.IsAny<string>())).Returns(Task.CompletedTask);

            var headers = new HeaderDictionary { ["Idempotency-Key"] = "key-1" };
            var controller = CreateController(svcMock, headers);

            var product = new Product { Id = "p1", Name = "New" };
            var result = await controller.CreateProduct(product);

            var created = Assert.IsType<CreatedAtActionResult>(result);
            svcMock.Verify(s => s.CreateProductAsync(It.Is<Product>(p => p.Id == "p1"), "key-1"), Times.Once);
        }

        [Fact]
        public async Task UpdateProduct_Valid_ReturnsOkAndCallsService()
        {
            var svcMock = new Mock<IProductService>();
            svcMock.Setup(s => s.UpdateProductAsync("p1", It.IsAny<Product>(), It.IsAny<string>())).Returns(Task.CompletedTask);

            var headers = new HeaderDictionary { ["Idempotency-Key"] = "key-2" };
            var controller = CreateController(svcMock, headers);

            var product = new Product { Id = "p1", Name = "Upd" };
            var result = await controller.UpdateProduct("p1", product);

            Assert.IsType<OkResult>(result);
            svcMock.Verify(s => s.UpdateProductAsync("p1", It.Is<Product>(p => p.Id == "p1"), "key-2"), Times.Once);
        }

        [Fact]
        public async Task DeleteProduct_CallsServiceAndReturnsNoContent()
        {
            var svcMock = new Mock<IProductService>();
            svcMock.Setup(s => s.DeleteProductAsync("p1")).Returns(Task.CompletedTask);

            var controller = CreateController(svcMock);

            var result = await controller.DeleteProduct("p1");
            Assert.IsType<NoContentResult>(result);
            svcMock.Verify(s => s.DeleteProductAsync("p1"), Times.Once);
        }
    }
}