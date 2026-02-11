using System.Reflection;
using CdkReloaded.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CdkReloaded.Hosting;

public class CloudApplicationBuilder
{
    private readonly string[] _args;
    private readonly CloudDefaults _defaults = new();

    private FunctionDiscoveryBuilder? _functionDiscovery;
    private TableDiscoveryBuilder? _tableDiscovery;
    private readonly List<(Type FunctionType, Action<FunctionOptions>? Configure)> _explicitFunctions = [];
    private readonly List<(Type EntityType, Action<TableOptions>? Configure)> _explicitTables = [];
    private IRuntime? _runtime;

    internal CloudApplicationBuilder(string[] args)
    {
        _args = args;
    }

    /// <summary>
    /// Registers additional services to be added to the runtime's DI container.
    /// </summary>
    public IServiceCollection Services { get; } = new ServiceCollection();

    public FunctionDiscoveryBuilder AddFunctions()
    {
        _functionDiscovery = new FunctionDiscoveryBuilder(this);
        return _functionDiscovery;
    }

    public TableDiscoveryBuilder AddTables()
    {
        _tableDiscovery = new TableDiscoveryBuilder(this);
        return _tableDiscovery;
    }

    public CloudApplicationBuilder AddFunction<TFunction>(Action<FunctionOptions>? configure = null)
        where TFunction : IFunction
    {
        _explicitFunctions.Add((typeof(TFunction), configure));
        return this;
    }

    public CloudApplicationBuilder AddTable<TEntity>(Action<TableOptions>? configure = null)
        where TEntity : ITableEntity
    {
        _explicitTables.Add((typeof(TEntity), configure));
        return this;
    }

    public CloudApplicationBuilder ConfigureDefaults(Action<CloudDefaults> configure)
    {
        configure(_defaults);
        return this;
    }

    /// <summary>
    /// Registers the runtime that will execute the application.
    /// Called by runtime packages (e.g. CdkReloaded.Runtime.Local).
    /// </summary>
    public CloudApplicationBuilder UseRuntime(IRuntime runtime)
    {
        _runtime = runtime;
        return this;
    }

    public CloudApplication Build()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<CloudApplicationBuilder>();

        var (mode, command) = DetectModeAndCommand(_args);
        logger.LogInformation("Mode: {Mode}, Command: {Command}", mode, command);

        // Discover functions
        var functions = new List<FunctionRegistration>();
        if (_functionDiscovery is not null)
            functions.AddRange(_functionDiscovery.Discover());

        // Discover tables
        var tables = new List<TableRegistration>();
        if (_tableDiscovery is not null)
            tables.AddRange(_tableDiscovery.Discover());

        logger.LogInformation("Discovered {FunctionCount} function(s), {TableCount} table(s)",
            functions.Count, tables.Count);

        foreach (var func in functions)
            logger.LogDebug("  Function: {Method} {Route} -> {Type}",
                func.HttpApi.Method, func.HttpApi.Route, func.FunctionType.Name);
        foreach (var table in tables)
            logger.LogDebug("  Table: {Type}", table.EntityType.Name);

        // Apply L2 config (explicit registrations with fluent config)
        foreach (var (funcType, configure) in _explicitFunctions)
        {
            var existing = functions.FirstOrDefault(f => f.FunctionType == funcType);
            if (existing is not null && configure is not null)
            {
                var opts = new FunctionOptions();
                configure(opts);
                existing.Options = opts;
            }
        }

        foreach (var (entityType, configure) in _explicitTables)
        {
            var existing = tables.FirstOrDefault(t => t.EntityType == entityType);
            if (existing is not null && configure is not null)
            {
                var opts = new TableOptions();
                configure(opts);
                existing.Options = opts;
            }
        }

        var context = new CloudApplicationContext
        {
            Args = _args,
            Mode = mode,
            Command = command,
            Functions = functions,
            Tables = tables,
            Defaults = _defaults
        };

        // Validate DI dependencies (skip for list command)
        if (command != CliCommand.List)
            ValidateDependencies(functions, logger);

