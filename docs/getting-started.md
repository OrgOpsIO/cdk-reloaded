# Getting Started

This guide walks you through building a complete REST API with CDK Reloaded — from local development to AWS deployment.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (for AWS CDK CLI, only needed for deployment)
- [AWS CLI](https://aws.amazon.com/cli/) configured with credentials (only for deployment)

---

## Step 1: Create the Project

```bash
dotnet new web -n OrderApi
cd OrderApi
```

Add a reference to CDK Reloaded (when published as NuGet, this will be `dotnet add package CdkReloaded`):

```bash
dotnet add reference path/to/CdkReloaded/src/CdkReloaded/CdkReloaded.csproj
```

## Step 2: Define Your Data Model

Create a `Models/Order.cs`:

```csharp
using CdkReloaded.Abstractions;

namespace OrderApi.Models;

public class Order : ITableEntity
{
    [PartitionKey]
    public string Id { get; set; } = default!;

    public string CustomerName { get; set; } = default!;
    public string Status { get; set; } = "pending";
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Key points:
- Implement `ITableEntity` to mark it as a DynamoDB table entity
- Decorate exactly one property with `[PartitionKey]`
- Optionally add `[SortKey]` for composite keys
- Optionally add `[TableName("custom-name")]` on the class

## Step 3: Write Your Functions

### Create Order

Create `Functions/CreateOrder.cs`:

```csharp
using CdkReloaded.Abstractions;
using OrderApi.Models;

namespace OrderApi.Functions;

public record CreateOrderRequest(string CustomerName, decimal Total);
public record CreateOrderResponse(string Id);

[HttpApi(Method.Post, "/orders")]
public class CreateOrder(ITable<Order> orders) : IHttpFunction<CreateOrderRequest, CreateOrderResponse>
{
    public async Task<CreateOrderResponse> HandleAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            CustomerName = request.CustomerName,
            Total = request.Total
        };

        await orders.PutAsync(order, ct);
        return new CreateOrderResponse(order.Id);
    }
}
```

### Get Order

Create `Functions/GetOrder.cs`:

```csharp
using CdkReloaded.Abstractions;
using OrderApi.Models;

namespace OrderApi.Functions;

public record GetOrderRequest(string Id);
public record GetOrderResponse(string Id, string CustomerName, string Status, decimal Total);

[HttpApi(Method.Get, "/orders/{id}")]
public class GetOrder(ITable<Order> orders) : IHttpFunction<GetOrderRequest, GetOrderResponse>
{
    public async Task<GetOrderResponse> HandleAsync(GetOrderRequest request, CancellationToken ct = default)
    {
        var order = await orders.GetAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Order {request.Id} not found");

        return new GetOrderResponse(order.Id, order.CustomerName, order.Status, order.Total);
    }
}
```

### List Orders

Create `Functions/ListOrders.cs`:

```csharp
using CdkReloaded.Abstractions;
using OrderApi.Models;

namespace OrderApi.Functions;

public record ListOrdersRequest;
public record ListOrdersResponse(IReadOnlyList<OrderSummary> Orders);
public record OrderSummary(string Id, string CustomerName, decimal Total);

[HttpApi(Method.Get, "/orders")]
public class ListOrders(ITable<Order> orders) : IHttpFunction<ListOrdersRequest, ListOrdersResponse>
{
    public async Task<ListOrdersResponse> HandleAsync(ListOrdersRequest request, CancellationToken ct = default)
    {
        var allOrders = await orders.ScanAsync(ct);
        var summaries = allOrders
            .Select(o => new OrderSummary(o.Id, o.CustomerName, o.Total))
            .ToList();
        return new ListOrdersResponse(summaries);
    }
}
```

### Delete Order

Create `Functions/DeleteOrder.cs`:

```csharp
using CdkReloaded.Abstractions;
using OrderApi.Models;

namespace OrderApi.Functions;

