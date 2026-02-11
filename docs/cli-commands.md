# CLI Commands

CDK Reloaded uses the same `dotnet run` entry point for all operations. Commands are passed after `--`.

---

## Command Overview

| Command | Mode | Description |
|---------|------|-------------|
| *(none)* | Local | Start local development server |
| `deploy` | Deploy | Build, synthesize, deploy to AWS |
| `synth` | Deploy | Generate CloudFormation (no deploy) |
| `diff` | Deploy | Compare local with deployed stack |
| `destroy` | Deploy | Delete the deployed AWS stack |
| `list` | Local | Show discovered functions and tables |

---

## `dotnet run` — Local Development

```bash
dotnet run
```

Starts a Kestrel web server with:
- All discovered functions mapped to HTTP endpoints
- In-memory DynamoDB tables (data resets on restart)
- Full ASP.NET logging and diagnostics

The server binds to `http://localhost:5000` by default. Override with:

```bash
dotnet run --urls http://localhost:8080
```

---

## `dotnet run -- deploy` — Deploy to AWS

```bash
dotnet run -- deploy
```

Performs the full deployment pipeline:

```
[1/3] Publishing for AWS Lambda (linux-arm64)...
      → dotnet publish -c Release -r linux-arm64 --self-contained
      → Renames executable to "bootstrap" (Lambda custom runtime)

[2/3] Synthesizing CDK stack...
      → Generates CloudFormation from discovered resources
      → Outputs to cdk.out/

[3/3] Deploying to AWS...
      → npx cdk deploy --require-approval never
      → Streams CloudFormation events in real-time
      → Saves stack outputs to cdk-outputs.json
```

### Prerequisites

- `dotnet` CLI available
- `npx` (Node.js) available
- AWS credentials configured (`aws configure` or environment variables)
- CDK bootstrapped in target account/region (`npx cdk bootstrap`)

### Stack Naming

The stack name is derived from the entry assembly name:
- `CdkReloaded.Sample.OrderApi` → `CdkReloaded-Sample-OrderApi`
- Dots and underscores are replaced with hyphens

---

## `dotnet run -- synth` — Synthesize Only

```bash
dotnet run -- synth
```

Publishes the project and generates the CloudFormation template without deploying:

```
[1/2] Publishing for AWS Lambda (linux-arm64)...
[2/2] Synthesizing CDK stack...
      CloudFormation template generated in /path/to/cdk.out
```

Useful for:
- Inspecting the generated CloudFormation template
- CI/CD pipelines where synth and deploy are separate steps
- Verifying infrastructure changes before deploying

The generated template is in `cdk.out/` in your project directory.

---

## `dotnet run -- diff` — Show Changes

```bash
dotnet run -- diff
```

Publishes, synthesizes, then compares the generated stack with what's currently deployed:

```
[1/2] Publishing...
[2/2] Synthesizing...
Comparing with deployed stack...
      Stack CdkReloaded-Sample-OrderApi
      Resources
      [+] AWS::Lambda::Function NewFunction
      [~] AWS::DynamoDB::Table OrderTable
       └── [+] GlobalSecondaryIndexes
```

Useful for reviewing infrastructure changes before deploying.

---

## `dotnet run -- destroy` — Tear Down

```bash
dotnet run -- destroy
```

Synthesizes the stack, then runs `cdk destroy --force`:

```
[1/2] Publishing...
[2/2] Synthesizing...
Destroying stack...
      CdkReloaded-Sample-OrderApi: destroying...
      CdkReloaded-Sample-OrderApi: destroyed
=== Stack destroyed! ===
```

**Warning**: This permanently deletes all AWS resources including DynamoDB tables and their data. The `--force` flag skips the confirmation prompt.

---

## `dotnet run -- list` — List Resources

```bash
dotnet run -- list
```

Lists all discovered functions and tables without starting a server or contacting AWS:

```
=== CdkReloaded Resources ===

Functions (3):
  Post    /orders                        -> CreateOrder
  Get     /orders/{id}                   -> GetOrder
  Get     /orders                        -> ListOrders

Tables (1):
  Order
```

This command:
- Skips DI validation (so it always works)
- Doesn't need a runtime
- Useful for verifying discovery results

---

## Mode Detection

The framework determines the execution mode from command-line arguments and environment variables:

```
Arguments contain "deploy"    → Deploy mode, Deploy command
Arguments contain "synth"     → Deploy mode, Synth command
Arguments contain "destroy"   → Deploy mode, Destroy command
Arguments contain "diff"      → Deploy mode, Diff command
Arguments contain "list"      → Local mode, List command
AWS_LAMBDA_RUNTIME_API is set → Lambda mode (auto-detected on AWS)
Default                       → Local mode
```

Lambda mode is never triggered from the CLI — it's automatically detected when running inside an AWS Lambda environment.
