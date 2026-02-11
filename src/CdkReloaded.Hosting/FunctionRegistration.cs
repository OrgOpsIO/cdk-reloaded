using CdkReloaded.Abstractions;

namespace CdkReloaded.Hosting;

public sealed class FunctionRegistration
{
    public required Type FunctionType { get; init; }
    public required Type RequestType { get; init; }
    public required Type ResponseType { get; init; }
    public required HttpApiAttribute HttpApi { get; init; }
    public FunctionOptions Options { get; set; } = new();
}
