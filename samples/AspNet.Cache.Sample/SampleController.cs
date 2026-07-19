using AspNet.Cache;
using Microsoft.AspNetCore.Mvc;

namespace AspNet.Cache.Sample;

[ApiController]
[Route("api/[controller]")]
public sealed class SampleController : ControllerBase
{
    private static readonly string[] Summaries =
        ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

    [HttpGet]
    [Cache(ExpireAt = 5, TimeSpanType = TimeSpanType.FromMinutes)]
    public IReadOnlyList<Forecast> Get(int id)
    {
        var items = new Forecast[5];
        for (var i = 0; i < items.Length; i++)
            items[i] = new Forecast(id, DateTime.UtcNow.AddDays(i), Random.Shared.Next(-20, 55), Summaries[i % Summaries.Length]);
        return items;
    }
}

public sealed record Forecast(int Id, DateTime Date, int TemperatureC, string Summary);
