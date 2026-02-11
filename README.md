# CDK Reloaded

**Write .NET functions. Get AWS infrastructure for free.**

CDK Reloaded is a .NET 10 framework that eliminates the gap between writing application code and deploying it to AWS. Define HTTP functions and DynamoDB tables with simple interfaces and attributes — the framework auto-generates Lambda functions, API Gateway routes, and DynamoDB tables via AWS CDK.

```
dotnet run           → Local Kestrel server with in-memory DynamoDB
dotnet run -- deploy → Publishes to Lambda, generates CDK, deploys to AWS
```

Zero CloudFormation. Zero YAML. Zero Terraform. Just C#.

---

## Quick Start

### 1. Create a new project

```bash
dotnet new web -n MyApi
cd MyApi
dotnet add package CdkReloaded.Net
```

### 2. Define a model

```csharp
using CdkReloaded.Abstractions;

public class Order : ITableEntity
{
    [PartitionKey] public string Id { get; set; } = default!;
    public string CustomerName { get; set; } = default!;
    public decimal Total { get; set; }
}
```

### 3. Write a function

```csharp
using CdkReloaded.Abstractions;

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

### 4. Wire it up

```csharp
using CdkReloaded.Hosting;

var builder = CloudApplication.CreateBuilder(args);
builder.AddFunctions().FromAssembly();
builder.AddTables().FromAssembly();

var app = builder.Build();
app.Run();
```

### 5. Run it

```bash
# Local development (Kestrel + in-memory DynamoDB)
dotnet run

# Deploy to AWS (Lambda + API Gateway + DynamoDB)
dotnet run -- deploy
```

That's it. The same code runs locally and on AWS.

---

## CLI Commands

| Command | Description |
|---------|-------------|
| `dotnet run` | Start local development server |
| `dotnet run -- deploy` | Build, synthesize CDK, deploy to AWS |
| `dotnet run -- synth` | Generate CloudFormation template without deploying |
| `dotnet run -- diff` | Show changes between local and deployed stack |
| `dotnet run -- destroy` | Remove the deployed AWS stack |
| `dotnet run -- list` | List all discovered functions and tables |

---

## How It Works

```
Your Code (.NET 10)
    |
    v
CloudApplicationBuilder.Build()
    ├── Discovers [HttpApi] functions via reflection
    ├── Discovers ITableEntity models
    ├── Validates DI dependencies
    └── Resolves runtime based on execution mode
           |
    ┌──────┼──────────────┐
    v      v              v
  Local   Lambda        CDK Deploy
  Runtime Runtime       Runtime
    |      |              |
    v      v              v
  Kestrel  AWS Lambda   dotnet publish
  + InMemory + DynamoDB + CDK synth
  Tables     Tables     + cdk deploy
```

**Three execution modes, one codebase:**

- **Local** (`dotnet run`): ASP.NET Kestrel server with in-memory DynamoDB tables
- **Lambda** (AWS): Lambda custom runtime with real DynamoDB
- **Deploy** (`dotnet run -- deploy`): Generates and deploys AWS CDK infrastructure

Runtimes are auto-discovered via assembly scanning — just add the NuGet package and the framework finds it.

---

## Documentation

| Document | Description |
|----------|-------------|
| [Getting Started](docs/getting-started.md) | Step-by-step tutorial |
| [Architecture](docs/architecture.md) | How the framework works internally |
| [Functions](docs/functions.md) | Writing HTTP functions |
| [DynamoDB](docs/dynamodb.md) | Table abstraction and data modeling |
| [Configuration](docs/configuration.md) | Defaults, per-function config, options |
| [CLI Reference](docs/cli-commands.md) | All CLI commands in detail |
| [Deployment](docs/deployment.md) | AWS deployment guide |
| [Error Handling](docs/error-handling.md) | Exception hierarchy and error responses |

---

## Project Structure

```
src/
  CdkReloaded.Abstractions/    # Interfaces, attributes (IFunction, ITable, [HttpApi])
  CdkReloaded.Hosting/          # Builder, discovery, CLI, runtime abstraction
  CdkReloaded.DynamoDb/         # InMemoryTable + DynamoDbTable implementations
  CdkReloaded.Runtime.Local/    # Kestrel-based local dev server
  CdkReloaded.Runtime.Lambda/   # AWS Lambda custom runtime
  CdkReloaded.Cdk/              # CDK stack generation + deploy
  CdkReloaded/                   # Meta-package (references all above)
samples/
  CdkReloaded.Sample.OrderApi/  # Complete example application
tests/
  CdkReloaded.*.Tests/          # Unit and integration tests
```

---

## Requirements

- .NET 10 SDK
- Node.js (for AWS CDK CLI, only needed for deployment)
- AWS CLI configured (only needed for deployment)

---

## Key Design Principles

1. **Convention over configuration** — Functions are discovered by scanning for `[HttpApi]` + `IHttpFunction<,>`. Tables are discovered by scanning for `ITableEntity`. No manual registration needed.

2. **Same code, everywhere** — Your function code doesn't know if it's running on Kestrel or Lambda. `ITable<T>` resolves to `InMemoryTable<T>` locally and `DynamoDbTable<T>` on AWS.

3. **Infrastructure from code** — The CDK runtime reads your function/table registrations and generates CloudFormation. No separate IaC files to maintain.

4. **Fail fast** — DI dependencies are validated at build time. Missing services throw `DependencyValidationException` before the app starts, not at request time.

---

## License

MIT
