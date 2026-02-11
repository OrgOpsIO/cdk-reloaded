namespace CdkReloaded.Abstractions;

/// <summary>
/// Marker interface for all cloud functions.
/// </summary>
public interface IFunction;

/// <summary>
/// HTTP-triggered function (API Gateway → Lambda).
/// </summary>
public interface IHttpFunction<TRequest, TResponse> : IFunction
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken ct = default);
}

/// <summary>
/// Event-triggered function (SQS, SNS, etc.).
/// </summary>
public interface IEventFunction<TEvent> : IFunction
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
