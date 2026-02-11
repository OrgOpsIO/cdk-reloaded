using System.Collections.Concurrent;
using System.Reflection;
using CdkReloaded.Abstractions;

namespace CdkReloaded.DynamoDb;

public class InMemoryTable<T> : ITable<T> where T : ITableEntity
{
    private readonly ConcurrentDictionary<string, T> _store = new();
    private readonly PropertyInfo _partitionKeyProperty;
    private readonly PropertyInfo? _sortKeyProperty;

    public InMemoryTable()
    {
        _partitionKeyProperty = FindKeyProperty<PartitionKeyAttribute>()
            ?? throw new InvalidOperationException(
                $"Entity type {typeof(T).Name} must have a property decorated with [PartitionKey].");

        _sortKeyProperty = FindKeyProperty<SortKeyAttribute>();
    }

    public Task<T?> GetAsync(string partitionKey, CancellationToken ct = default)
    {
        var key = BuildKey(partitionKey, sortKey: null);
        _store.TryGetValue(key, out var entity);
        return Task.FromResult(entity);
    }

    public Task<T?> GetAsync(string partitionKey, string sortKey, CancellationToken ct = default)
    {
        var key = BuildKey(partitionKey, sortKey);
        _store.TryGetValue(key, out var entity);
        return Task.FromResult(entity);
    }

    public Task PutAsync(T entity, CancellationToken ct = default)
    {
        var pk = GetPartitionKey(entity);
        var sk = GetSortKey(entity);
        var key = BuildKey(pk, sk);
        _store[key] = entity;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string partitionKey, CancellationToken ct = default)
    {
        // Delete all items with matching partition key by checking actual property values
        var keysToRemove = _store
            .Where(kvp => GetPartitionKey(kvp.Value) == partitionKey)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _store.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string partitionKey, string sortKey, CancellationToken ct = default)
    {
        var key = BuildKey(partitionKey, sortKey);
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<T>> QueryAsync(string partitionKey, CancellationToken ct = default)
    {
        var results = _store
            .Where(kvp => GetPartitionKey(kvp.Value) == partitionKey)
            .Select(kvp => kvp.Value)
            .ToList();

        return Task.FromResult<IReadOnlyList<T>>(results);
    }

    public Task<IReadOnlyList<T>> ScanAsync(CancellationToken ct = default)
    {
        var results = _store.Values.ToList();
        return Task.FromResult<IReadOnlyList<T>>(results);
    }

    private string GetPartitionKey(T entity) =>
        _partitionKeyProperty.GetValue(entity)?.ToString()
        ?? throw new InvalidOperationException("Partition key value cannot be null.");

    private string? GetSortKey(T entity) =>
        _sortKeyProperty?.GetValue(entity)?.ToString();

    private static string BuildKey(string partitionKey, string? sortKey) =>
        sortKey is not null ? $"{partitionKey}#{sortKey}" : partitionKey;

    private static PropertyInfo? FindKeyProperty<TAttribute>() where TAttribute : Attribute =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<TAttribute>() is not null);
}
