using System.Reflection;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using CdkReloaded.Abstractions;
using CdkReloaded.DynamoDb;
using CdkReloaded.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CdkReloaded.Runtime.Lambda;

public sealed class LambdaRuntime : IRuntime
{
    public async Task RunAsync(
        CloudApplicationContext context,
        IReadOnlyList<Action<IServiceCollection>> serviceConfigurators,
        CancellationToken ct)
    {
        // Build DI container
        var services = new ServiceCollection();
        services.AddLogging();
        foreach (var configurator in serviceConfigurators)
            configurator(services);

        // Register real DynamoDB tables
        services.AddDynamoDbTables(context.Tables.Select(t => t.EntityType));

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<LambdaRuntime>();

        // Resolve which function this Lambda instance handles
        var functionTypeName = Environment.GetEnvironmentVariable("CDK_RELOADED_FUNCTION_TYPE")
            ?? throw new FunctionInvocationException("unknown",
                "CDK_RELOADED_FUNCTION_TYPE environment variable not set. This Lambda was not deployed by CdkReloaded.",
                new InvalidOperationException("Missing CDK_RELOADED_FUNCTION_TYPE"));

        var targetFunction = context.Functions.FirstOrDefault(
            f => f.FunctionType.Name == functionTypeName)
            ?? throw new FunctionInvocationException(functionTypeName,
                $"Function type '{functionTypeName}' not found in discovered functions.",
                new InvalidOperationException($"Unknown function: {functionTypeName}"));

        logger.LogInformation("Lambda dispatching to function: {Function}", functionTypeName);

        // Create the Lambda handler
        var handler = CreateHandler(targetFunction, provider, logger);

        var serializer = new DefaultLambdaJsonSerializer();
        using var bootstrap = LambdaBootstrapBuilder
            .Create<APIGatewayHttpApiV2ProxyRequest, APIGatewayHttpApiV2ProxyResponse>(handler, serializer)
            .Build();

        await bootstrap.RunAsync(ct);
    }

    private static Func<APIGatewayHttpApiV2ProxyRequest, ILambdaContext, Task<APIGatewayHttpApiV2ProxyResponse>>
        CreateHandler(FunctionRegistration func, IServiceProvider provider, ILogger logger)
    {
        return async (apiEvent, lambdaContext) =>
        {
            try
            {
                var functionInstance = provider.GetRequiredService(func.FunctionType);
                object? request;

                if (func.HttpApi.Method is Method.Get or Method.Delete)
                {
                    request = BuildRequestFromEvent(func.RequestType, apiEvent);
                }
                else
                {
                    try
                    {
                        request = string.IsNullOrEmpty(apiEvent.Body)
                            ? Activator.CreateInstance(func.RequestType)
                            : JsonSerializer.Deserialize(apiEvent.Body, func.RequestType, JsonOptions);
                    }
                    catch (JsonException jsonEx)
                    {
                        logger.LogWarning("Deserialization error for {Function}: {Message}",
                            func.FunctionType.Name, jsonEx.Message);
                        return new APIGatewayHttpApiV2ProxyResponse
                        {
                            StatusCode = 400,
                            Body = JsonSerializer.Serialize(new { error = jsonEx.Message }),
                            Headers = JsonHeaders
                        };
                    }
                }

                if (request is null)
                {
                    return new APIGatewayHttpApiV2ProxyResponse
                    {
                        StatusCode = 400,
                        Body = JsonSerializer.Serialize(new { error = "Invalid request" }),
                        Headers = JsonHeaders
                    };
                }

                var handleMethod = func.FunctionType.GetMethod("HandleAsync")!;
                var task = (Task)handleMethod.Invoke(functionInstance, [request, CancellationToken.None])!;
                await task;

                var result = task.GetType().GetProperty("Result")!.GetValue(task);

                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = 200,
                    Body = JsonSerializer.Serialize(result, JsonOptions),
                    Headers = JsonHeaders
                };
            }
            catch (TargetInvocationException ex) when (ex.InnerException is KeyNotFoundException knf)
            {
                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = 404,
                    Body = JsonSerializer.Serialize(new { error = knf.Message }),
                    Headers = JsonHeaders
                };
            }
            catch (TargetInvocationException ex)
            {
                logger.LogError(ex, "Invocation error in {Function}", func.FunctionType.Name);
                throw new FunctionInvocationException(
                    func.FunctionType.Name,
                    $"Error invoking function '{func.FunctionType.Name}': {ex.InnerException?.Message ?? ex.Message}",
                    ex.InnerException ?? ex);
            }
            catch (FunctionInvocationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in {Function}", func.FunctionType.Name);
                lambdaContext.Logger.LogError($"Unhandled exception: {ex}");
                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = 500,
                    Body = JsonSerializer.Serialize(new { error = "Internal server error" }),
                    Headers = JsonHeaders
                };
            }
        };
    }

    private static object? BuildRequestFromEvent(Type requestType, APIGatewayHttpApiV2ProxyRequest apiEvent)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (apiEvent.PathParameters is not null)
        {
            foreach (var kv in apiEvent.PathParameters)
                values[kv.Key] = kv.Value;
        }

        if (apiEvent.QueryStringParameters is not null)
        {
            foreach (var kv in apiEvent.QueryStringParameters)
            {
                if (!values.ContainsKey(kv.Key))
                    values[kv.Key] = kv.Value;
            }
        }

        // Try parameterless constructor first
        var parameterlessCtor = requestType.GetConstructor(Type.EmptyTypes);
        if (parameterlessCtor is not null)
        {
            var instance = parameterlessCtor.Invoke([]);
            foreach (var prop in requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (values.TryGetValue(prop.Name, out var value) && prop.CanWrite)
                    prop.SetValue(instance, Convert.ChangeType(value, prop.PropertyType));
            }
            return instance;
        }

        // Fall back to constructor with parameters (records)
        var ctor = requestType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            if (values.TryGetValue(param.Name!, out var val))
                args[i] = Convert.ChangeType(val, param.ParameterType);
            else
                args[i] = param.HasDefaultValue ? param.DefaultValue
                    : (param.ParameterType.IsValueType ? Activator.CreateInstance(param.ParameterType) : null);
        }

        return ctor.Invoke(args);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Dictionary<string, string> JsonHeaders = new()
    {
        ["Content-Type"] = "application/json"
    };
}
