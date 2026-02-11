# Error Handling

CDK Reloaded provides structured error handling at every layer — from build-time validation to runtime error responses.

---

## Exception Hierarchy

```
Exception
  └── CdkReloadedException              (base for all framework exceptions)
        ├── FunctionInvocationException  (function execution errors)
        ├── DeploymentException          (CDK deploy pipeline errors)
        └── DependencyValidationException (missing DI registrations)
```

---

## Build-Time Validation

### DependencyValidationException

Thrown by `CloudApplicationBuilder.Build()` when a function's constructor dependencies are not registered.

```
Missing service registrations:
  - CreateOrder requires IEmailService (parameter 'emailService')
  - ProcessPayment requires IPaymentGateway (parameter 'gateway')
```

**Automatically skipped types:**
- `ITable<T>` — provided by the runtime (InMemory or DynamoDB)
- `ILogger<T>` — provided by the runtime's DI container

**Fix:** Register missing services in `builder.Services`:

```csharp
builder.Services.AddSingleton<IEmailService, SmtpEmailService>();
builder.Services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
```

**Note:** Validation is skipped for the `list` command, so `dotnet run -- list` always works regardless of DI state.

---

## Runtime Error Handling

### Local Runtime (Kestrel)

Exceptions thrown in `HandleAsync` are automatically mapped to HTTP responses:

| Exception | Status | Response |
|-----------|--------|----------|
| `KeyNotFoundException` | 404 | `{"error": "Item not found"}` |
| `JsonException` | 400 | `{"error": "Invalid JSON..."}` |
| All other exceptions | 500 | `{"error": "Internal server error"}` |

All errors are logged via `ILogger<LocalRuntime>`:

```
warn: LocalRuntime[0] Not found: Order abc123 not found
error: LocalRuntime[0] Unhandled error in CreateOrder
```

### Lambda Runtime

Same error mapping as local, plus:

| Exception | Status | Response |
|-----------|--------|----------|
| `KeyNotFoundException` | 404 | `{"error": "..."}` |
| `JsonException` | 400 | `{"error": "..."}` |
| `TargetInvocationException` | Re-thrown as `FunctionInvocationException` |
| All other exceptions | 500 | `{"error": "Internal server error"}` |

Deserialization errors for the request body are caught separately and return 400 with the specific JSON parsing error.

Errors are logged to both `ILogger` and `ILambdaContext.Logger` (for CloudWatch).

---

## Deployment Errors

### DeploymentException

Thrown by `CdkDeployRuntime` with a `Stage` property indicating where the failure occurred:

| Stage | Cause |
|-------|-------|
| `prerequisites` | `dotnet` or `npx` not found/not working |
| `publish` | `dotnet publish` failed |
| `deploy` | `cdk deploy` failed |
| `destroy` | `cdk destroy` failed |
| `diff` | `cdk diff` failed |

```csharp
try
{
    app.Run();
}
catch (DeploymentException ex)
{
    Console.Error.WriteLine($"Deployment failed at stage '{ex.Stage}': {ex.Message}");
}
```

### Prerequisite Checks

Before any CDK operation, the runtime verifies that required tools are available:

```
dotnet --version    → Must succeed
npx --version       → Must succeed
```

If either is missing, a `DeploymentException` with stage `"prerequisites"` is thrown immediately, before any work is done.

---

## FunctionInvocationException

Thrown when a function fails during reflection-based invocation in the Lambda runtime:

```csharp
catch (FunctionInvocationException ex)
{
    Console.Error.WriteLine($"Function '{ex.FunctionName}' failed: {ex.Message}");
    // ex.InnerException contains the original error
}
```

This wraps `TargetInvocationException` from `MethodInfo.Invoke()` to provide context about which function failed.

---

## Error Response Format

All error responses across runtimes use a consistent JSON format:

```json
{
  "error": "Description of the error"
}
```

With `Content-Type: application/json` header.

---

## Best Practices

### Throw KeyNotFoundException for missing resources

```csharp
var order = await orders.GetAsync(request.Id, ct)
    ?? throw new KeyNotFoundException($"Order {request.Id} not found");
```

This automatically returns 404 in both local and Lambda runtimes.

### Let the framework handle unexpected errors

Don't wrap your entire `HandleAsync` in try-catch. The framework's error handling ensures:
- Consistent error response format
- Proper logging
- Correct HTTP status codes
- No stack traces leaked to clients

### Use DI validation to catch wiring issues early

The build-time validation catches missing services before your app starts, not when the first request hits a function with a missing dependency.

---

## Logging

All runtimes use `Microsoft.Extensions.Logging.ILogger<T>`:

| Component | Log Points |
|-----------|------------|
| `CloudApplicationBuilder` | Mode detection, discovery results, DI validation, runtime resolution |
| `LocalRuntime` | Endpoint mapping, request errors |
| `LambdaRuntime` | Function dispatch, deserialization errors, invocation errors |
| `CdkDeployRuntime` | Deploy stages (publish, synth, deploy) |

Configure logging via standard ASP.NET configuration (local) or Lambda environment variables (AWS).
