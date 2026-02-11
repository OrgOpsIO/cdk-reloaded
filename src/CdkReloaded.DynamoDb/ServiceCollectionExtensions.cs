using Amazon.DynamoDBv2;
using CdkReloaded.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CdkReloaded.DynamoDb;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers InMemoryTable&lt;T&gt; for a specific entity type.
    /// </summary>
    public static IServiceCollection AddInMemoryTable<T>(this IServiceCollection services)
        where T : ITableEntity
    {
        services.AddSingleton<ITable<T>, InMemoryTable<T>>();
        return services;
    }

    /// <summary>
    /// Registers InMemoryTable&lt;T&gt; for a list of entity types (discovered at runtime).
    /// </summary>
    public static IServiceCollection AddInMemoryTables(
        this IServiceCollection services,
        IEnumerable<Type> entityTypes)
    {
        foreach (var entityType in entityTypes)
        {
            var tableInterface = typeof(ITable<>).MakeGenericType(entityType);
            var tableImpl = typeof(InMemoryTable<>).MakeGenericType(entityType);
            services.AddSingleton(tableInterface, tableImpl);
        }

        return services;
    }

    /// <summary>
    /// Registers DynamoDbTable&lt;T&gt; (real AWS implementation) for a list of entity types.
    /// </summary>
    public static IServiceCollection AddDynamoDbTables(
        this IServiceCollection services,
        IEnumerable<Type> entityTypes)
    {
        // Register the DynamoDB client as a singleton
        services.AddSingleton<IAmazonDynamoDB>(new AmazonDynamoDBClient());

        foreach (var entityType in entityTypes)
        {
            var tableInterface = typeof(ITable<>).MakeGenericType(entityType);
            var tableImpl = typeof(DynamoDbTable<>).MakeGenericType(entityType);
            services.AddSingleton(tableInterface, tableImpl);
        }

        return services;
    }
}
