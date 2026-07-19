using System;
using System.Text.Json;

namespace AspNet.Cache;

/// <summary>Global options for <see cref="CacheAttribute"/>. Configure via <c>services.Configure&lt;AspNetCacheOptions&gt;(...)</c>.</summary>
public sealed class AspNetCacheOptions
{
    internal static readonly AspNetCacheOptions FallbackDefaults = new();

    /// <summary>Serializer options used when hashing action arguments into the cache key. Defaults to web defaults.</summary>
    public JsonSerializerOptions? KeySerializerOptions { get; set; }

    /// <summary>Serializer options for the cached payload. Defaults to the app's MVC JSON options, then web defaults.</summary>
    public JsonSerializerOptions? PayloadSerializerOptions { get; set; }

    /// <summary>Responses whose serialized size exceeds this many bytes are not cached. Default keeps entries off the Large Object Heap.</summary>
    public int MaxPayloadBytes { get; set; } = 84_000;

    /// <summary>Predicate deciding which action arguments are excluded from the cache key.</summary>
    public Func<string, bool> IgnoreArgument { get; set; } =
        static name => name.Contains("correlationid", StringComparison.OrdinalIgnoreCase);
}
