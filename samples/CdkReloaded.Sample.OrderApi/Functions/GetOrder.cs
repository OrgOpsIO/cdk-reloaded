using CdkReloaded.Abstractions;
using CdkReloaded.Sample.OrderApi.Models;

namespace CdkReloaded.Sample.OrderApi.Functions;

public record GetOrderRequest(string Id);
public record GetOrderResponse(string Id, string CustomerName, decimal Total);

[HttpApi(Method.Get, "/orders/{id}")]
public class GetOrder(ITable<Order> orders) : IHttpFunction<GetOrderRequest, GetOrderResponse>
{
    public async Task<GetOrderResponse> HandleAsync(GetOrderRequest request, CancellationToken ct = default)
    {
        var order = await orders.GetAsync(request.Id, ct);
        return order is null
            ? throw new KeyNotFoundException($"Order {request.Id} not found")
            : new GetOrderResponse(order.Id, order.CustomerName, order.Total);
    }
}
