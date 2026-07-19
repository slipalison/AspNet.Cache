using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace AspNet.Cache.Internal;

/// <summary>Writes a cached UTF-8 JSON payload straight to the response body, without re-deserializing it.</summary>
internal sealed class CachedJsonResult : IActionResult
{
    internal static readonly byte[] EmptyMarker = "Empty"u8.ToArray();

    private const string JsonContentType = "application/json; charset=utf-8";

    private readonly byte[] _payload;
    private readonly int _statusCode;

    public CachedJsonResult(byte[] payload, int statusCode)
    {
        _payload = payload;
        _statusCode = statusCode;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var response = context.HttpContext.Response;
        response.StatusCode = _statusCode;
        response.ContentType = JsonContentType;
        response.ContentLength = _payload.Length;
        await response.BodyWriter.WriteAsync(_payload, context.HttpContext.RequestAborted).ConfigureAwait(false);
    }

    internal static bool IsEmptyMarker(byte[] payload) => IsEmptyMarker(payload.AsSpan());

    internal static bool IsEmptyMarker(ReadOnlySpan<byte> payload) => payload.SequenceEqual(EmptyMarker);
}
