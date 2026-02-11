# Deployment Guide

This guide covers deploying CDK Reloaded applications to AWS.

---

## Prerequisites

### 1. AWS CLI

Install and configure the AWS CLI:

```bash
aws configure
# Enter: Access Key ID, Secret Access Key, Region (e.g., eu-central-1)
```

### 2. Node.js

CDK CLI requires Node.js. Install from [nodejs.org](https://nodejs.org/).

Verify:
```bash
npx --version
```

### 3. CDK Bootstrap

If you've never used CDK in your AWS account/region, bootstrap it first:

```bash
npx cdk bootstrap aws://ACCOUNT_ID/REGION
# Example: npx cdk bootstrap aws://123456789012/eu-central-1
```

This creates an S3 bucket and IAM roles that CDK needs for deployments.

### 4. .NET 10 SDK

```bash
dotnet --version
# Should be 10.x
```

---

## First Deployment

```bash
cd your-project
dotnet run -- deploy
```

The framework:
1. **Publishes** your project as a self-contained `linux-arm64` binary
2. **Renames** the executable to `bootstrap` (required by Lambda custom runtime)
3. **Generates** a CDK stack from your discovered functions and tables
4. **Deploys** via `npx cdk deploy`

First deployment takes longer because CloudFormation creates all resources from scratch.

---

## What Gets Created

For a typical application with 3 functions and 1 table:

| Resource | Type | Details |
|----------|------|---------|
| HTTP API | `AWS::ApiGatewayV2::Api` | HTTP API Gateway |
| Lambda x3 | `AWS::Lambda::Function` | One per function, ARM64, PROVIDED_AL2023 |
| DynamoDB x1 | `AWS::DynamoDB::Table` | PAY_PER_REQUEST, DESTROY removal policy |
| IAM Roles | `AWS::IAM::Role` | Lambda execution roles with DynamoDB permissions |
| Integrations | `AWS::ApiGatewayV2::Integration` | Wiring API Gateway → Lambda |
| Routes | `AWS::ApiGatewayV2::Route` | HTTP method + path patterns |

### Lambda Configuration

Each Lambda function is configured:

```
Runtime:        PROVIDED_AL2023 (custom runtime)
Architecture:   ARM_64 (Graviton, cheaper + faster)
Handler:        bootstrap
Code:           Your published self-contained binary
Memory:         256 MB (configurable per-function)
Timeout:        30 seconds (configurable per-function)

Environment Variables:
  CDK_RELOADED_FUNCTION_TYPE = "CreateOrder"  (function class name)
  TABLE_ORDER = "Stack-OrderTable-ABC123"      (physical table name per entity)
```

### DynamoDB Tables

Tables are created from your `ITableEntity` definitions:

```
Billing:        PAY_PER_REQUEST (no capacity planning needed)
Removal Policy: DESTROY (table deleted when stack is destroyed)
Partition Key:  From [PartitionKey] property (camelCase)
Sort Key:       From [SortKey] property (camelCase, optional)
```

---

## Deployment Workflow

### Preview Changes

Before deploying, see what will change:

```bash
dotnet run -- diff
```

### Synthesize Only

Generate CloudFormation without deploying (useful for CI/CD):

```bash
dotnet run -- synth
# Output in ./cdk.out/
```

Inspect the generated template:
```bash
cat cdk.out/CdkReloaded-Sample-OrderApi.template.json | jq .
```

### Deploy

```bash
dotnet run -- deploy
```

The API URL is printed after deployment and saved to `cdk-outputs.json`:

```json
{
  "CdkReloaded-Sample-OrderApi": {
    "ApiUrl": "https://abc123.execute-api.eu-central-1.amazonaws.com/"
  }
}
```

### Destroy

Remove all resources:

```bash
dotnet run -- destroy
```

---

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Deploy
on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0'

      - name: Setup Node.js
        uses: actions/setup-node@v4
        with:
          node-version: '20'

      - name: Configure AWS
        uses: aws-actions/configure-aws-credentials@v4
        with:
          aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID }}
          aws-secret-access-key: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
          aws-region: eu-central-1

      - name: Deploy
        run: dotnet run --project samples/CdkReloaded.Sample.OrderApi -- deploy
```

---

## Updating Deployments

Subsequent deployments are incremental — CloudFormation only updates what changed:

- **New function added**: Creates Lambda + API Gateway route
- **Function code changed**: Updates Lambda code (fast, ~30s)
- **New table entity**: Creates DynamoDB table + grants permissions
- **Config changed**: Updates Lambda memory/timeout

```bash
# Make changes to your code, then:
dotnet run -- diff     # Preview
dotnet run -- deploy   # Apply
```

---

## Stack Naming

The CDK stack name is derived from your project's assembly name:

| Assembly Name | Stack Name |
|--------------|------------|
| `MyApi` | `MyApi` |
| `CdkReloaded.Sample.OrderApi` | `CdkReloaded-Sample-OrderApi` |
| `My.Cool_App` | `My-Cool-App` |

Lambda function names follow the pattern: `{StackName}-{FunctionClassName}`

---

## Troubleshooting

### "npx is required but not found"

Install Node.js: https://nodejs.org/

### "CDK deploy failed"

1. Check AWS credentials: `aws sts get-caller-identity`
2. Bootstrap CDK: `npx cdk bootstrap`
3. Check the CloudFormation console for detailed error messages

### "dotnet publish failed"

1. Ensure .NET 10 SDK is installed
2. Check your project builds: `dotnet build`
3. Verify no platform-specific packages that don't support `linux-arm64`

### Lambda returns 500

1. Check CloudWatch Logs for the Lambda function
2. The function name includes `CDK_RELOADED_FUNCTION_TYPE` — look for that in logs
3. Common causes: missing DynamoDB permissions, missing environment variables

---

## Cost Considerations

CDK Reloaded deploys with cost-efficient defaults:

| Resource | Pricing Model | Free Tier |
|----------|--------------|-----------|
| Lambda | Per-request + duration | 1M requests/month |
| API Gateway | Per-request | 1M requests/month |
| DynamoDB | Per-request | 25 RCU + 25 WCU/month |

For most development and small production workloads, the entire stack falls within the AWS Free Tier.

ARM64 (Graviton) Lambdas are ~20% cheaper than x86 equivalents.
