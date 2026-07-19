using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AspNet.Cache.Test;

public sealed class CacheAttributeCoverageTests
{
    private static ServiceProvider WithCache(IDistributedCache cache)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton(cache);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(TimeSpanType.FromMilliseconds, 5000, "max-age=5")]
    [InlineData(TimeSpanType.FromSeconds, 90, "max-age=90")]
    [InlineData(TimeSpanType.FromMinutes, 2, "max-age=120")]
    [InlineData(TimeSpanType.FromHours, 2, "max-age=7200")]
    [InlineData(TimeSpanType.FromDays, 1, "max-age=86400")]
    public async Task Expiration_Unit_ProducesCorrectMaxAge(TimeSpanType unit, long expireAt, string expected)
    {
        await using var services = WithCache(new InMemoryBufferCache());
        var attribute = new CacheAttribute { ExpireAt = expireAt, TimeSpanType = unit };

        var (first, _) = FilterContextFactory.Create(services, "/api/exp", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(first,
            FilterContextFactory.NextReturning(first, new ObjectResult(new Payload(1, "x"))));

        var (second, http) = FilterContextFactory.Create(services, "/api/exp", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(second,
            FilterContextFactory.NextReturning(second, new ObjectResult(new Payload(1, "x"))));

        second.Result.ShouldNotBeNull();
        http.Response.Headers.CacheControl.ToString().ShouldBe(expected);
    }

    [Fact]
    public async Task BufferedProvider_StatusCodeResult_CachedAsEmptyMarker_AndReplayed()
    {
        await using var services = WithCache(new InMemoryBufferCache());
        var attribute = new CacheAttribute();
        var executions = 0;

        var (first, _) = FilterContextFactory.Create(services, "/api/bempty", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(first,
            FilterContextFactory.NextReturning(first, new StatusCodeResult(200), () => executions++));

        var (second, _) = FilterContextFactory.Create(services, "/api/bempty", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(second,
            FilterContextFactory.NextReturning(second, new StatusCodeResult(200), () => executions++));

        executions.ShouldBe(1);
        second.Result.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task BufferedProvider_GetFailure_FailsOpen()
    {
        await using var services = WithCache(new FaultyBufferCache { OnGet = new InvalidOperationException("down") });
        var attribute = new CacheAttribute();
        var executions = 0;

        var (context, _) = FilterContextFactory.Create(services, "/api/bfail", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(context,
            FilterContextFactory.NextReturning(context, new ObjectResult(new Payload(1, "x")), () => executions++));

        executions.ShouldBe(1);
        context.Result.ShouldBeNull();
    }

    [Fact]
    public async Task BufferedProvider_CanceledRequest_PropagatesOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await using var services = WithCache(new FaultyBufferCache { OnGet = new OperationCanceledException(cts.Token) });
        var attribute = new CacheAttribute();

        var (context, http) = FilterContextFactory.Create(services, "/api/bcancel", new() { ["id"] = 1 });
        http.RequestAborted = cts.Token;

        await Should.ThrowAsync<OperationCanceledException>(() => attribute.OnActionExecutionAsync(context,
            FilterContextFactory.NextReturning(context, new ObjectResult(new Payload(1, "x")))));
    }

    [Fact]
    public async Task BufferedProvider_SetFailure_FailsOpen()
    {
        await using var services = WithCache(new FaultyBufferCache { OnSet = new InvalidOperationException("down") });
        var attribute = new CacheAttribute();
        var executions = 0;

        var (context, _) = FilterContextFactory.Create(services, "/api/bsetfail", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(context,
            FilterContextFactory.NextReturning(context, new ObjectResult(new Payload(1, "x")), () => executions++));

        executions.ShouldBe(1);
        context.Result.ShouldBeNull();
    }

    [Fact]
    public async Task StatusCodeResult_SetFailure_FailsOpen()
    {
        var failing = Substitute.For<IDistributedCache>();
        failing.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        failing.SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("down"));
        await using var services = WithCache(failing);
        var attribute = new CacheAttribute();
        var executions = 0;

        var (context, _) = FilterContextFactory.Create(services, "/api/scodefail", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(context,
            FilterContextFactory.NextReturning(context, new StatusCodeResult(200), () => executions++));

        executions.ShouldBe(1);
    }

    [Fact]
    public async Task ActionCanceled_IsNotCached()
    {
        await using var services = WithCache(new InMemoryBufferCache());
        var attribute = new CacheAttribute();
        var executions = 0;

        for (var i = 0; i < 2; i++)
        {
            var (context, _) = FilterContextFactory.Create(services, "/api/actcancel", new() { ["id"] = 1 });
            ActionExecutionDelegate next = () =>
            {
                executions++;
                return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), new object())
                {
                    Canceled = true
                });
            };
            await attribute.OnActionExecutionAsync(context, next);
        }

        executions.ShouldBe(2);
    }

    [Fact]
    public async Task NoOptionsRegistered_UsesFallbackDefaults()
    {
        await using var services = new ServiceCollection()
            .AddSingleton<IDistributedCache>(new InMemoryBufferCache())
            .BuildServiceProvider();
        var attribute = new CacheAttribute();
        var executions = 0;

        var (first, _) = FilterContextFactory.Create(services, "/api/nodefaults", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(first,
            FilterContextFactory.NextReturning(first, new ObjectResult(new Payload(1, "x")), () => executions++));

        var (second, _) = FilterContextFactory.Create(services, "/api/nodefaults", new() { ["id"] = 1 });
        await attribute.OnActionExecutionAsync(second,
            FilterContextFactory.NextReturning(second, new ObjectResult(new Payload(1, "x")), () => executions++));

        executions.ShouldBe(1);
        second.Result.ShouldNotBeNull();
    }

    private sealed record Payload(int Id, string Name);
}
