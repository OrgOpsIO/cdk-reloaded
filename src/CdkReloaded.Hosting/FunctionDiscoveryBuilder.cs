using System.Reflection;
using CdkReloaded.Abstractions;

namespace CdkReloaded.Hosting;

public class FunctionDiscoveryBuilder
{
    private readonly CloudApplicationBuilder _appBuilder;
    private Assembly? _assembly;
    private Func<Type, bool>? _filter;

    internal FunctionDiscoveryBuilder(CloudApplicationBuilder appBuilder)
    {
        _appBuilder = appBuilder;
    }

    public FunctionDiscoveryBuilder FromAssembly(Assembly? assembly = null)
    {
        _assembly = assembly ?? Assembly.GetEntryAssembly();
        return this;
    }

    public FunctionDiscoveryBuilder WithFilter(Func<Type, bool> predicate)
    {
        _filter = predicate;
        return this;
    }

    public IReadOnlyList<FunctionRegistration> Discover()
    {
        var assembly = _assembly ?? Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException("No assembly specified and no entry assembly found.");

        var registrations = new List<FunctionRegistration>();

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            if (_filter is not null && !_filter(type))
                continue;

            var httpApiAttr = type.GetCustomAttribute<HttpApiAttribute>();
            if (httpApiAttr is null)
                continue;

            var httpFunctionInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHttpFunction<,>));

            if (httpFunctionInterface is null)
                continue;

            var genericArgs = httpFunctionInterface.GetGenericArguments();

            registrations.Add(new FunctionRegistration
            {
                FunctionType = type,
                RequestType = genericArgs[0],
                ResponseType = genericArgs[1],
                HttpApi = httpApiAttr
            });
        }

        return registrations;
    }
}
