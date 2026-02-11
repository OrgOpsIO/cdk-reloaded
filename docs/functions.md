# Functions

Functions are the core building block in CDK Reloaded. Each function handles a single HTTP endpoint and maps to a single Lambda function when deployed.

---

## Defining a Function

A function is a class that:
1. Implements `IHttpFunction<TRequest, TResponse>`
2. Has an `[HttpApi]` attribute specifying the HTTP method and route

```csharp
using CdkReloaded.Abstractions;

public record GetUserRequest(string Id);
public record GetUserResponse(string Id, string Name, string Email);

[HttpApi(Method.Get, "/users/{id}")]
public class GetUser(ITable<User> users) : IHttpFunction<GetUserRequest, GetUserResponse>
{
    public async Task<GetUserResponse> HandleAsync(GetUserRequest request, CancellationToken ct = default)
    {
        var user = await users.GetAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"User {request.Id} not found");

        return new GetUserResponse(user.Id, user.Name, user.Email);
    }
}
```

---

## HTTP Methods

All standard HTTP methods are supported:

```csharp
[HttpApi(Method.Get, "/items/{id}")]       // GET
[HttpApi(Method.Post, "/items")]            // POST
[HttpApi(Method.Put, "/items/{id}")]        // PUT
[HttpApi(Method.Delete, "/items/{id}")]     // DELETE
[HttpApi(Method.Patch, "/items/{id}")]      // PATCH
```

---

## Request Binding

### GET / DELETE — Route + Query Parameters

For GET and DELETE requests, the request object is built from route parameters and query string values.

**Records (recommended):**

```csharp
public record GetItemRequest(string Id);

// GET /items/abc → GetItemRequest { Id = "abc" }
```

Parameter names are matched case-insensitively against route segments (`{id}`) and query parameters (`?id=abc`).

**Classes with setters:**

```csharp
public class SearchRequest
{
    public string Query { get; set; } = default!;
    public int Page { get; set; } = 1;
}

// GET /search?query=hello&page=2 → SearchRequest { Query = "hello", Page = 2 }
```

### POST / PUT / PATCH — JSON Body

For POST, PUT, and PATCH requests, the request object is deserialized from the JSON body:

```csharp
public record CreateItemRequest(string Name, decimal Price);

// POST /items with body {"name": "Widget", "price": 9.99}
// → CreateItemRequest { Name = "Widget", Price = 9.99 }
```

JSON uses `camelCase` naming policy by default (matching AWS conventions).

---

## Dependency Injection

Functions receive dependencies via constructor injection. The framework registers all function types as `Transient` services automatically.

```csharp
[HttpApi(Method.Post, "/orders")]
public class CreateOrder(
    ITable<Order> orders,         // Provided by runtime (InMemory locally, DynamoDB on AWS)
    ILogger<CreateOrder> logger,  // Provided by runtime
    IEmailService emailService    // Must be registered in builder.Services
) : IHttpFunction<CreateOrderRequest, CreateOrderResponse>
{
    public async Task<CreateOrderResponse> HandleAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        logger.LogInformation("Creating order for {Customer}", request.CustomerName);
        // ...
    }
}
```

Register custom services in `Program.cs`:

```csharp
var builder = CloudApplication.CreateBuilder(args);
builder.Services.AddSingleton<IEmailService, SmtpEmailService>();
// ...
```

### DI Validation

At build time, the framework validates that all constructor parameters are resolvable:

- `ITable<T>` — automatically provided by the runtime (skipped)
- `ILogger<T>` — automatically provided by the runtime (skipped)
- Everything else — must be registered in `builder.Services`

If any dependencies are missing, a `DependencyValidationException` is thrown with a clear error message listing all missing services.

---

## Error Handling

Exceptions thrown in `HandleAsync` are automatically mapped to HTTP status codes:

| Exception | Local (Kestrel) | Lambda |
|-----------|----------------|--------|
| `KeyNotFoundException` | 404 | 404 |
| `JsonException` | 400 | 400 |
| Any other exception | 500 | 500 |

Error responses are JSON:

```json
{
  "error": "Order abc123 not found"
}
```

---

## Per-Function Configuration

### Attribute-Based (L1)

Use `[FunctionConfig]` to set Lambda memory and timeout per function:

```csharp
[HttpApi(Method.Post, "/process")]
[FunctionConfig(MemoryMb = 1024, TimeoutSeconds = 60)]
public class ProcessData : IHttpFunction<ProcessRequest, ProcessResponse>
{
    // This function gets 1GB memory and 60s timeout
}
```

### Fluent API (L2)

Override configuration in the builder:

```csharp
builder.AddFunction<ProcessData>(opts =>
{
    opts.MemoryMb = 2048;
    opts.TimeoutSeconds = 120;
});
```

### Defaults

Set defaults for all functions:

```csharp
builder.ConfigureDefaults(d =>
{
    d.Lambda.MemoryMb = 512;
    d.Lambda.TimeoutSeconds = 30;
});
```

**Priority**: Fluent API > Attribute > Defaults

---

## Discovery

Functions are discovered automatically via assembly scanning:

```csharp
// Scan the entry assembly (most common)
builder.AddFunctions().FromAssembly();

// Scan a specific assembly
builder.AddFunctions().FromAssembly(typeof(MyFunction).Assembly);

// Scan with a filter
builder.AddFunctions()
    .FromAssembly()
    .WithFilter(type => type.Namespace?.StartsWith("MyApp.Functions") == true);
```

The scanner looks for types that:
1. Are concrete (not abstract, not interface)
2. Have an `[HttpApi]` attribute
3. Implement `IHttpFunction<TRequest, TResponse>`

---

## Listing Functions

Use the `list` command to see all discovered functions and tables:

```bash
dotnet run -- list
```

```
=== CdkReloaded Resources ===

Functions (3):
  Post    /orders                        -> CreateOrder
  Get     /orders/{id}                   -> GetOrder
  Get     /orders                        -> ListOrders

Tables (1):
  Order
```
