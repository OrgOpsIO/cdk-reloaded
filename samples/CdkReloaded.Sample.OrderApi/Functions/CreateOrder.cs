using CdkReloaded.Abstractions;
using CdkReloaded.Sample.OrderApi.Models;

namespace CdkReloaded.Sample.OrderApi.Functions;

public record CreateOrderRequest(string CustomerName, decimal Total);
public record CreateOrderResponse(string Id);

[HttpApi(Method.Post, "/orders")]
public class CreateOrder(ITable<Order> orders) : IHttpFunction<CreateOrderRequest, CreateOrderResponse>
{
    public async Task<CreateOrderResponse> HandleAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            CustomerName = request.CustomerName,
            Total = request.Total
        };

        await orders.PutAsync(order, ct);
        return new CreateOrderResponse(order.Id);
    }
}
