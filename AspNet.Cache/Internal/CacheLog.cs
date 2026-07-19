using System;
using Microsoft.Extensions.Logging;

namespace AspNet.Cache.Internal;

internal static partial class CacheLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "AspNet.Cache: distributed cache read failed; the request continues without cache.")]
    public static partial void GetFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "AspNet.Cache: distributed cache write failed; the response was served but not cached.")]
    public static partial void SetFailed(ILogger logger, Exception exception);
}
