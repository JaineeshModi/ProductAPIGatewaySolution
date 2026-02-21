# Product API Gateway Solution
Gateway API is fetching, updating, removing and adding product data in ERP and Warehouse systems.

## Why used C# .Net
- Mature ecosystem for enterprise integration
- Built-in DI, HttpClientFactory, MemoryCache, Authentication middleware support

## How to Run
1. Clone the repository on your local system

2. Start mocks:
   dotnet run --project ERPMockApi --urls http://localhost:5001/scalar/
   dotnet run --project WarehouseMockApi --urls http://localhost:5002/scalar/

3. Generate dev JWT Token: (If you need to create new Token) current token Expires on 2026-05-19
   dotnet user-jwts create --role admin --scope "products.read" --scope "products.write"

4. Run GatewayApi:
   dotnet run --project GatewayApi --urls http://localhost:5000/swagger/index.html
   Open Swagger: http://localhost:5000/swagger
   Authorize with Bearer <token-from-user-jwts>

Override env var files for production use case
   
