# DynamoDB Abstraction

CDK Reloaded provides a transparent DynamoDB abstraction that works identically in local development and on AWS. Your function code uses `ITable<T>` — the framework swaps the implementation based on execution mode.

---

## Defining a Table Entity

```csharp
using CdkReloaded.Abstractions;

public class Product : ITableEntity
{
    [PartitionKey]
    public string Id { get; set; } = default!;

    public string Name { get; set; } = default!;
    public decimal Price { get; set; }
    public int Stock { get; set; }
}
```

Requirements:
- Implement `ITableEntity` (marker interface)
- Exactly one property decorated with `[PartitionKey]`
- Optionally one property decorated with `[SortKey]`

---

## Composite Keys

For tables with a partition key + sort key:

```csharp
public class OrderItem : ITableEntity
{
    [PartitionKey]
    public string OrderId { get; set; } = default!;

    [SortKey]
    public string ProductId { get; set; } = default!;

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
```

---

## ITable\<T\> API

```csharp
public interface ITable<T> where T : ITableEntity
{
    // Single-item operations
    Task<T?> GetAsync(string partitionKey, CancellationToken ct = default);
    Task<T?> GetAsync(string partitionKey, string sortKey, CancellationToken ct = default);
    Task PutAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(string partitionKey, CancellationToken ct = default);
    Task DeleteAsync(string partitionKey, string sortKey, CancellationToken ct = default);

    // Multi-item operations
    Task<IReadOnlyList<T>> QueryAsync(string partitionKey, CancellationToken ct = default);
    Task<IReadOnlyList<T>> ScanAsync(CancellationToken ct = default);
}
```

### Usage Examples

```csharp
public class OrderService(ITable<Order> orders, ITable<OrderItem> items)
{
    // Get a single order
    var order = await orders.GetAsync("order-123", ct);

    // Get a specific item in an order (composite key)
    var item = await items.GetAsync("order-123", "product-456", ct);

    // Save an entity (insert or update)
    await orders.PutAsync(new Order { Id = "order-123", ... }, ct);

    // Delete by partition key (removes all sort keys too)
    await orders.DeleteAsync("order-123", ct);

    // Delete a specific item (composite key)
    await items.DeleteAsync("order-123", "product-456", ct);

    // Query: all items for an order
    var orderItems = await items.QueryAsync("order-123", ct);

    // Scan: all orders (use sparingly!)
    var allOrders = await orders.ScanAsync(ct);
}
```

---

## Implementations

### InMemoryTable\<T\> (Local Development)

Used automatically when running locally (`dotnet run`). Features:
- `ConcurrentDictionary`-backed storage
- Thread-safe
- Supports composite keys via `PK#SK` key format
- Full scan and query support
- No persistence (data lost on restart)

### DynamoDbTable\<T\> (AWS)

Used automatically on AWS Lambda. Features:
- Real AWS DynamoDB via `AWSSDK.DynamoDBv2`
- JSON serialization with `camelCase` naming
- Table name resolution (see below)

---

## Table Name Resolution

When deployed to AWS, the table name is resolved in this order:

1. **Environment variable**: `TABLE_{ENTITYNAME}` (uppercase)
   - Set automatically by CDK during deployment
   - Example: `TABLE_ORDER=CdkReloaded-Sample-OrderApi-OrderTable`

2. **Attribute**: `[TableName("custom-table-name")]`
   ```csharp
   [TableName("my-orders")]
   public class Order : ITableEntity { ... }
   ```

3. **Convention**: Entity name + "s"
   - `Order` → `Orders`
   - `OrderItem` → `OrderItems`
   - `Address` → `Addresss` (simple pluralization)

In practice, you almost never need to set this manually — CDK generates the table name and passes it via environment variables.

---

## Discovery

Tables are discovered by scanning for `ITableEntity` implementations:

```csharp
// Scan the entry assembly
builder.AddTables().FromAssembly();

// Or register explicitly
builder.AddTable<Order>();
builder.AddTable<OrderItem>();
```

### With Options

```csharp
builder.AddTable<Order>(opts =>
{
    opts.TableName = "custom-orders-table";
});
```

---

## CDK-Generated Infrastructure

For each discovered `ITableEntity`, CDK creates a DynamoDB table:

| Setting | Value |
|---------|-------|
| Partition Key | `[PartitionKey]` property name (camelCased) |
| Sort Key | `[SortKey]` property name (camelCased), if present |
| Billing Mode | `PAY_PER_REQUEST` |
| Removal Policy | `DESTROY` (for dev; configurable) |
| Key Type | `STRING` |

Each Lambda function gets:
- `ReadWriteData` permissions on all tables
- `TABLE_{ENTITYNAME}` environment variable with the physical table name

---

## JSON Serialization

Both `InMemoryTable<T>` and `DynamoDbTable<T>` use the same JSON settings:

```csharp
new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
};
```

This means C# property `CustomerName` becomes DynamoDB attribute `customerName`.

---

## Tips

- **Use `QueryAsync` over `ScanAsync`** — Scans read every item in the table and are expensive at scale
- **Don't query with empty partition key** — Use `ScanAsync()` instead if you need all items
- **Composite keys are powerful** — Model hierarchical data (orders → items, users → sessions) as partition key + sort key rather than separate tables
