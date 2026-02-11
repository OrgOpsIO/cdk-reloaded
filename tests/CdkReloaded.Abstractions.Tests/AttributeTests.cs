using CdkReloaded.Abstractions;

namespace CdkReloaded.Abstractions.Tests;

public class AttributeTests
{
    [Fact]
    public void HttpApiAttribute_StoresMethodAndRoute()
    {
        var attr = new HttpApiAttribute(Method.Get, "/orders/{id}");

        Assert.Equal(Method.Get, attr.Method);
        Assert.Equal("/orders/{id}", attr.Route);
    }

    [Fact]
    public void FunctionConfigAttribute_HasDefaults()
    {
        var attr = new FunctionConfigAttribute();

        Assert.Equal(256, attr.MemoryMb);
        Assert.Equal(30, attr.TimeoutSeconds);
    }

    [Fact]
    public void FunctionConfigAttribute_AllowsCustomValues()
    {
        var attr = new FunctionConfigAttribute { MemoryMb = 1024, TimeoutSeconds = 60 };

        Assert.Equal(1024, attr.MemoryMb);
        Assert.Equal(60, attr.TimeoutSeconds);
    }

    [Fact]
    public void TableNameAttribute_StoresName()
    {
        var attr = new TableNameAttribute("custom-table");

        Assert.Equal("custom-table", attr.Name);
    }

    [Fact]
    public void PartitionKeyAttribute_CanBeAppliedToProperty()
    {
        var prop = typeof(TestEntity).GetProperty(nameof(TestEntity.Id))!;
        var attr = prop.GetCustomAttributes(typeof(PartitionKeyAttribute), false);

        Assert.Single(attr);
    }

    [Fact]
    public void SortKeyAttribute_CanBeAppliedToProperty()
    {
        var prop = typeof(TestEntity).GetProperty(nameof(TestEntity.Sk))!;
        var attr = prop.GetCustomAttributes(typeof(SortKeyAttribute), false);

        Assert.Single(attr);
    }

    private class TestEntity : ITableEntity
    {
        [PartitionKey] public string Id { get; set; } = default!;
        [SortKey] public string Sk { get; set; } = default!;
    }
}
