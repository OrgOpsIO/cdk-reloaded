using System.Reflection;
using CdkReloaded.Abstractions;
using CdkReloaded.Hosting;

namespace CdkReloaded.Hosting.Tests;

public class FunctionDiscoveryTests
{
    [Fact]
    public void Discover_FindsHttpFunctions()
    {
        var builder = CloudApplication.CreateBuilder([]);
        var discovery = builder.AddFunctions();
        discovery.FromAssembly(typeof(FunctionDiscoveryTests).Assembly);

        var registrations = discovery.Discover();

        Assert.Single(registrations);
        Assert.Equal(typeof(TestFunction), registrations[0].FunctionType);
        Assert.Equal(typeof(TestRequest), registrations[0].RequestType);
        Assert.Equal(typeof(TestResponse), registrations[0].ResponseType);
        Assert.Equal(Method.Get, registrations[0].HttpApi.Method);
        Assert.Equal("/test", registrations[0].HttpApi.Route);
    }

    [Fact]
    public void Discover_IgnoresAbstractClasses()
    {
        var builder = CloudApplication.CreateBuilder([]);
        var discovery = builder.AddFunctions();
        discovery.FromAssembly(typeof(FunctionDiscoveryTests).Assembly);

        var registrations = discovery.Discover();

        Assert.DoesNotContain(registrations, r => r.FunctionType == typeof(AbstractFunction));
    }

    [Fact]
    public void Discover_AppliesFilter()
    {
        var builder = CloudApplication.CreateBuilder([]);
        var discovery = builder.AddFunctions();
        discovery.FromAssembly(typeof(FunctionDiscoveryTests).Assembly);
        discovery.WithFilter(t => t.Name.StartsWith("Filtered"));

        var registrations = discovery.Discover();

        Assert.Empty(registrations);
    }
}

// Test fixtures
public record TestRequest(string Value);
public record TestResponse(string Value);

[HttpApi(Method.Get, "/test")]
public class TestFunction : IHttpFunction<TestRequest, TestResponse>
{
    public Task<TestResponse> HandleAsync(TestRequest request, CancellationToken ct = default)
        => Task.FromResult(new TestResponse(request.Value));
}

[HttpApi(Method.Post, "/abstract")]
public abstract class AbstractFunction : IHttpFunction<TestRequest, TestResponse>
{
    public abstract Task<TestResponse> HandleAsync(TestRequest request, CancellationToken ct = default);
}
