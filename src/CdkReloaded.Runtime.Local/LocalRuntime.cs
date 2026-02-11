using System.Reflection;
using System.Text.Json;
using CdkReloaded.Abstractions;
using CdkReloaded.DynamoDb;
using CdkReloaded.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CdkReloaded.Runtime.Local;

public sealed class LocalRuntime : IRuntime
{
    public async Task RunAsync(
        CloudApplicationContext context,
        IReadOnlyList<Action<IServiceCollection>> serviceConfigurators,
        CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder(context.Args);

        // Apply all service registrations from the CloudApplicationBuilder
        foreach (var configurator in serviceConfigurators)
        {
            configurator(builder.Services);
        }

        // Register InMemoryTable<T> for all discovered table entities
        builder.Services.AddInMemoryTables(context.Tables.Select(t => t.EntityType));

        var app = builder.Build();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<LocalRuntime>();

        // Map each discovered HTTP function to an endpoint
        foreach (var func in context.Functions)
        {
            logger.LogInformation("Mapping {Method} {Route} -> {Function}",
                func.HttpApi.Method, func.HttpApi.Route, func.FunctionType.Name);
            MapFunction(app, func);
        }

        await app.RunAsync();
    }

    internal static void MapFunction(IEndpointRouteBuilder app, FunctionRegistration func)
    {
        var route = func.HttpApi.Route;
        var handler = CreateHandler(func);

        switch (func.HttpApi.Method)
        {
            case Method.Get:
                app.MapGet(route, handler);
                break;
            case Method.Post:
                app.MapPost(route, handler);
                break;
            case Method.Put:
                app.MapPut(route, handler);
                break;
            case Method.Delete:
                app.MapDelete(route, handler);
                break;
            case Method.Patch:
                app.MapPatch(route, handler);
                break;
        }
    }

    private static Delegate CreateHandler(FunctionRegistration func)
    {
        return async (HttpContext httpContext) =>
        {
            var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger<LocalRuntime>();

            try
            {
                var functionInstance = httpContext.RequestServices.GetRequiredService(func.FunctionType);

                object? request;

                if (func.HttpApi.Method is Method.Get or Method.Delete)
                {
                    request = BuildRequestFromRoute(func.RequestType, httpContext);
                }
                else
                {
                    request = await JsonSerializer.DeserializeAsync(
                        httpContext.Request.Body,
                        func.RequestType,
                        JsonOptions,
                        httpContext.RequestAborted);
                }

                if (request is null)
                {
                    httpContext.Response.StatusCode = 400;
                    await httpContext.Response.WriteAsJsonAsync(new { error = "Invalid request body" });
                    return;
                }

                var handleMethod = func.FunctionType.GetMethod("HandleAsync")
                    ?? throw new InvalidOperationException($"HandleAsync not found on {func.FunctionType.Name}");

                var task = (Task)handleMethod.Invoke(functionInstance, [request, httpContext.RequestAborted])!;
                await task;

                var resultProperty = task.GetType().GetProperty("Result")!;
                var result = resultProperty.GetValue(task);

                await httpContext.Response.WriteAsJsonAsync(result, JsonOptions, httpContext.RequestAborted);
            }
            catch (KeyNotFoundException knf)
            {
                logger.LogWarning("Not found: {Message}", knf.Message);
                httpContext.Response.StatusCode = 404;
                await httpContext.Response.WriteAsJsonAsync(new { error = knf.Message }, JsonOptions);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is KeyNotFoundException knf)
            {
                logger.LogWarning("Not found: {Message}", knf.Message);
                httpContext.Response.StatusCode = 404;
                await httpContext.Response.WriteAsJsonAsync(new { error = knf.Message }, JsonOptions);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is JsonException jsonEx)
            {
                logger.LogWarning("Bad request: {Message}", jsonEx.Message);
                httpContext.Response.StatusCode = 400;
                await httpContext.Response.WriteAsJsonAsync(new { error = jsonEx.Message }, JsonOptions);
            }
            catch (JsonException jsonEx)
            {
                logger.LogWarning("Bad request: {Message}", jsonEx.Message);
                httpContext.Response.StatusCode = 400;
                await httpContext.Response.WriteAsJsonAsync(new { error = jsonEx.Message }, JsonOptions);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in {Function}", func.FunctionType.Name);
                httpContext.Response.StatusCode = 500;
                await httpContext.Response.WriteAsJsonAsync(new { error = "Internal server error" }, JsonOptions);
            }
        };
    }

    private static object? BuildRequestFromRoute(Type requestType, HttpContext httpContext)
    {
        // Collect all available values from route and query string
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rv in httpContext.GetRouteData().Values)
        {
            if (rv.Value is not null)
                values[rv.Key] = rv.Value.ToString()!;
        }

        foreach (var q in httpContext.Request.Query)
        {
            if (!values.ContainsKey(q.Key) && q.Value.Count > 0)
                values[q.Key] = q.Value.First()!;
        }

        // Try parameterless constructor first (classes with setters)
        var parameterlessCtor = requestType.GetConstructor(Type.EmptyTypes);
        if (parameterlessCtor is not null)
        {
            var instance = parameterlessCtor.Invoke([]);

            foreach (var prop in requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (values.TryGetValue(prop.Name, out var value) && prop.CanWrite)
                {
                    prop.SetValue(instance, Convert.ChangeType(value, prop.PropertyType));
                }
            }

            return instance;
        }

        // Fall back to constructor with parameters (records / positional types)
        var ctors = requestType.GetConstructors();
        if (ctors.Length == 0) return null;

        // Pick the constructor with the most parameters we can satisfy
        var ctor = ctors
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            if (values.TryGetValue(param.Name!, out var val))
            {
                args[i] = Convert.ChangeType(val, param.ParameterType);
            }
            else
            {
                args[i] = param.HasDefaultValue ? param.DefaultValue : GetDefault(param.ParameterType);
            }
        }

        return ctor.Invoke(args);
    }

    private static object? GetDefault(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
