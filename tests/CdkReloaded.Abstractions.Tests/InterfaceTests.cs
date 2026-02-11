using CdkReloaded.Abstractions;

namespace CdkReloaded.Abstractions.Tests;

public class InterfaceTests
{
    [Fact]
    public void IHttpFunction_ImplementsIFunction()
    {
        Assert.True(typeof(IFunction).IsAssignableFrom(typeof(IHttpFunction<string, string>)));
    }

    [Fact]
    public void IEventFunction_ImplementsIFunction()
    {
        Assert.True(typeof(IFunction).IsAssignableFrom(typeof(IEventFunction<string>)));
    }

    [Fact]
    public async Task IHttpFunction_CanBeImplemented()
    {
        IHttpFunction<string, string> func = new EchoFunction();
        var result = await func.HandleAsync("hello");

        Assert.Equal("hello", result);
    }

    private class EchoFunction : IHttpFunction<string, string>
    {
        public Task<string> HandleAsync(string request, CancellationToken ct = default)
            => Task.FromResult(request);
    }
}
