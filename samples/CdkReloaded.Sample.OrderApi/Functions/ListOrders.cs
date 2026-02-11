using CdkReloaded.Abstractions;
using CdkReloaded.Sample.OrderApi.Models;

namespace CdkReloaded.Sample.OrderApi.Functions;

public record ListOrdersRequest;
public record ListOrdersResponse(IReadOnlyList<OrderSummary> Orders);
public record OrderSummary(string Id, string CustomerName, decimal Total);

[HttpApi(Method.Get, "/orders")]
public class ListOrders(ITable<Order> orders) : IHttpFunction<ListOrdersRequest, ListOrdersResponse>
{
    public async Task<ListOrdersResponse> HandleAsync(ListOrdersRequest request, CancellationToken ct = default)
    {
        var allOrders = await orders.ScanAsync(ct);
        var summaries = allOrders
            .Select(o => new OrderSummary(o.Id, o.CustomerName, o.Total))
            .ToList();

        return new ListOrdersResponse(summaries);
    }
}
