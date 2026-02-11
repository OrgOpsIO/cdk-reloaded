using Microsoft.Extensions.DependencyInjection;

namespace CdkReloaded.Hosting;

/// <summary>
/// Contract that each execution mode runtime implements to run the application.
/// </summary>
public interface IRuntime
{
    Task RunAsync(CloudApplicationContext context, IReadOnlyList<Action<IServiceCollection>> serviceConfigurators, CancellationToken ct);
}
