# AspNet.Cache

![.NET Core](https://github.com/slipalison/AspNet.Cache/workflows/.NET%20Core/badge.svg?event=push)
![.NET Core](https://github.com/slipalison/AspNet.Cache/workflows/.NET%20Core/badge.svg)
[![codecov](https://codecov.io/gh/slipalison/AspNet.Cache/branch/master/graph/badge.svg)](https://codecov.io/gh/slipalison/AspNet.Cache)

### Features
> ...

----

## C#　

```C#
	[ApiController, Route("api/[controller]")]
    public class DefaultController : ControllerBase
    {
        private readonly ILogger<DefaultController> _logger;
        public DefaultController(ILogger<DefaultController> logger)
        {
            _logger = logger;
        }

        [HttpGet, Cache(Folder = "Test", SuccessStatus = System.Net.HttpStatusCode.Created, TimeSpanType = TimeSpanType.FromDays, ExpireAt = 12)]
        public IEnumerable<WeatherForecast> Get([Required, FromHeader(Name = "X-Correlation-Id")] string correlationId)
        {
			...
        }

        [HttpGet("get1"), Cache]
        public IEnumerable<WeatherForecast> Get1([Required, FromHeader(Name = "X-Correlation-Id")] string correlationId)
        {
				...
        }
    }
```

---
