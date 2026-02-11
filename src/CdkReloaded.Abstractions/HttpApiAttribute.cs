namespace CdkReloaded.Abstractions;

public enum Method
{
    Get,
    Post,
    Put,
    Delete,
    Patch
}

[AttributeUsage(AttributeTargets.Class)]
public class HttpApiAttribute(Method method, string route) : Attribute
{
    public Method Method { get; } = method;
    public string Route { get; } = route;
}
