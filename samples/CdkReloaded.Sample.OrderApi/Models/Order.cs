using CdkReloaded.Abstractions;

namespace CdkReloaded.Sample.OrderApi.Models;

public class Order : ITableEntity
{
    [PartitionKey]
    public string Id { get; set; } = default!;

    public string CustomerName { get; set; } = default!;

    public decimal Total { get; set; }
}
