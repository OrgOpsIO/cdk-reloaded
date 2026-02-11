namespace CdkReloaded.Hosting;

public sealed class TableRegistration
{
    public required Type EntityType { get; init; }
    public TableOptions Options { get; set; } = new();
}
