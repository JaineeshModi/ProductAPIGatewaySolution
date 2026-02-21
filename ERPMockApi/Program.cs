using Scalar.AspNetCore;
using System.Net.Http;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var baseUrl = builder.Configuration["App:BaseUrl"];
var WarehouseApiUrl = builder.Configuration["App:WarehouseApiUrl"];

var app = builder.Build();

// We'll need HttpClient to call WarehouseMockApi
var httpClient = new HttpClient { BaseAddress = new Uri(WarehouseApiUrl) };

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

var products = new Dictionary<string, Product> // Seed Data
{
    ["1"] = new Product { Id = "1", Name = "Phone widget", Description = "Iphone 14" },
    ["2"] = new Product { Id = "2", Name = "Laptop widget", Description = "Sony Laptop" },
    ["3"] = new Product { Id = "3", Name = "Macbook widget", Description = "Macbook Pro" },
    ["4"] = new Product { Id = "4", Name = "TV", Description = "Sony TV" },
    ["5"] = new Product { Id = "5", Name = "Fridge", Description = "LG Fridge" },
    ["6"] = new Product { Id = "6", Name = "Washing Machine", Description = "LG Washing Machine" }
};

app.MapGet("/products", () => Results.Ok(products.Values));

app.MapGet("/products/{id}", (string id) => products.TryGetValue(id, out var p) ? Results.Ok(p) : Results.NotFound());

app.MapPost("/products", async (Product product) =>
{
    if (product is null)
        return Results.BadRequest();

    var id = product.Id ?? Guid.NewGuid().ToString();
    product.Id = id;
    
    products[id] = product;

    int initialStock = product?.Stock ?? 0;  // Initialize stock in Warehouse (default 0 or from payload if you add it)

    var stockPayload = new { Stock = initialStock };
    await httpClient.PutAsJsonAsync($"/stock/{id}", stockPayload);

    return Results.Created($"/products/{id}", new { Id = id });

});

app.MapPut("/products/{id}", async (string id, Product product) =>
{
    if (product is null)
        return Results.BadRequest();

    if (!products.ContainsKey(id)) 
        return Results.NotFound();

    product.Id = id;
    products[id] = product;

    int initialStock = product?.Stock ?? 0;  // Initialize stock in Warehouse (default 0 or from payload if you add it)

    var stockPayload = new { Stock = initialStock };
    await httpClient.PutAsJsonAsync($"/stock/{id}", stockPayload);

    return Results.Ok();
});

app.MapDelete("/products/{id}", (string id) =>
{
    products.Remove(id); // Soft delete
    return Results.NoContent();
});

app.MapGet(
    "/",
    () => "ERP Mock API is running. Use /products"
);

app.Run(baseUrl);

// Simple product model
public class Product
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int Stock { get; set; }
}
