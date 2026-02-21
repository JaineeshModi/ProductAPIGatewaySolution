using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GatewayApi.Middleware
{
    public sealed class IdempotencyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<IdempotencyMiddleware> _logger;

        // In-memory dedupe store for demo. For production use distributed store (Redis)
        private static readonly ConcurrentDictionary<string, bool> _requestKeys = new();

        public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                var method = context.Request.Method;
                var path = context.Request.Path;

                // Only enforce idempotency on (POST/PUT)
                if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method))
                {
                    var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();

                    _logger.LogDebug("IdempotencyMiddleware start. Method={Method} Path={Path} IdempotencyKey='{Key}'", method, path, idempotencyKey);

                    if (string.IsNullOrWhiteSpace(idempotencyKey))
                    {
                        _logger.LogWarning("IdempotencyMiddleware: missing Idempotency-Key for {Method} {Path}", method, path);
                    }
                    else
                    {
                        if (!_requestKeys.TryAdd(idempotencyKey, true))
                        {
                            _logger.LogInformation("IdempotencyMiddleware: duplicate Idempotency-Key detected. Key={Key} Method={Method} Path={Path}", idempotencyKey, method, path);
                            context.Response.StatusCode = StatusCodes.Status409Conflict;
                            await context.Response.WriteAsync("Duplicate idempotency key");
                            return;
                        }
                    }
                }

                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IdempotencyMiddleware: unexpected error processing request {Method} {Path}", context.Request.Method, context.Request.Path);
                throw;
            }            
        }
    }
}
