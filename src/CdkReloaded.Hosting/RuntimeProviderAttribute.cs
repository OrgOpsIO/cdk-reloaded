namespace CdkReloaded.Hosting;

/// <summary>
/// Applied to assemblies that provide an IRuntime implementation.
/// Discovered by CloudApplicationBuilder during Build().
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public class RuntimeProviderAttribute(Type runtimeType, ExecutionMode mode) : Attribute
{
    public Type RuntimeType { get; } = runtimeType;
    public ExecutionMode Mode { get; } = mode;
}
