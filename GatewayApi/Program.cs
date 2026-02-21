using Asp.Versioning;
using GatewayApi.Middleware;
using GatewayApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Scalar.AspNetCore;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GatewayApi.Swagger;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
//builder.Services.AddOpenApi();

var baseUrl = builder.Configuration["App:BaseUrl"] ?? "http://localhost:5000";
var openApiServer = builder.Configuration["OpenApi:ServerUrl"] ?? baseUrl;

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("api-version"),
        new MediaTypeApiVersionReader("v"));
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'V";
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Product Gateway API",
        Version = "v1",
        Description = "API Gateway for ERP + Warehouse integration"
    });

    c.SwaggerDoc("v2", new OpenApiInfo
    {
        Title = "Product Gateway API",
        Version = "v2",
        Description = "Enhanced version with additional fields like Tags (backward compatible)"
    });

    c.OperationFilter<AddIdempotencyKeyHeaderFilter>();

    c.AddServer(new OpenApiServer
    {
        Url = openApiServer,
        Description = "Local Development (HTTP)"
    });

    const string schemeId = "Bearer";
    
    c.AddSecurityDefinition(schemeId, new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1bmlxdWVfbmFtZSI6ImhhcmRpIiwic3ViIjoiaGFyZGkiLCJqdGkiOiIyYTZmMWEyMSIsInNjb3BlIjpbInByb2R1Y3RzLnJlYWQiLCJwcm9kdWN0cy53cml0ZSJdLCJyb2xlIjoiYWRtaW4iLCJhdWQiOlsiaHR0cDovL2xvY2FsaG9zdDo1MDAwIiwiaHR0cHM6Ly9sb2NhbGhvc3Q6NzI1MyJdLCJuYmYiOjE3NzE0OTczNTMsImV4cCI6MTc3OTE4Njk1MywiaWF0IjoxNzcxNDk3MzU0LCJpc3MiOiJkb3RuZXQtdXNlci1qd3RzIn0.LBsR_XXvDB34L8pVcjhPG6MBUdTZgcN1imbadSpwkfg",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(schemeId, document)] = []
    });

});

builder.Services.AddMemoryCache(); // For caching
builder.Services.AddHttpClient(); // For calling upstream

// Auth: OAuth2.0 with JWT
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var bearerSection = builder.Configuration.GetSection("Authentication:Schemes:Bearer");
        options.TokenValidationParameters.ValidIssuer = bearerSection["ValidIssuer"];

        var validAudiences = bearerSection.GetSection("ValidAudiences").Get<string[]>();
        options.TokenValidationParameters.ValidAudiences = validAudiences;

        var signingKeySection = bearerSection.GetSection("SigningKeys").GetChildren().FirstOrDefault();

        if (signingKeySection == null)
        {
            throw new InvalidOperationException("Missing SigningKeys configuration.");
        }

        var signingValue = signingKeySection["Value"];
        var signingIssuer = signingKeySection["Issuer"];

        if (string.IsNullOrWhiteSpace(signingValue))
        {
            throw new InvalidOperationException("Missing configuration: 'Authentication:Schemes:Bearer:SigningKeys:0:Value'.");
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(signingValue);
        }
        catch (FormatException)
        {
            keyBytes = Encoding.UTF8.GetBytes(signingValue);
        }

        options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(keyBytes);
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateLifetime = true;
        options.TokenValidationParameters.ValidateIssuerSigningKey = true;

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine("Authentication failed: " + context.Exception.Message);
                if (context.Exception.InnerException != null)
                    Console.WriteLine("Inner: " + context.Exception.InnerException.Message);
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<IProductService, ProductService>(); // Created Product custom service

//Can also configure CORS for cross domain communication between ERPMockApi, WarehouseMockApi and GatewayApi

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    //app.MapOpenApi();
    //app.MapScalarApiReference();

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Product Gateway API v1");
        c.SwaggerEndpoint("/swagger/v2/swagger.json", "Product Gateway API v2");
    });

}

//app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<IdempotencyMiddleware>();
app.MapControllers();

try
{
    Log.Information("Starting ProductGateway API");
    app.Run(baseUrl);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}