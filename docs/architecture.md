# Architecture

CDK Reloaded is built on a layered architecture where each package has a single responsibility. The key insight: **your application code never references AWS-specific packages** — the framework handles the mapping.

---

## Package Dependency Graph

```
                    CdkReloaded (meta-package)
                    /     |      |        \
                   /      |      |         \
    Runtime.Local  Runtime.Lambda  Cdk    DynamoDb
          \            |         /       /
           \           |        /       /
            +----- Hosting ----+-------+
                       |
                  Abstractions
```

- **Abstractions**: Zero-dependency interfaces and attributes (`IFunction`, `ITable<T>`, `[HttpApi]`)
- **Hosting**: Builder, discovery engine, runtime abstraction, CLI dispatch
- **DynamoDb**: `InMemoryTable<T>` (local) and `DynamoDbTable<T>` (AWS) implementations
- **Runtime.Local**: ASP.NET Kestrel server that maps functions to HTTP endpoints
- **Runtime.Lambda**: AWS Lambda custom runtime bootstrap
- **Cdk**: CDK stack generator and deploy orchestration

---

## The Build Pipeline

When `CloudApplicationBuilder.Build()` is called, this happens:

```
1. DetectModeAndCommand(args)
   ├── "deploy"  → (Deploy, Deploy)
   ├── "synth"   → (Deploy, Synth)
   ├── "destroy" → (Deploy, Destroy)
   ├── "diff"    → (Deploy, Diff)
   ├── "list"    → (Local, List)
   ├── AWS_LAMBDA_RUNTIME_API set → (Lambda, None)
   └── default   → (Local, None)

2. Discovery Phase
   ├── Scan assembly for [HttpApi] + IHttpFunction<,> → FunctionRegistration[]
   └── Scan assembly for ITableEntity → TableRegistration[]

3. Configuration Phase
   ├── Apply explicit AddFunction<T>(opts) overrides
   └── Apply explicit AddTable<T>(opts) overrides

4. DI Validation (skipped for "list" command)
   ├── For each function's constructor parameters:
   │   ├── ITable<T> → skip (provided by runtime)
   │   ├── ILogger<T> → skip (provided by runtime)
   │   └── Other → must be in builder.Services
   └── Throw DependencyValidationException if any missing

5. Runtime Resolution
   ├── Explicit: builder.UseRuntime(...)
   ├── Static registry: RuntimeRegistry.Create(mode)
   └── Assembly scanning: [RuntimeProvider] attribute on CdkReloaded.*.dll
```

---

## Runtime Auto-Discovery

The framework uses a two-stage runtime discovery mechanism:

### Stage 1: Static Registry

Runtime packages can register themselves via `RuntimeRegistry.Register()`:

```csharp
RuntimeRegistry.Register(ExecutionMode.Local, () => new LocalRuntime());
```

### Stage 2: Assembly Scanning

If no runtime was found in the registry, the framework scans all `CdkReloaded.*.dll` assemblies in the application's base directory for the `[RuntimeProvider]` attribute:

```csharp
[assembly: RuntimeProvider(typeof(LocalRuntime), ExecutionMode.Local)]
```

This uses `Assembly.LoadFrom()` (not `AssemblyName.GetAssemblyName()`) to avoid ICU-related crashes on Lambda AL2023.

---

## Execution Modes

### Local Mode

```
CloudApplication.RunAsync()
    → LocalRuntime.RunAsync()
        → WebApplication.CreateBuilder()
        → Register InMemoryTable<T> for each table
        → Register function types as transient services
        → For each function:
        │   MapGet/MapPost/MapPut/MapDelete/MapPatch
        │   → CreateHandler(func)
        │       → Resolve function from DI
        │       → Bind request from route/query/body
        │       → Invoke HandleAsync via reflection
        │       → Return JSON response
        └── app.RunAsync()
```

Request binding:
- **GET/DELETE**: Route parameters + query string → constructor parameters (records) or property setters
- **POST/PUT/PATCH**: JSON body → `System.Text.Json.Deserialize`

Error handling:
- `KeyNotFoundException` → 404
- `JsonException` → 400
- Everything else → 500

### Lambda Mode