public record DeleteOrderRequest(string Id);
public record DeleteOrderResponse(bool Deleted);

[HttpApi(Method.Delete, "/orders/{id}")]
public class DeleteOrder(ITable<Order> orders) : IHttpFunction<DeleteOrderRequest, DeleteOrderResponse>
{
    public async Task<DeleteOrderResponse> HandleAsync(DeleteOrderRequest request, CancellationToken ct = default)
    {
        await orders.DeleteAsync(request.Id, ct);
        return new DeleteOrderResponse(true);
    }
}
```

## Step 4: Wire Up the Entry Point

Replace `Program.cs` with:

```csharp
using CdkReloaded.Hosting;

var builder = CloudApplication.CreateBuilder(args);

builder.AddFunctions().FromAssembly();
builder.AddTables().FromAssembly();

var app = builder.Build();
app.Run();
```

That's the entire entry point. The builder:
1. Scans your assembly for `[HttpApi]` functions
2. Scans for `ITableEntity` implementations
3. Auto-discovers the appropriate runtime
4. Validates all DI dependencies

## Step 5: Run Locally

```bash
dotnet run
```

The local runtime starts a Kestrel server with in-memory DynamoDB tables:

```
info: Now listening on: http://localhost:5000
info: Mapping POST /orders -> CreateOrder
info: Mapping GET  /orders/{id} -> GetOrder
info: Mapping GET  /orders -> ListOrders
info: Mapping DELETE /orders/{id} -> DeleteOrder
```

Test it:

```bash
# Create an order
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -d '{"customerName": "Alice", "total": 42.50}'

# Get the order
curl http://localhost:5000/orders/{id}

# List all orders
curl http://localhost:5000/orders

# Delete an order
curl -X DELETE http://localhost:5000/orders/{id}
```

## Step 6: Inspect Your Resources

```bash
dotnet run -- list
```

Output:

```
=== CdkReloaded Resources ===

Functions (4):
  Post    /orders                        -> CreateOrder
  Get     /orders/{id}                   -> GetOrder
  Get     /orders                        -> ListOrders
  Delete  /orders/{id}                   -> DeleteOrder

Tables (1):
  Order
```

## Step 7: Deploy to AWS

```bash
# Preview changes
dotnet run -- diff

# Deploy
dotnet run -- deploy
```

This will:
1. `dotnet publish` your project for `linux-arm64` (Lambda)
2. Generate a CDK stack with Lambda functions, API Gateway, and DynamoDB tables
3. Deploy via `cdk deploy`

Output:

```
=== CdkReloaded Deploy ===
[1/3] Publishing for AWS Lambda (linux-arm64)...
[2/3] Synthesizing CDK stack...
[3/3] Deploying to AWS...
      ...CloudFormation deployment output...

Stack outputs:
{
  "OrderApi": {
    "ApiUrl": "https://abc123.execute-api.eu-central-1.amazonaws.com/"
  }
}
=== Deployment complete! ===
```

Now test against the real AWS endpoint:

```bash
curl -X POST https://abc123.execute-api.eu-central-1.amazonaws.com/orders \
  -H "Content-Type: application/json" \
  -d '{"customerName": "Alice", "total": 42.50}'
```

## Step 8: Tear Down

When you're done:

```bash
dotnet run -- destroy
```

---

## What You've Built

With ~100 lines of C# and zero infrastructure code:

- 4 Lambda functions (ARM64, self-contained)
- 1 HTTP API Gateway with routes
- 1 DynamoDB table (PAY_PER_REQUEST billing)
- Full local development environment with hot reload

All from the same codebase that runs locally with `dotnet run`.

---

## Next Steps

- [Functions Guide](functions.md) — Request/response patterns, route parameters, custom DI
- [DynamoDB Guide](dynamodb.md) — Composite keys, table naming, queries
- [Configuration Guide](configuration.md) — Memory, timeouts, defaults
- [Architecture Guide](architecture.md) — How runtime discovery and CDK generation work
