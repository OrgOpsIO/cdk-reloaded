using CdkReloaded.Hosting;

namespace CdkReloaded.Runtime.Local;

public static class CloudApplicationBuilderExtensions
{
    /// <summary>
    /// Configures the application to use the local Kestrel-based runtime
    /// with in-memory table implementations.
    /// </summary>
    public static CloudApplicationBuilder UseLocalRuntime(this CloudApplicationBuilder builder)
    {
        builder.UseRuntime(new LocalRuntime());
        return builder;
    }
}
