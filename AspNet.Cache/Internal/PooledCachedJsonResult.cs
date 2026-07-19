using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace AspNet.Cache.Internal;

/// <summary>
/// Writes a cached UTF-8 JSON payload held in a pooled buffer straight to the response body,
/// returning the buffer to the pool after the write completes.
/// </summary>
internal sealed class PooledCachedJsonResult : IActionResult
{
    private const string JsonContentType = "application/json; charset=utf-8";

    private readonly PooledArrayBufferWriter _buffer;
    private readonly int _statusCode;

    public PooledCachedJsonResult(PooledArrayBufferWriter buffer, int statusCode)
    {
        _buffer = buffer;
        _statusCode = statusCode;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var response = context.HttpContext.Response;
            response.StatusCode = _statusCode;
            response.ContentType = JsonContentType;
            response.ContentLength = _buffer.WrittenCount;
            await response.BodyWriter.WriteAsync(_buffer.WrittenMemory, context.HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        finally
        {
            _buffer.Dispose();
        }
    }
}
