# Configuration

CDK Reloaded follows a layered configuration model: global defaults, attribute-based per-resource config, and fluent API overrides.

---

## Configuration Layers

```
Priority (highest to lowest):
1. Fluent API       builder.AddFunction<T>(opts => { ... })
2. Attributes       [FunctionConfig(MemoryMb = 1024)]
3. Defaults         builder.ConfigureDefaults(d => { ... })
4. Built-in         256 MB, 30s timeout
```

---

## Global Defaults

Set defaults for all Lambda functions and DynamoDB tables:

```csharp
var builder = CloudApplication.CreateBuilder(args);

builder.ConfigureDefaults(defaults =>
{
    // Lambda defaults
    defaults.Lambda.MemoryMb = 512;          // Default: 256
    defaults.Lambda.TimeoutSeconds = 60;     // Default: 30

    // DynamoDB defaults
    defaults.DynamoDb.BillingMode = "PayPerRequest";  // Default: PayPerRequest
});
```

---

## Per-Function Configuration

### Via Attribute (L1 — Declarative)

Apply `[FunctionConfig]` directly on the function class:

```csharp
[HttpApi(Method.Post, "/process-image")]
[FunctionConfig(MemoryMb = 2048, TimeoutSeconds = 300)]
public class ProcessImage : IHttpFunction<ProcessImageRequest, ProcessImageResponse>
{
    // Gets 2GB RAM and 5 minute timeout
}
```

This is the simplest approach and keeps configuration close to the code.

### Via Fluent API (L2 — Programmatic)

Override configuration in the builder:

```csharp
builder.AddFunction<ProcessImage>(opts =>
{
    opts.MemoryMb = 4096;
    opts.TimeoutSeconds = 600;
});
```

The fluent API takes precedence over attributes.

---

## Per-Table Configuration

### Via Attribute

```csharp
[TableName("production-orders")]
public class Order : ITableEntity
{
    [PartitionKey] public string Id { get; set; } = default!;
}
```

### Via Fluent API

```csharp
builder.AddTable<Order>(opts =>
{
    opts.TableName = "production-orders";
});
```

---

## Custom Services

Register your own services in the builder's DI container:

```csharp
var builder = CloudApplication.CreateBuilder(args);

// Register services available in all functions
builder.Services.AddSingleton<IEmailService, SmtpEmailService>();
builder.Services.AddHttpClient<IPaymentGateway, StripePaymentGateway>();
builder.Services.AddScoped<IOrderValidator, OrderValidator>();
```

These services are available in all execution modes (local, Lambda, deploy).

---

## Configuration Reference

### CloudDefaults

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Lambda.MemoryMb` | `int` | `256` | Default Lambda memory (MB) |
| `Lambda.TimeoutSeconds` | `int` | `30` | Default Lambda timeout (seconds) |
| `DynamoDb.BillingMode` | `string` | `"PayPerRequest"` | DynamoDB billing mode |

### FunctionOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MemoryMb` | `int?` | `null` (uses default) | Lambda memory override |
| `TimeoutSeconds` | `int?` | `null` (uses default) | Lambda timeout override |

### FunctionConfigAttribute

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MemoryMb` | `int` | `256` | Lambda memory |
| `TimeoutSeconds` | `int` | `30` | Lambda timeout |

### TableOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TableName` | `string?` | `null` (auto-resolved) | Custom DynamoDB table name |

---

## Builder API Reference

```csharp
var builder = CloudApplication.CreateBuilder(args);

// Discovery
builder.AddFunctions().FromAssembly();              // Scan entry assembly
builder.AddFunctions().FromAssembly(assembly);       // Scan specific assembly
builder.AddFunctions().WithFilter(type => ...);      // Filter discovered types
builder.AddTables().FromAssembly();                  // Scan for ITableEntity types

// Explicit registration with config
builder.AddFunction<MyFunc>(opts => { ... });
builder.AddTable<MyEntity>(opts => { ... });

// Global defaults
builder.ConfigureDefaults(d => { ... });

// DI container
builder.Services.AddSingleton<IMyService, MyService>();

// Runtime override (rarely needed)
builder.UseRuntime(new CustomRuntime());

// Build
var app = builder.Build();
```
