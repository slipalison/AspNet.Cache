using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AspNet.Cache.Test;

public sealed class CacheAttributeTests
{
    private static ServiceProvider BuildServices(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddDistributedMemoryCache();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task MissThenHit_ServesCachedPayload_WithoutReexecutingAction()
    {
        await using var services = BuildServices();
        var attribute = new CacheAttribute();
        var executions = 0;

        var (first, _) = FilterContextFactory.Create(services, "/api/test", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(first,
            FilterContextFactory.NextReturning(first, new ObjectResult(new Payload(1, "fresh")), () => executions++));

        executions.ShouldBe(1);
        first.Result.ShouldBeNull();

        var (second, http) = FilterContextFactory.Create(services, "/api/test", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(second,
            FilterContextFactory.NextReturning(second, new ObjectResult(new Payload(1, "fresh")), () => executions++));

        executions.ShouldBe(1);
        second.Result.ShouldNotBeNull();
        var body = await FilterContextFactory.ReadBodyAsync(http, second.Result!);
        body.ShouldContain("fresh");
        http.Response.Headers.CacheControl.ToString().ShouldBe("max-age=600");
    }

    [Fact]
    public async Task BufferedProvider_MissThenHit_Roundtrips()
    {
        await using var services = BuildServices(s =>
            s.AddSingleton<IDistributedCache>(new InMemoryBufferCache()));
        var attribute = new CacheAttribute();
        var executions = 0;

        var (first, _) = FilterContextFactory.Create(services, "/api/buffered", new() { ["id"] = 9 });
        await attribute.OnActionExecutionAsync(first,
            FilterContextFactory.NextReturning(first, new ObjectResult(new Payload(9, "pooled")), () => executions++));

        var (second, http) = FilterContextFactory.Create(services, "/api/buffered", new() { ["id"] = 9 });
        await attribute.OnActionExecutionAsync(second,
            FilterContextFactory.NextReturning(second, new ObjectResult(new Payload(9, "pooled")), () => executions++));

        executions.ShouldBe(1);
        var body = await FilterContextFactory.ReadBodyAsync(http, second.Result!);
        body.ShouldContain("pooled");
    }

    [Fact]
    public async Task SharedAttributeInstance_ConcurrentRequests_NeverMixResponses()
    {
        await using var services = BuildServices();
        var attribute = new CacheAttribute();

        await Parallel.ForAsync(0, 500, async (iteration, _) =>
        {
            var expected = iteration % 20;
            var (context, http) = FilterContextFactory.Create(services, "/api/race", new() { ["id"] = expected });
            await attribute.OnActionExecutionAsync(context,
                FilterContextFactory.NextReturning(context, new ObjectResult(new Echo(expected))));

            if (context.Result is not null)
            {
                var body = await FilterContextFactory.ReadBodyAsync(http, context.Result);
                body.ShouldBe($"{{\"id\":{expected}}}");
            }
        });
    }

    [Fact]
    public async Task StatusMismatch_IsNotCached()
    {
        await using var services = BuildServices();
        var attribute = new CacheAttribute { SuccessStatus = HttpStatusCode.Created };
        var executions = 0;

        for (var i = 0; i < 2; i++)
        {
            var (context, _) = FilterContextFactory.Create(services, "/api/status", new() { ["id"] = 1 });
            await attribute.OnActionExecutionAsync(context,
                FilterContextFactory.NextReturning(context, new ObjectResult(new Payload(1, "x")), () => executions++));
        }

        executions.ShouldBe(2);
    }

    [Fact]
    public async Task OversizedPayload_IsNotCached()
    {
        await using var services = BuildServices(s =>
            s.Configure<AspNetCacheOptions>(o => o.MaxPayloadBytes = 8));
        var attribute = new CacheAttribute();
        var executions = 0;

        for (var i = 0; i < 2; i++)
        {
            var (context, _) = FilterContextFactory.Create(services, "/api/big", new() { ["id"] = 1 });
            await attribute.OnActionExecutionAsync(context,
                FilterContextFactory.NextReturning(context, new ObjectResult(new Payload(1, "0123456789")), () => executions++));
        }

        executions.ShouldBe(2);
    }

    [Fact]
    public async Task StatusCodeResult_IsCachedAsEmptyMarker_AndReplayed()
    {
        await using var services = BuildServices();
        var attribute = new CacheAttribute();
        var executions = 0;

        var (first, _) = FilterContextFactory.Create(services, "/api/empty", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(first,
            FilterContextFactory.NextReturning(first, new StatusCodeResult(200), () => executions++));

        var (second, _) = FilterContextFactory.Create(services, "/api/empty", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(second,
            FilterContextFactory.NextReturning(second, new StatusCodeResult(200), () => executions++));

        executions.ShouldBe(1);
        second.Result.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task CacheGetFailure_FailsOpen_ActionStillExecutes()
    {
        var failing = Substitute.For<IDistributedCache>();
        failing.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("redis down"));
        failing.SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await using var services = BuildServices(s => s.AddSingleton(failing));
        var attribute = new CacheAttribute();
        var executions = 0;

        var (context, _) = FilterContextFactory.Create(services, "/api/failopen", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(context,
            FilterContextFactory.NextReturning(context, new ObjectResult(new Payload(1, "x")), () => executions++));

        executions.ShouldBe(1);
        context.Result.ShouldBeNull();
    }

    [Fact]
    public async Task CanceledRequest_PropagatesOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var failing = Substitute.For<IDistributedCache>();
        failing.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        await using var services = BuildServices(s => s.AddSingleton(failing));
        var attribute = new CacheAttribute();

        var (context, http) = FilterContextFactory.Create(services, "/api/cancel", new() { ["id"] = 1 });
        http.RequestAborted = cts.Token;

        await Should.ThrowAsync<OperationCanceledException>(() => attribute.OnActionExecutionAsync(context,
            FilterContextFactory.NextReturning(context, new ObjectResult(new Payload(1, "x")))));
    }

    [Fact]
    public async Task NoDistributedCacheRegistered_JustRunsAction()
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var attribute = new CacheAttribute();
        var executions = 0;

        var (context, _) = FilterContextFactory.Create(services, "/api/nocache", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(context,
            FilterContextFactory.NextReturning(context, new ObjectResult(new Payload(1, "x")), () => executions++));

        executions.ShouldBe(1);
    }

    private sealed record Payload(int Id, string Name);

    private sealed record Echo(int Id);
}
