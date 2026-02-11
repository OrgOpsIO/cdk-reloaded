using System.Reflection;
using Amazon.CDK;
using Amazon.CDK.AWS.Apigatewayv2;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AwsApigatewayv2Integrations;
using CdkReloaded.Abstractions;
using CdkReloaded.Hosting;
using Constructs;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace CdkReloaded.Cdk;

public class CdkStackGenerator
{
    private readonly CloudApplicationContext _context;
    private readonly string _publishDir;
    private readonly string? _outdir;

    public CdkStackGenerator(CloudApplicationContext context, string publishDir, string? outdir = null)
    {
        _context = context;
        _publishDir = publishDir;
        _outdir = outdir;
    }

    public App Generate()
    {
        var appProps = _outdir is not null ? new AppProps { Outdir = _outdir } : null;
        var app = new App(appProps);
        var stackName = ResolveStackName();
        var stack = new Stack(app, stackName);

        // Create DynamoDB tables
        var tables = new Dictionary<Type, Table>();
        foreach (var tableReg in _context.Tables)
        {
            var table = CreateTable(stack, tableReg);
            tables[tableReg.EntityType] = table;
        }

        // Create API Gateway HTTP API
        var httpApi = new HttpApi(stack, "HttpApi", new HttpApiProps
        {
            ApiName = stackName + "-Api"
        });

        // Create Lambda functions and wire them up
        foreach (var funcReg in _context.Functions)
        {
            var lambda = CreateLambdaFunction(stack, funcReg);

            // Grant DynamoDB access and pass table names as env vars
            foreach (var (entityType, table) in tables)
            {
                table.GrantReadWriteData(lambda);
                var envVarName = $"TABLE_{entityType.Name.ToUpperInvariant()}";
                lambda.AddEnvironment(envVarName, table.TableName);
            }

            // Add API Gateway route with Lambda integration
            var integration = new HttpLambdaIntegration(
                funcReg.FunctionType.Name + "Integration",
                lambda);

            httpApi.AddRoutes(new AddRoutesOptions
            {
                Path = funcReg.HttpApi.Route,
                Methods = [MapMethod(funcReg.HttpApi.Method)],
                Integration = integration
            });
        }

        // Output the API URL
        new CfnOutput(stack, "ApiUrl", new CfnOutputProps
        {
            Value = httpApi.Url ?? "N/A",
            Description = "HTTP API endpoint URL"
        });

        return app;
    }

    private Table CreateTable(Stack stack, TableRegistration tableReg)
    {
        var entityType = tableReg.EntityType;
        var tableName = ResolveTableName(entityType);
        var pkName = FindKeyPropertyName<PartitionKeyAttribute>(entityType)
            ?? throw new InvalidOperationException($"Entity {entityType.Name} must have a [PartitionKey] property.");
        var skName = FindKeyPropertyName<SortKeyAttribute>(entityType);

        var props = new TableProps
        {
            TableName = tableName,
            PartitionKey = new Attribute
            {
                Name = CamelCase(pkName),
                Type = AttributeType.STRING
            },
            BillingMode = BillingMode.PAY_PER_REQUEST,
            RemovalPolicy = RemovalPolicy.DESTROY
        };

        if (skName is not null)
        {
            props.SortKey = new Attribute
            {
                Name = CamelCase(skName),
                Type = AttributeType.STRING
            };
        }

        return new Table(stack, entityType.Name + "Table", props);
    }

    private Function CreateLambdaFunction(Stack stack, FunctionRegistration funcReg)
    {
        var memoryMb = funcReg.Options.MemoryMb ?? _context.Defaults.Lambda.MemoryMb;
        var timeoutSeconds = funcReg.Options.TimeoutSeconds ?? _context.Defaults.Lambda.TimeoutSeconds;

        // Check for L1 attribute overrides
        var configAttr = funcReg.FunctionType.GetCustomAttribute<FunctionConfigAttribute>();
        if (configAttr is not null)
        {
            memoryMb = configAttr.MemoryMb;
            timeoutSeconds = configAttr.TimeoutSeconds;
        }

        return new Function(stack, funcReg.FunctionType.Name, new FunctionProps
        {
            FunctionName = $"{ResolveStackName()}-{funcReg.FunctionType.Name}",
            Runtime = Runtime.PROVIDED_AL2023,
            Handler = "bootstrap",
            Code = Code.FromAsset(_publishDir),
            MemorySize = memoryMb,
            Timeout = Duration.Seconds(timeoutSeconds),
            Architecture = Architecture.ARM_64,
            Environment = new Dictionary<string, string>
            {
                ["CDK_RELOADED_FUNCTION_TYPE"] = funcReg.FunctionType.Name
            }
        });
    }

    private string ResolveStackName()
    {
        var entry = Assembly.GetEntryAssembly();
        var name = entry?.GetName().Name ?? "CdkReloadedApp";
        return name.Replace(".", "-").Replace("_", "-");
    }

    private static string ResolveTableName(Type entityType)
    {
        var attr = entityType.GetCustomAttribute<TableNameAttribute>();
        if (attr is not null) return attr.Name;

        var name = entityType.Name;
        return name.EndsWith('s') ? name : name + "s";
    }

    private static string? FindKeyPropertyName<TAttribute>(Type entityType) where TAttribute : System.Attribute =>
        entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<TAttribute>() is not null)
            ?.Name;

    private static Amazon.CDK.AWS.Apigatewayv2.HttpMethod MapMethod(Abstractions.Method method) => method switch
    {
        Abstractions.Method.Get => Amazon.CDK.AWS.Apigatewayv2.HttpMethod.GET,
        Abstractions.Method.Post => Amazon.CDK.AWS.Apigatewayv2.HttpMethod.POST,
        Abstractions.Method.Put => Amazon.CDK.AWS.Apigatewayv2.HttpMethod.PUT,
        Abstractions.Method.Delete => Amazon.CDK.AWS.Apigatewayv2.HttpMethod.DELETE,
        Abstractions.Method.Patch => Amazon.CDK.AWS.Apigatewayv2.HttpMethod.PATCH,
        _ => Amazon.CDK.AWS.Apigatewayv2.HttpMethod.ANY
    };

    private static string CamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