        // Collect service configurators: user services + function registrations
        var configurators = new List<Action<IServiceCollection>>();

        // Add user-registered services
        var userServices = Services;
        configurators.Add(services =>
        {
            foreach (var descriptor in userServices)
                services.Add(descriptor);
        });

        // Register all discovered function types
        configurators.Add(services =>
        {
            foreach (var func in functions)
                services.AddTransient(func.FunctionType);
        });

        // Resolve the runtime: explicit > auto-discovery > error
        var runtime = _runtime ?? DiscoverRuntime(mode);
        logger.LogInformation("Runtime: {Runtime}", runtime?.GetType().Name ?? "none");

        return new CloudApplication(context, configurators, runtime);
    }

    private void ValidateDependencies(List<FunctionRegistration> functions, ILogger logger)
    {
        var registeredServices = new HashSet<Type>(Services.Select(sd => sd.ServiceType));
        var missing = new List<string>();

        foreach (var func in functions)
        {
            var ctors = func.FunctionType.GetConstructors();
            if (ctors.Length == 0) continue;

            var ctor = ctors.OrderByDescending(c => c.GetParameters().Length).First();
            foreach (var param in ctor.GetParameters())
            {
                var paramType = param.ParameterType;

                // ITable<T> is provided by the runtime, skip
                if (paramType.IsGenericType &&
                    paramType.GetGenericTypeDefinition() == typeof(ITable<>))
                    continue;

                // ILogger<T> is provided by the runtime's DI container
                if (paramType.IsGenericType &&
                    paramType.GetGenericTypeDefinition() == typeof(ILogger<>))
                    continue;

                if (!registeredServices.Contains(paramType))
                {
                    missing.Add($"{func.FunctionType.Name} requires {paramType.Name} (parameter '{param.Name}')");
                }
            }
        }

        if (missing.Count > 0)
        {
            logger.LogError("Dependency validation failed");
            throw new DependencyValidationException(missing);
        }
    }

    public static ExecutionMode DetectMode(string[] args) => DetectModeAndCommand(args).Mode;

    public static (ExecutionMode Mode, CliCommand Command) DetectModeAndCommand(string[] args)
    {
        if (Environment.GetEnvironmentVariable("AWS_LAMBDA_RUNTIME_API") is not null)
            return (ExecutionMode.Lambda, CliCommand.None);

        if (args.Contains("deploy"))
            return (ExecutionMode.Deploy, CliCommand.Deploy);
        if (args.Contains("synth"))
            return (ExecutionMode.Deploy, CliCommand.Synth);
        if (args.Contains("destroy"))
            return (ExecutionMode.Deploy, CliCommand.Destroy);
        if (args.Contains("diff"))
            return (ExecutionMode.Deploy, CliCommand.Diff);
        if (args.Contains("list"))
            return (ExecutionMode.Local, CliCommand.List);

        return (ExecutionMode.Local, CliCommand.None);
    }

    /// <summary>
    /// Scans assemblies in the application's base directory for [RuntimeProvider] attributes
    /// to auto-discover the appropriate runtime for the given mode.
    /// </summary>
    private static IRuntime? DiscoverRuntime(ExecutionMode mode)
    {
        // First check the static registry (for explicitly registered runtimes)
        var fromRegistry = RuntimeRegistry.Create(mode);
        if (fromRegistry is not null)
            return fromRegistry;

        // Scan all CdkReloaded.*.dll assemblies in the app's base directory
        var baseDir = AppContext.BaseDirectory;
        foreach (var dllPath in Directory.GetFiles(baseDir, "CdkReloaded.*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dllPath);
                var attr = assembly.GetCustomAttribute<RuntimeProviderAttribute>();

                if (attr is not null && attr.Mode == mode)
                {
                    return (IRuntime)Activator.CreateInstance(attr.RuntimeType)!;
                }
            }
            catch
            {
                // Skip assemblies that can't be loaded
            }
        }

        return null;
    }
}
