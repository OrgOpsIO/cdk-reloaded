using CdkReloaded.Abstractions;
using CdkReloaded.Hosting;

namespace CdkReloaded.Hosting.Tests;

public class TableDiscoveryTests
{
    [Fact]
    public void Discover_FindsTableEntities()
    {
        var builder = CloudApplication.CreateBuilder([]);
        var discovery = builder.AddTables();
        discovery.FromAssembly(typeof(TableDiscoveryTests).Assembly);

        var registrations = discovery.Discover();

        Assert.Single(registrations);
        Assert.Equal(typeof(TestOrder), registrations[0].EntityType);
    }

    [Fact]
    public void Discover_IgnoresAbstractEntities()
    {
        var builder = CloudApplication.CreateBuilder([]);
        var discovery = builder.AddTables();
        discovery.FromAssembly(typeof(TableDiscoveryTests).Assembly);

        var registrations = discovery.Discover();

        Assert.DoesNotContain(registrations, r => r.EntityType == typeof(AbstractEntity));
    }
}

// Test fixtures
public class TestOrder : ITableEntity
{
    [PartitionKey] public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
}

public abstract class AbstractEntity : ITableEntity;
