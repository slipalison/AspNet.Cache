using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace AspNet.Cache.Test;

internal static class FilterContextFactory
{
    public static (ActionExecutingContext Context, DefaultHttpContext Http) Create(IServiceProvider services,
        string path, Dictionary<string, object?> arguments)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Path = path;
        http.Response.Body = new MemoryStream();
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), arguments, new object());
        return (context, http);
    }

    public static ActionExecutionDelegate NextReturning(ActionExecutingContext context, IActionResult result,
        Action? onExecuted = null) => () =>
    {
        onExecuted?.Invoke();
        return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), new object())
        {
            Result = result
        });
    };

    public static async Task<string> ReadBodyAsync(DefaultHttpContext http, IActionResult result)
    {
        await result.ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor()));
        http.Response.Body.Position = 0;
        using var reader = new StreamReader(http.Response.Body);
        return await reader.ReadToEndAsync();
    }
}
