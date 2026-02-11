namespace CdkReloaded.Hosting;

/// <summary>
/// Immutable snapshot of all discovered resources and configuration,
/// shared between builder and runtime.
/// </summary>
public sealed class CloudApplicationContext
{
    public required string[] Args { get; init; }
    public required ExecutionMode Mode { get; init; }
    public required CliCommand Command { get; init; }
    public required IReadOnlyList<FunctionRegistration> Functions { get; init; }
    public required IReadOnlyList<TableRegistration> Tables { get; init; }
    public required CloudDefaults Defaults { get; init; }
}
