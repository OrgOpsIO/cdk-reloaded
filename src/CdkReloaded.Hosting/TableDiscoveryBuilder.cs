using System.Reflection;
using CdkReloaded.Abstractions;

namespace CdkReloaded.Hosting;

public class TableDiscoveryBuilder
{
    private readonly CloudApplicationBuilder _appBuilder;
    private Assembly? _assembly;

    internal TableDiscoveryBuilder(CloudApplicationBuilder appBuilder)
    {
        _appBuilder = appBuilder;
    }

    public TableDiscoveryBuilder FromAssembly(Assembly? assembly = null)
    {
        _assembly = assembly ?? Assembly.GetEntryAssembly();
        return this;
    }

    public IReadOnlyList<TableRegistration> Discover()
    {
        var assembly = _assembly ?? Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException("No assembly specified and no entry assembly found.");

        var registrations = new List<TableRegistration>();
        var discoveredEntityTypes = new HashSet<Type>();

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            if (!typeof(ITableEntity).IsAssignableFrom(type))
                continue;

            if (!discoveredEntityTypes.Add(type))
                continue;

            registrations.Add(new TableRegistration
            {
                EntityType = type
            });
        }

        return registrations;
    }
}