```
CloudApplication.RunAsync()
    → LambdaRuntime.RunAsync()
        → Build ServiceProvider with DynamoDbTable<T>
        → Read CDK_RELOADED_FUNCTION_TYPE env var
        → Find matching FunctionRegistration
        → LambdaBootstrapBuilder.Create(handler)
        → handler receives APIGatewayHttpApiV2ProxyRequest
        │   → Bind from PathParameters + QueryStringParameters or Body
        │   → Invoke HandleAsync
        │   → Return APIGatewayHttpApiV2ProxyResponse
        └── bootstrap.RunAsync()
```

Each Lambda function instance handles exactly **one function type** — the `CDK_RELOADED_FUNCTION_TYPE` environment variable (set by CDK) tells the runtime which function to dispatch to.

### Deploy Mode

```
CloudApplication.RunAsync()
    → CdkDeployRuntime.RunAsync()
        → CheckPrerequisites (dotnet, npx)
        → Based on command:
        │
        ├── Deploy:
        │   1. dotnet publish -r linux-arm64 --self-contained
        │   2. Rename executable → "bootstrap"
        │   3. CdkStackGenerator.Generate() → CDK App
        │   4. app.Synth() → cdk.out/
        │   5. npx cdk deploy --app "cdk.out"
        │
        ├── Synth: Steps 1-4 only
        ├── Diff:  Steps 1-4 + npx cdk diff
        └── Destroy: Steps 1-4 + npx cdk destroy --force
```

---

## CDK Stack Generation

`CdkStackGenerator` reads the `CloudApplicationContext` and generates:

```
CDK Stack
├── DynamoDB Tables
│   ├── Partition key from [PartitionKey] property
│   ├── Sort key from [SortKey] property (optional)
│   ├── PAY_PER_REQUEST billing
│   └── DESTROY removal policy
│
├── Lambda Functions (one per [HttpApi] function)
│   ├── Runtime: PROVIDED_AL2023 (custom runtime)
│   ├── Architecture: ARM_64
│   ├── Code: FromAsset(publishDir)
│   ├── Handler: "bootstrap"
│   ├── Environment:
│   │   ├── CDK_RELOADED_FUNCTION_TYPE = function class name
│   │   └── TABLE_{ENTITY} = DynamoDB table name
│   └── Memory/Timeout from [FunctionConfig] or defaults
│
├── HTTP API Gateway
│   └── Routes wired to Lambda integrations
│
└── CfnOutput: API URL
```

Property names are camelCased for DynamoDB attribute names (matching `System.Text.Json`'s `CamelCase` naming policy).

---

## Service Flow

The DI container setup flows through a list of `Action<IServiceCollection>` configurators:

```
CloudApplicationBuilder.Build()
    ├── User's builder.Services registrations
    └── Function type registrations (AddTransient)

         ↓ passed to runtime

LocalRuntime.RunAsync()
    ├── Applies configurators to WebApplicationBuilder.Services
    └── Adds InMemoryTable<T> for each table entity

LambdaRuntime.RunAsync()
    ├── Applies configurators to new ServiceCollection
    └── Adds DynamoDbTable<T> + IAmazonDynamoDB for each table entity
```

This design ensures that user-registered services (custom repositories, HTTP clients, etc.) are available in all execution modes.

---

## Key Type Overview

| Type | Package | Purpose |
|------|---------|---------|
| `IHttpFunction<TReq, TRes>` | Abstractions | Function contract |
| `ITable<T>` | Abstractions | DynamoDB abstraction |
| `ITableEntity` | Abstractions | Table entity marker |
| `[HttpApi]` | Abstractions | Route + method metadata |
| `[PartitionKey]` / `[SortKey]` | Abstractions | Key schema metadata |
| `[FunctionConfig]` | Abstractions | Per-function resource config |
| `CloudApplication` | Hosting | Entry point (`CreateBuilder` + `Run`) |
| `CloudApplicationBuilder` | Hosting | Discovery + build pipeline |
| `CloudApplicationContext` | Hosting | Immutable resource snapshot |
| `IRuntime` | Hosting | Runtime contract |
| `InMemoryTable<T>` | DynamoDb | Local development table |
| `DynamoDbTable<T>` | DynamoDb | Real AWS DynamoDB table |
| `LocalRuntime` | Runtime.Local | Kestrel server |
| `LambdaRuntime` | Runtime.Lambda | Lambda bootstrap |
| `CdkDeployRuntime` | Cdk | CDK deploy orchestration |
| `CdkStackGenerator` | Cdk | CDK construct generation |
