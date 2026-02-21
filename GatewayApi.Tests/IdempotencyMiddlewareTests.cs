using System.IO;
using System.Threading.Tasks;
using GatewayApi.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GatewayApi.Tests
{
    public class IdempotencyMiddlewareTests
    {
        [Fact]
        public async Task DuplicateIdempotencyKey_SecondRequestReturns409AndDoesNotInvokeNext()
        {
            var logger = new NullLogger<IdempotencyMiddleware>();
            var invokedCount = 0;
            RequestDelegate next = ctx =>
            {
                invokedCount++;
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            };

            var middleware = new IdempotencyMiddleware(next, logger);

            var ctx1 = new DefaultHttpContext();
            ctx1.Request.Method = "POST";
            ctx1.Request.Path = "/api/v1/products";
            ctx1.Request.Headers["Idempotency-Key"] = "dup-key";

            await middleware.InvokeAsync(ctx1);
            Assert.Equal(1, invokedCount);
            Assert.NotEqual(409, ctx1.Response.StatusCode);

            var ctx2 = new DefaultHttpContext();
            ctx2.Request.Method = "POST";
            ctx2.Request.Path = "/api/v1/products";
            ctx2.Request.Headers["Idempotency-Key"] = "dup-key";

            // capture response body
            ctx2.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(ctx2);
            Assert.Equal(1, invokedCount); // next not called again
            Assert.Equal(409, ctx2.Response.StatusCode);

            ctx2.Response.Body.Seek(0, SeekOrigin.Begin);
            using var sr = new StreamReader(ctx2.Response.Body);
            var body = await sr.ReadToEndAsync();
            Assert.Contains("Duplicate", body, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}