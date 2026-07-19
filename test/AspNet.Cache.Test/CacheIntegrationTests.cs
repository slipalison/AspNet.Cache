using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace AspNet.Cache.Test;

public sealed class CacheIntegrationTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;

    public CacheIntegrationTests()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddControllers().AddApplicationPart(typeof(CounterController).Assembly);
                    services.AddDistributedMemoryCache();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .Start();
        _client = _host.GetTestClient();
    }

    [Fact]
    public async Task SecondRequest_SameArguments_IsServedFromCache()
    {
        CounterController.Reset();

        var first = await _client.GetStringAsync("/api/counter?id=1");
        var second = await _client.GetStringAsync("/api/counter?id=1");

        second.ShouldBe(first);
        CounterController.Executions.ShouldBe(1);
    }

    [Fact]
    public async Task DifferentArguments_ProduceDifferentEntries()
    {
        CounterController.Reset();

        var one = await _client.GetStringAsync("/api/counter?id=1");
        var two = await _client.GetStringAsync("/api/counter?id=2");

        one.ShouldNotBe(two);
        CounterController.Executions.ShouldBe(2);
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }
}

[ApiController]
[Route("api/[controller]")]
public sealed class CounterController : ControllerBase
{
    private static int _executions;

    internal static int Executions => Volatile.Read(ref _executions);

    internal static void Reset() => Interlocked.Exchange(ref _executions, 0);

    [HttpGet]
    [Cache(ExpireAt = 5, TimeSpanType = TimeSpanType.FromMinutes)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "MVC action methods must be instance methods.")]
    public CounterPayload Get(int id)
    {
        Interlocked.Increment(ref _executions);
        return new CounterPayload(id, Guid.NewGuid().ToString("N"));
    }
}

public sealed record CounterPayload(int Id, string Nonce);
