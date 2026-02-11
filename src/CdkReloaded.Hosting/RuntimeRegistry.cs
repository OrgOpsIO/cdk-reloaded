namespace CdkReloaded.Hosting;

/// <summary>
/// Registry where runtime packages register themselves.
/// Used by CloudApplicationBuilder.Build() to auto-select the appropriate runtime.
/// </summary>
public static class RuntimeRegistry
{
    private static readonly Dictionary<ExecutionMode, Func<IRuntime>> Factories = [];

    public static void Register(ExecutionMode mode, Func<IRuntime> factory)
        => Factories[mode] = factory;

    internal static IRuntime? Create(ExecutionMode mode)
        => Factories.TryGetValue(mode, out var factory) ? factory() : null;
}
