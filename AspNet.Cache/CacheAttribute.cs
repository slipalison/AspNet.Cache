using System;
using System.Buffers;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AspNet.Cache.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AspNet.Cache;

/// <summary>
/// Caches the JSON payload of a successful action result in the registered <see cref="IDistributedCache"/>.
/// When the provider implements <see cref="IBufferDistributedCache"/>, reads and writes flow through pooled
/// buffers and never materialize a per-request byte array.
/// The attribute instance is shared across concurrent requests and therefore holds no per-request state.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CacheAttribute : ActionFilterAttribute
{
    private string? _cacheControlHeaderValue;
    private DistributedCacheEntryOptions? _entryOptions;

    /// <summary>Optional key prefix ("folder") prepended to every cache key.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>Expiration amount, interpreted according to <see cref="TimeSpanType"/>. Default: 10.</summary>
    public long ExpireAt { get; set; } = 10;

    /// <summary>Unit for <see cref="ExpireAt"/>. Default: minutes.</summary>
    public TimeSpanType TimeSpanType { get; set; } = TimeSpanType.FromMinutes;

    /// <summary>Status code that marks a response as cacheable and is replayed on cache hits. Default: 200 OK.</summary>
    public HttpStatusCode SuccessStatus { get; set; } = HttpStatusCode.OK;

    /// <inheritdoc />
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var services = context.HttpContext.RequestServices;
        var cache = services.GetService<IDistributedCache>();
        if (cache is null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var options = services.GetService<IOptions<AspNetCacheOptions>>()?.Value ?? AspNetCacheOptions.FallbackDefaults;
        var key = CacheKeyBuilder.Build(Folder, context.HttpContext.Request.Path.Value, context.ActionArguments,
            options.IgnoreArgument, options.KeySerializerOptions ?? SerializerDefaults.Web);

        var cancellationToken = context.HttpContext.RequestAborted;
        var hitResult = cache is IBufferDistributedCache bufferedCache
            ? await TryServeBufferedAsync(bufferedCache, key, services, cancellationToken).ConfigureAwait(false)
            : await TryServeLegacyAsync(cache, key, services, cancellationToken).ConfigureAwait(false);

        if (hitResult is not null)
        {
            context.HttpContext.Response.Headers.CacheControl = GetCacheControlHeaderValue();
            context.Result = hitResult;
            return;
        }

        var executed = await next().ConfigureAwait(false);
        if (executed.Exception is not null || executed.Canceled)
            return;

        await StoreAsync(cache, key, executed.Result, services, options).ConfigureAwait(false);
    }

    private async Task<IActionResult?> TryServeBufferedAsync(IBufferDistributedCache cache, string key,
        IServiceProvider services, CancellationToken cancellationToken)
    {
        var buffer = new PooledArrayBufferWriter();
        var handedOff = false;
        try
        {
            var found = await cache.TryGetAsync(key, buffer, cancellationToken).ConfigureAwait(false);
            if (!found || buffer.WrittenCount == 0)
                return null;

            if (CachedJsonResult.IsEmptyMarker(buffer.WrittenSpan))
                return new StatusCodeResult((int)SuccessStatus);

            handedOff = true;
            return new PooledCachedJsonResult(buffer, (int)SuccessStatus);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            CacheLog.GetFailed(ResolveLogger(services), exception);
            return null;
        }
        finally
        {
            if (!handedOff)
                buffer.Dispose();
        }
    }

    private async Task<IActionResult?> TryServeLegacyAsync(IDistributedCache cache, string key,
        IServiceProvider services, CancellationToken cancellationToken)
    {
        byte[]? cached = null;
        try
        {
            cached = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            CacheLog.GetFailed(ResolveLogger(services), exception);
        }

        if (cached is not { Length: > 0 })
            return null;

        return CachedJsonResult.IsEmptyMarker(cached)
            ? new StatusCodeResult((int)SuccessStatus)
            : new CachedJsonResult(cached, (int)SuccessStatus);
    }

    private async Task StoreAsync(IDistributedCache cache, string key, IActionResult? result,
        IServiceProvider services, AspNetCacheOptions options)
    {
        if (result is ObjectResult { Value: not null } objectResult && MatchesSuccessStatus(objectResult.StatusCode))
        {
            var payloadOptions = options.PayloadSerializerOptions
                ?? services.GetService<IOptions<JsonOptions>>()?.Value.JsonSerializerOptions
                ?? SerializerDefaults.Web;

            using var buffer = new PooledArrayBufferWriter(options.MaxPayloadBytes);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                JsonSerializer.Serialize(writer, objectResult.Value, objectResult.Value.GetType(), payloadOptions);
            }

            if (buffer.Overflowed)
                return;

            try
            {
                // CancellationToken.None: a client disconnect after the action ran must not discard the produced entry.
                if (cache is IBufferDistributedCache bufferedCache)
                {
                    await bufferedCache.SetAsync(key, new ReadOnlySequence<byte>(buffer.WrittenMemory),
                        GetEntryOptions(), CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    var payload = buffer.WrittenSpan.ToArray();
                    await cache.SetAsync(key, payload, GetEntryOptions(), CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                CacheLog.SetFailed(ResolveLogger(services), exception);
            }

            return;
        }

        if (result is StatusCodeResult statusCodeResult && statusCodeResult.StatusCode == (int)SuccessStatus)
        {
            try
            {
                await cache.SetAsync(key, CachedJsonResult.EmptyMarker, GetEntryOptions(), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                CacheLog.SetFailed(ResolveLogger(services), exception);
            }
        }
    }

    private bool MatchesSuccessStatus(int? statusCode) =>
        (statusCode ?? (int)HttpStatusCode.OK) == (int)SuccessStatus;

    private TimeSpan GetExpiration() => TimeSpanType switch
    {
        TimeSpanType.FromMilliseconds => TimeSpan.FromMilliseconds(ExpireAt),
        TimeSpanType.FromSeconds => TimeSpan.FromSeconds(ExpireAt),
        TimeSpanType.FromHours => TimeSpan.FromHours(ExpireAt),
        TimeSpanType.FromDays => TimeSpan.FromDays(ExpireAt),
        _ => TimeSpan.FromMinutes(ExpireAt)
    };

    private string GetCacheControlHeaderValue() =>
        // Benign race: concurrent first requests compute the same value; last write wins.
        _cacheControlHeaderValue ??= string.Create(CultureInfo.InvariantCulture,
            $"max-age={(long)GetExpiration().TotalSeconds}");

    private DistributedCacheEntryOptions GetEntryOptions() =>
        // Benign race: same value on every compute; providers treat the instance as read-only.
        _entryOptions ??= new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = GetExpiration() };

    private static ILogger ResolveLogger(IServiceProvider services) =>
        services.GetService<ILogger<CacheAttribute>>() ?? NullLogger<CacheAttribute>.Instance;
}
