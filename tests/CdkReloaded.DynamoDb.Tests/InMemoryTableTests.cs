using CdkReloaded.Abstractions;
using CdkReloaded.DynamoDb;

namespace CdkReloaded.DynamoDb.Tests;

public class InMemoryTableTests
{
    private readonly InMemoryTable<TestItem> _table = new();

    [Fact]
    public async Task PutAndGet_RoundTrips()
    {
        var item = new TestItem { Id = "1", Name = "Test" };
        await _table.PutAsync(item);

        var result = await _table.GetAsync("1");

        Assert.NotNull(result);
        Assert.Equal("1", result.Id);
        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public async Task Get_ReturnsNull_WhenNotFound()
    {
        var result = await _table.GetAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task Put_OverwritesExisting()
    {
        await _table.PutAsync(new TestItem { Id = "1", Name = "Original" });
        await _table.PutAsync(new TestItem { Id = "1", Name = "Updated" });

        var result = await _table.GetAsync("1");

        Assert.NotNull(result);
        Assert.Equal("Updated", result.Name);
    }

    [Fact]
    public async Task Delete_RemovesItem()
    {
        await _table.PutAsync(new TestItem { Id = "1", Name = "Test" });
        await _table.DeleteAsync("1");

        var result = await _table.GetAsync("1");

        Assert.Null(result);
    }

    [Fact]
    public async Task Delete_DoesNotThrow_WhenNotFound()
    {
        await _table.DeleteAsync("nonexistent");
    }

    [Fact]
    public async Task Query_ReturnsMatchingItems()
    {
        await _table.PutAsync(new TestItem { Id = "pk1", Name = "A" });
        await _table.PutAsync(new TestItem { Id = "pk2", Name = "B" });

        var results = await _table.QueryAsync("pk1");

        Assert.Single(results);
        Assert.Equal("A", results[0].Name);
    }

    [Fact]
    public async Task Query_ReturnsEmpty_WhenNoneMatch()
    {
        var results = await _table.QueryAsync("nonexistent");

        Assert.Empty(results);
    }

    [Fact]
    public void Constructor_Throws_WhenNoPartitionKey()
    {
        Assert.Throws<InvalidOperationException>(() => new InMemoryTable<MissingKeyEntity>());
    }

    [Fact]
    public async Task Delete_DoesNotAffectSimilarKeys()
    {
        // Regression: "user1" delete should not delete "user10"
        await _table.PutAsync(new TestItem { Id = "user1", Name = "A" });
        await _table.PutAsync(new TestItem { Id = "user10", Name = "B" });

        await _table.DeleteAsync("user1");

        Assert.Null(await _table.GetAsync("user1"));
        Assert.NotNull(await _table.GetAsync("user10"));
    }
}

public class InMemoryTableWithSortKeyTests
{
    private readonly InMemoryTable<CompositeItem> _table = new();

    [Fact]
    public async Task PutAndGet_WithSortKey()
    {
        var item = new CompositeItem { Pk = "user1", Sk = "order1", Data = "first" };
        await _table.PutAsync(item);

        var result = await _table.GetAsync("user1", "order1");

        Assert.NotNull(result);
        Assert.Equal("first", result.Data);
    }

    [Fact]
    public async Task Query_ReturnsSamePartitionKey()
    {
        await _table.PutAsync(new CompositeItem { Pk = "user1", Sk = "order1", Data = "A" });
        await _table.PutAsync(new CompositeItem { Pk = "user1", Sk = "order2", Data = "B" });
        await _table.PutAsync(new CompositeItem { Pk = "user2", Sk = "order3", Data = "C" });

        var results = await _table.QueryAsync("user1");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task MultipleSortKeys_SamePartition()
    {
        await _table.PutAsync(new CompositeItem { Pk = "user1", Sk = "a", Data = "First" });
        await _table.PutAsync(new CompositeItem { Pk = "user1", Sk = "b", Data = "Second" });

        var first = await _table.GetAsync("user1", "a");
        var second = await _table.GetAsync("user1", "b");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("First", first.Data);
        Assert.Equal("Second", second.Data);
    }

    [Fact]
    public async Task DeleteAsync_WithSortKey_RemovesSingleItem()
    {
        await _table.PutAsync(new CompositeItem { Pk = "user1", Sk = "a", Data = "First" });
        await _table.PutAsync(new CompositeItem { Pk = "user1", Sk = "b", Data = "Second" });

        await _table.DeleteAsync("user1", "a");

        Assert.Null(await _table.GetAsync("user1", "a"));
        Assert.NotNull(await _table.GetAsync("user1", "b"));
    }

    [Fact]
    public async Task DeleteAsync_ByPartitionKey_RemovesAllSortKeys()
    {
        await _table.PutAsync(new CompositeItem { Pk = "user1", Sk = "a", Data = "First" });
        await _table.PutAsync(new CompositeItem { Pk = "user1", Sk = "b", Data = "Second" });
        await _table.PutAsync(new CompositeItem { Pk = "user2", Sk = "c", Data = "Third" });

        await _table.DeleteAsync("user1");

        var remaining = await _table.ScanAsync();
        Assert.Single(remaining);
        Assert.Equal("user2", remaining[0].Pk);
    }

    [Fact]
    public async Task DeleteAsync_WithSortKey_DoesNotThrow_WhenNotFound()
    {
        await _table.DeleteAsync("nonexistent", "key");
    }
}

// Test fixtures
public class TestItem : ITableEntity
{
    [PartitionKey] public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
}

public class CompositeItem : ITableEntity
{
    [PartitionKey] public string Pk { get; set; } = default!;
    [SortKey] public string Sk { get; set; } = default!;
    public string Data { get; set; } = default!;
}

public class MissingKeyEntity : ITableEntity
{
    public string Id { get; set; } = default!;
}
