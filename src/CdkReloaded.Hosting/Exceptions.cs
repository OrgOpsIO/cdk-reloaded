namespace CdkReloaded.Hosting;

public class CdkReloadedException : Exception
{
    public CdkReloadedException(string message) : base(message) { }
    public CdkReloadedException(string message, Exception innerException) : base(message, innerException) { }
}

public class FunctionInvocationException : CdkReloadedException
{
    public string FunctionName { get; }

    public FunctionInvocationException(string functionName, string message, Exception innerException)
        : base(message, innerException)
    {
        FunctionName = functionName;
    }
}

public class DeploymentException : CdkReloadedException
{
    public string Stage { get; }

    public DeploymentException(string stage, string message)
        : base(message)
    {
        Stage = stage;
    }

    public DeploymentException(string stage, string message, Exception innerException)
        : base(message, innerException)
    {
        Stage = stage;
    }
}

public class DependencyValidationException : CdkReloadedException
{
    public IReadOnlyList<string> MissingServices { get; }

    public DependencyValidationException(IReadOnlyList<string> missingServices)
        : base($"Missing service registrations:\n{string.Join("\n", missingServices.Select(s => $"  - {s}"))}")
    {
        MissingServices = missingServices;
    }
}
