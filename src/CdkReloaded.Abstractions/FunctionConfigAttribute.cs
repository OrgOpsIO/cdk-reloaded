namespace CdkReloaded.Abstractions;

[AttributeUsage(AttributeTargets.Class)]
public class FunctionConfigAttribute : Attribute
{
    public int MemoryMb { get; set; } = 256;
    public int TimeoutSeconds { get; set; } = 30;
}
