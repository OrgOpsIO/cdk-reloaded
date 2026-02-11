namespace CdkReloaded.Abstractions;

/// <summary>
/// Marker interface for DynamoDB table entities.
/// </summary>
public interface ITableEntity;

[AttributeUsage(AttributeTargets.Property)]
public class PartitionKeyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property)]
public class SortKeyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public class TableNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// DynamoDB table abstraction.
/// </summary>
public interface ITable<T> where T : ITableEntity
{
    Task<T?> GetAsync(string partitionKey, CancellationToken ct = default);
    Task<T?> GetAsync(string partitionKey, string sortKey, CancellationToken ct = default);
    Task PutAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(string partitionKey, CancellationToken ct = default);
    Task DeleteAsync(string partitionKey, string sortKey, CancellationToken ct = default);
    Task<IReadOnlyList<T>> QueryAsync(string partitionKey, CancellationToken ct = default);
    Task<IReadOnlyList<T>> ScanAsync(CancellationToken ct = default);
}
