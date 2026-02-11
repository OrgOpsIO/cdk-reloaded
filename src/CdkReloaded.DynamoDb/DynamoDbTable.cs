using System.Reflection;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using CdkReloaded.Abstractions;

namespace CdkReloaded.DynamoDb;

/// <summary>
/// Real AWS DynamoDB implementation of ITable&lt;T&gt;.
/// Table name is resolved from [TableName] attribute, TABLE_{TYPE} env var, or convention (type name + "s").
/// </summary>
public class DynamoDbTable<T> : ITable<T> where T : ITableEntity
{
    private readonly ITable _table;
    private readonly string _partitionKeyName;
    private readonly string? _sortKeyName;

    public DynamoDbTable(IAmazonDynamoDB dynamoDb)
    {
        var tableName = ResolveTableName();
        _table = Table.LoadTable(dynamoDb, tableName);
        _partitionKeyName = FindKeyPropertyName<PartitionKeyAttribute>()
            ?? throw new InvalidOperationException(
                $"Entity {typeof(T).Name} must have a [PartitionKey] property.");
        _sortKeyName = FindKeyPropertyName<SortKeyAttribute>();
    }

    public async Task<T?> GetAsync(string partitionKey, CancellationToken ct = default)
    {
        var doc = await _table.GetItemAsync(new Primitive(partitionKey), ct);
        return doc is null ? default : Deserialize(doc);
    }

    public async Task<T?> GetAsync(string partitionKey, string sortKey, CancellationToken ct = default)
    {
        var doc = await _table.GetItemAsync(new Primitive(partitionKey), new Primitive(sortKey), ct);
        return doc is null ? default : Deserialize(doc);
    }

    public async Task PutAsync(T entity, CancellationToken ct = default)
    {
        var doc = Serialize(entity);
        await _table.PutItemAsync(doc, ct);
    }

    public async Task DeleteAsync(string partitionKey, CancellationToken ct = default)
    {
        await _table.DeleteItemAsync(new Primitive(partitionKey), ct);
    }

    public async Task DeleteAsync(string partitionKey, string sortKey, CancellationToken ct = default)
    {
        await _table.DeleteItemAsync(new Primitive(partitionKey), new Primitive(sortKey), ct);
    }

    public async Task<IReadOnlyList<T>> QueryAsync(string partitionKey, CancellationToken ct = default)
    {
        var filter = new QueryFilter(_partitionKeyName, QueryOperator.Equal, partitionKey);
        var search = _table.Query(filter);
        var results = new List<T>();

        do
        {
            var docs = await search.GetNextSetAsync(ct);
            results.AddRange(docs.Select(Deserialize));
        } while (!search.IsDone);

        return results;
    }

    public async Task<IReadOnlyList<T>> ScanAsync(CancellationToken ct = default)
    {
        var search = _table.Scan(new ScanFilter());
        var results = new List<T>();

        do
        {
            var docs = await search.GetNextSetAsync(ct);
            results.AddRange(docs.Select(Deserialize));
        } while (!search.IsDone);

        return results;
    }

    private static string ResolveTableName()
    {
        // 1. Check for environment variable (set by CDK during deployment)
        var envVarName = $"TABLE_{typeof(T).Name.ToUpperInvariant()}";
        var fromEnv = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrEmpty(fromEnv))
            return fromEnv;

        // 2. Check for [TableName] attribute
        var attr = typeof(T).GetCustomAttribute<TableNameAttribute>();
        if (attr is not null)
            return attr.Name;

        // 3. Convention: type name + "s" (simple pluralization)
        var name = typeof(T).Name;
        return name.EndsWith('s') ? name : name + "s";
    }

    private static Document Serialize(T entity)
    {
        var json = JsonSerializer.Serialize(entity, JsonOptions);
        return Document.FromJson(json);
    }

    private static T Deserialize(Document doc)
    {
        var json = doc.ToJson();
        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    }

    private static string? FindKeyPropertyName<TAttribute>() where TAttribute : Attribute =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<TAttribute>() is not null)
            ?.Name;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
