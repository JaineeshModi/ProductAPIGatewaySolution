using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json.Nodes;

namespace GatewayApi.Swagger
{
    public class AddIdempotencyKeyHeaderFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (context.MethodInfo.GetCustomAttributes(true).Any(attr => attr is HttpPostAttribute || 
                attr is HttpPutAttribute) || operation.RequestBody != null)
            {
                if (operation.Parameters == null)
                {
                    operation.Parameters = new List<IOpenApiParameter>();
                }

                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "Idempotency-Key",
                    In = ParameterLocation.Header,
                    Description = "Unique key to ensure idempotent writes (required for POST/PUT). Use a UUID or similar.",
                    Required = true,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    Example = JsonValue.Create("550e8400-e29b-41d4-a716-446655440000")
                });
            }
        }
    }
}
