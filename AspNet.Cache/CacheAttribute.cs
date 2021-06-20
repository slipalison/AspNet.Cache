using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;

namespace AspNet.Cache
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
      public sealed class CacheAttribute : ActionFilterAttribute
    {
        private static readonly IList<(TimeSpanType TimeSpanType, Func<double, TimeSpan> TimeSpanFrom)>
            _timeSpanTypeList =
                new List<(TimeSpanType TimeSpanType, Func<double, TimeSpan> TimeSpanFrom)>
                {
                    (TimeSpanType.FromMinutes, TimeSpan.FromMinutes),
                    (TimeSpanType.FromMilliseconds, TimeSpan.FromMilliseconds),
                    (TimeSpanType.FromDays, TimeSpan.FromDays),
                    (TimeSpanType.FromHours, TimeSpan.FromHours),
                    (TimeSpanType.FromSeconds, TimeSpan.FromSeconds)
                };

        private IDistributedCache _cache;
        private string _hashKey = string.Empty;
        private bool _isCach;
        private string _path = string.Empty;

        public string Folder { get; set; } = string.Empty;
        public long ExpireAt { get; set; } = 10;
        public TimeSpanType TimeSpanType { get; set; } = TimeSpanType.FromMinutes;
        public HttpStatusCode SuccessStatus { get; set; } = HttpStatusCode.OK;

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            _cache = (IDistributedCache) context.HttpContext.RequestServices.GetService(typeof(IDistributedCache));
            if (context.HttpContext.Request.Path.HasValue)
                _path = context.HttpContext.Request.Path.Value;

            var body = JsonConvert.SerializeObject(context.ActionArguments.Where(x =>
                !x.Key.Contains("correlationid", StringComparison.InvariantCultureIgnoreCase)));
            var separotor = string.IsNullOrWhiteSpace(Folder) ? string.Empty : ":";
            _hashKey =
                $"{Folder}{separotor}{string.Join("-", _path.Replace("/", "-"), GetStringSha256Hash(body))}";
            var existingData = _cache.GetString(_hashKey);
            if (string.IsNullOrEmpty(existingData))
            {
                _isCach = false;
                base.OnActionExecuting(context);
            }
            else
            {
                _isCach = true;
                context.HttpContext.Response.StatusCode = (int) SuccessStatus;
                context.HttpContext.Response.Headers.Clear();
                context.HttpContext.Response.Headers.Add("Cache-Control",
                    new StringValues($"max-age={GetTimeSpan().Seconds}"));
                if (string.IsNullOrEmpty(existingData)) return;
                if ("Empty" == existingData)
                    context.Result = new StatusCodeResult((int) SuccessStatus);
                else
                    context.Result = new JsonResult(JsonConvert.DeserializeObject(existingData));
            }
        }

        public override void OnResultExecuted(ResultExecutedContext context)
        {
            if (!_isCach && context.HttpContext.Response.StatusCode == (int) SuccessStatus)
            {
                switch (context.Result)
                {
                    case ObjectResult {Value: { }} result:
                        _cache.SetString(_hashKey, JsonConvert.SerializeObject(result.Value),
                            new DistributedCacheEntryOptions {AbsoluteExpirationRelativeToNow = GetTimeSpan()});
                        break;
                    case StatusCodeResult _:
                        _cache.SetString(_hashKey, "Empty",
                            new DistributedCacheEntryOptions {AbsoluteExpirationRelativeToNow = GetTimeSpan()});
                        break;
                }
            }

            base.OnResultExecuted(context);
        }

        private TimeSpan GetTimeSpan()
        {
            return _timeSpanTypeList.FirstOrDefault(x => x.TimeSpanType == TimeSpanType).TimeSpanFrom.Invoke(ExpireAt);
        }

        private static string GetStringSha256Hash(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            using var sha = new SHA256Managed();
            var textData = Encoding.UTF8.GetBytes(text);
            var hash = sha.ComputeHash(textData);
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }
    }
}
