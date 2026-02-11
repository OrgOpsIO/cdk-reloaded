using System.Net;
using System.Net.Http.Json;
using CdkReloaded.Abstractions;
using CdkReloaded.DynamoDb;
using CdkReloaded.Hosting;
using CdkReloaded.Runtime.Local;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CdkReloaded.Runtime.Local.Tests;

public class LocalRuntimeIntegrationTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder([]);
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // random port

        // Register function types
        builder.Services.AddTransient<TestGetFunction>();
        builder.Services.AddTransient<TestPostFunction>();

        // Register InMemoryTable
        builder.Services.AddInMemoryTable<TestEntity>();

        _app = builder.Build();

        // Map functions manually (simulating what LocalRuntime does)
        var getFuncReg = new FunctionRegistration
        {
            FunctionType = typeof(TestGetFunction),
            RequestType = typeof(TestGetRequest),
            ResponseType = typeof(TestGetResponse),
            HttpApi = new HttpApiAttribute(Method.Get, "/items/{id}")
        };

        var postFuncReg = new FunctionRegistration
        {
            FunctionType = typeof(TestPostFunction),
            RequestType = typeof(TestPostRequest),
            ResponseType = typeof(TestPostResponse),
            HttpApi = new HttpApiAttribute(Method.Post, "/items")
        };

        LocalRuntime.MapFunction(_app, getFuncReg);
        LocalRuntime.MapFunction(_app, postFuncReg);

        await _app.StartAsync();

        _client = new HttpClient
        {
            BaseAddress = new Uri(_app.Urls.First())
        };
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [Fact]
    public async Task Post_CreatesItem_Get_RetrievesIt()
    {
        // Create
        var createResponse = await _client!.PostAsJsonAsync("/items",
            new TestPostRequest("TestItem", 42));
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<TestPostResponse>(LocalRuntime.JsonOptions);
        Assert.NotNull(created);
        Assert.False(string.IsNullOrEmpty(created.Id));

        // Retrieve
        var getResponse = await _client.GetAsync($"/items/{created.Id}");
        getResponse.EnsureSuccessStatusCode();

        var item = await getResponse.Content.ReadFromJsonAsync<TestGetResponse>(LocalRuntime.JsonOptions);
        Assert.NotNull(item);
        Assert.Equal(created.Id, item.Id);
        Assert.Equal("TestItem", item.Name);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_ForMissingItem()
    {
        var response = await _client!.GetAsync("/items/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_DeserializesJsonBody()
    {
        var response = await _client!.PostAsJsonAsync("/items",
            new TestPostRequest("JsonTest", 99));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TestPostResponse>(LocalRuntime.JsonOptions);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.Id));
    }
}

// Test fixtures
public class TestEntity : ITableEntity
{
    [PartitionKey] public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public int Value { get; set; }
}

public record TestGetRequest(string Id);
public record TestGetResponse(string Id, string Name, int Value);

[HttpApi(Method.Get, "/items/{id}")]
public class TestGetFunction(ITable<TestEntity> table) : IHttpFunction<TestGetRequest, TestGetResponse>
{
    public async Task<TestGetResponse> HandleAsync(TestGetRequest request, CancellationToken ct = default)
    {
        var item = await table.GetAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Item {request.Id} not found");
        return new TestGetResponse(item.Id, item.Name, item.Value);
    }
}

public record TestPostRequest(string Name, int Value);
public record TestPostResponse(string Id);

[HttpApi(Method.Post, "/items")]
public class TestPostFunction(ITable<TestEntity> table) : IHttpFunction<TestPostRequest, TestPostResponse>
{
    public async Task<TestPostResponse> HandleAsync(TestPostRequest request, CancellationToken ct = default)
    {
        var item = new TestEntity
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            Value = request.Value
        };
        await table.PutAsync(item, ct);
        return new TestPostResponse(item.Id);
    }
}
