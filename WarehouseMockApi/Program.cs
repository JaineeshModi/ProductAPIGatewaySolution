using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var baseUrl = builder.Configuration["App:BaseUrl"];

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

var stock = new Dictionary<string, int> { ["1"] = 100, ["2"] = 50, ["3"] = 150, ["4"] = 150, ["5"] = 50, ["6"] = 10};

app.MapGet("/stock/{id}", (string id) =>
{
    if (stock.TryGetValue(id, out var quantity))
    {
        return Results.Ok(new { Stock = quantity });
    }
    
    return Results.NotFound(); // Return 404 if product doesn't exist yet
});

app.MapPut("/stock/{id}", async (string id, HttpContext ctx) =>
{
    var payload = await ctx.Request.ReadFromJsonAsync<StockPayload>();

    if (payload?.Stock == null)
        return Results.BadRequest("Missing Stock");

    int newStock = payload.Stock.Value;

    stock[id] = newStock;  // Store or update

    return Results.Ok(new { Id = id, Stock = newStock });
});

app.MapGet(
    "/",
    () => "Warehouse Mock API is running. Use /stock/1"
);

app.Run(baseUrl);

public class StockPayload
{
    public int? Stock { get; set; }
}