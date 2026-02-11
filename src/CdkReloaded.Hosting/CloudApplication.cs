using Microsoft.Extensions.DependencyInjection;

namespace CdkReloaded.Hosting;

public class CloudApplication
{
    private readonly CloudApplicationContext _context;
    private readonly IReadOnlyList<Action<IServiceCollection>> _serviceConfigurators;
    private readonly IRuntime? _runtime;

    internal CloudApplication(
        CloudApplicationContext context,
        IReadOnlyList<Action<IServiceCollection>> serviceConfigurators,
        IRuntime? runtime)
    {
        _context = context;
        _serviceConfigurators = serviceConfigurators;
        _runtime = runtime;
    }

    public static CloudApplicationBuilder CreateBuilder(string[] args) => new(args);

    public CloudApplicationContext Context => _context;

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (_context.Command == CliCommand.List)
        {
            PrintResources();
            return;
        }

        if (_runtime is null)
            throw new InvalidOperationException(
                $"No runtime configured for execution mode '{_context.Mode}'. " +
                "Add a runtime package (e.g. CdkReloaded.Runtime.Local).");

        await _runtime.RunAsync(_context, _serviceConfigurators, ct);
    }

    private void PrintResources()
    {
        Console.WriteLine("=== CdkReloaded Resources ===");
        Console.WriteLine();

        Console.WriteLine($"Functions ({_context.Functions.Count}):");
        foreach (var func in _context.Functions)
        {
            Console.WriteLine($"  {func.HttpApi.Method,-7} {func.HttpApi.Route,-30} -> {func.FunctionType.Name}");
        }

        Console.WriteLine();
        Console.WriteLine($"Tables ({_context.Tables.Count}):");
        foreach (var table in _context.Tables)
        {
            Console.WriteLine($"  {table.EntityType.Name}");
        }
    }

    public void Run()
    {
        RunAsync().GetAwaiter().GetResult();
    }
}
