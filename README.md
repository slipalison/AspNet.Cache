# AspNet.Cache

[![CI](https://github.com/slipalison/AspNet.Cache/actions/workflows/ci.yml/badge.svg)](https://github.com/slipalison/AspNet.Cache/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/AspNet.Cache.svg)](https://www.nuget.org/packages/AspNet.Cache/)
[![Downloads](https://img.shields.io/nuget/dt/AspNet.Cache.svg)](https://www.nuget.org/packages/AspNet.Cache/)

**English** | [Português](#português-br)

Declarative response caching for ASP.NET Core MVC actions — one attribute, any
[`IDistributedCache`](https://learn.microsoft.com/aspnet/core/performance/caching/distributed) provider.

```csharp
[HttpGet]
[Cache(Folder = "Weather", ExpireAt = 5, TimeSpanType = TimeSpanType.FromMinutes)]
public IReadOnlyList<Forecast> Get(int id) => _service.Load(id);
```

First call runs the action and stores the serialized JSON response. Subsequent calls with the same
route + arguments are served straight from the cache — the action never executes.

## Why AspNet.Cache

- **Provider-agnostic by design.** Anything that registers `IDistributedCache` works:
  Redis (`AddStackExchangeRedisCache`), SQL Server (`AddDistributedSqlServerCache`),
  in-memory (`AddDistributedMemoryCache`), MongoDB (community providers), NCache, and more.
  You are never locked in.
- **Allocation-conscious hot path.** Cache keys are built with `stackalloc`/`ArrayPool` spans and a
  static `SHA256.HashData` (~5× fewer bytes per operation than v1, zero Gen2 impact). Cache hits write
  the stored UTF-8 bytes directly to the response body — no deserialize/re-serialize round-trip.
- **`IBufferDistributedCache` fast path.** On .NET 9+ providers that implement it (e.g.
  StackExchange.Redis provider 9.x), reads and writes flow through pooled buffers — no per-request
  payload `byte[]` at all. Older providers fall back to the classic path automatically.
- **LOH-safe by default.** Responses larger than `MaxPayloadBytes` (default 84,000 bytes — right under
  the 85 KB Large Object Heap threshold) are simply not cached, so the cache layer never fragments
  the LOH of a long-running service.
- **Fail-open.** If the cache provider is down, requests are served without cache and a single
  `Warning` is logged. A cache outage never becomes an API outage.
- **Thread-safe.** The filter holds no per-request state (MVC shares attribute instances across
  concurrent requests — v1 had a race here; v2 is stateless by construction and regression-tested
  with 500 concurrent requests).

## Install

```bash
dotnet add package AspNet.Cache
```

Requires .NET 8.0+.

## Quick start

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDistributedMemoryCache();          // or Redis, SQL Server, Mongo...

var app = builder.Build();
app.MapControllers();
app.Run();
```

```csharp
[ApiController]
[Route("api/[controller]")]
public sealed class WeatherController : ControllerBase
{
    // Cache for 12 hours, replay hits with 201 Created
    [HttpGet]
    [Cache(Folder = "Weather", ExpireAt = 12, TimeSpanType = TimeSpanType.FromHours,
        SuccessStatus = HttpStatusCode.Created)]
    public IReadOnlyList<Forecast> Get(int id) => Load(id);
}
```

### Attribute settings

| Property | Default | Meaning |
|---|---|---|
| `Folder` | `""` | Key prefix (`Folder:` is prepended to every key) |
| `ExpireAt` | `10` | Expiration amount, in `TimeSpanType` units |
| `TimeSpanType` | `FromMinutes` | Unit: milliseconds, seconds, minutes, hours or days |
| `SuccessStatus` | `200 OK` | Status that marks a response cacheable, and the status replayed on hits |

### Global options

```csharp
builder.Services.Configure<AspNetCacheOptions>(options =>
{
    options.MaxPayloadBytes = 32_000;                       // skip caching anything bigger
    options.IgnoreArgument = name =>                        // args excluded from the cache key
        name.Contains("correlationid", StringComparison.OrdinalIgnoreCase);
    options.PayloadSerializerOptions = mySerializerOptions; // defaults to your MVC JSON options
});
```

### How the key is built

```
{Folder}:{request path with "/" → "-"}-{SHA-256 hex of the JSON of the action arguments}
Weather:-api-weather-E39743B91B98367F...
```

Arguments matching `IgnoreArgument` (correlation ids by default) don't affect the key.
SHA-256 (not a fast non-crypto hash) is deliberate: keys derive from untrusted input, and a forgeable
hash would allow cross-user cache collisions.

### Notes & trade-offs

- Cached responses are always served as `application/json` (content negotiation does not apply to hits).
- The response is cached only when its status matches `SuccessStatus` exactly.
- Bodyless responses (`StatusCodeResult`) are cached with a tiny sentinel and replayed as status-only.
- On a cache hit the action does not run — side effects inside the action won't happen.

## Performance

Measured on the v2 implementation (net8.0, x64, Release; BenchmarkDotNet, short job):

| Path | v1 | v2 |
|---|---|---|
| Key generation | 1,784 B/op | **344 B/op** |
| Hit read via `IBufferDistributedCache` | n/a | **104 B/op** (pooled) |
| Hit read via legacy `IDistributedCache` | n/a | **144 B/op** |
| Buffered store (serialize + set) | n/a | **864 B/op** |
| Gen1 / Gen2 collections | — | **0** (all paths Gen0-only) |

CI enforces allocation budgets on every PR (`build/check-allocation-budgets.ps1`) — allocated bytes
per operation are deterministic, so regressions fail the build.

> **On "1 million requests/second".** At that scale the bottleneck is the cache provider round-trip
> and the network, and the target is reached by horizontal scale-out (N instances). This library's job
> is to add no GC pressure and no payload copy on the request path — which the budgets above guarantee.

## Migrating from v1

| Change | Impact |
|---|---|
| Target framework `netstandard2.1` → `net8.0` | Apps must run .NET 8+ |
| Newtonsoft.Json → System.Text.Json | Serialization follows your MVC JSON options (camelCase by default) |
| Key format internals changed | Existing cache entries miss once after upgrade (cache warms itself) |
| `Cache-Control: max-age` | Now correct (`TotalSeconds`; v1 emitted the 0–59 component) |
| Response headers on hit | No longer cleared — CORS/security headers survive |
| Concurrency | Race condition fixed; safe under parallel load |

## License

[MIT](LICENSE)

---

## Português (BR)

[English](#aspnetcache) | **Português**

Cache de resposta declarativo para actions ASP.NET Core MVC — um attribute, qualquer provider de
[`IDistributedCache`](https://learn.microsoft.com/aspnet/core/performance/caching/distributed).

```csharp
[HttpGet]
[Cache(Folder = "Weather", ExpireAt = 5, TimeSpanType = TimeSpanType.FromMinutes)]
public IReadOnlyList<Forecast> Get(int id) => _service.Load(id);
```

A primeira chamada executa a action e grava a resposta JSON serializada. Chamadas seguintes com o
mesmo path + argumentos são servidas direto do cache — a action nem executa.

## Por quê

- **Agnóstico de provider.** Qualquer `IDistributedCache` funciona: Redis, SQL Server, memória,
  MongoDB (providers da comunidade), NCache. Sem lock-in.
- **Hot path consciente de alocação.** Chaves geradas com spans (`stackalloc`/`ArrayPool`) e
  `SHA256.HashData` estático (~5× menos bytes por operação que a v1, zero impacto em Gen2). Hits
  escrevem os bytes UTF-8 armazenados direto no body — sem deserializar/re-serializar.
- **Fast path `IBufferDistributedCache`.** Em providers .NET 9+ que o implementam (ex.: Redis 9.x),
  leituras e escritas usam buffers pooled — nenhum `byte[]` de payload por request. Providers antigos
  caem automaticamente no caminho clássico.
- **Seguro para a LOH por default.** Respostas maiores que `MaxPayloadBytes` (default 84.000 bytes —
  logo abaixo do limiar de 85 KB da Large Object Heap) não são cacheadas: a camada de cache nunca
  fragmenta a LOH de um serviço de longa vida.
- **Fail-open.** Provider fora do ar → request segue sem cache com um único log `Warning`. Queda do
  cache nunca vira queda da API.
- **Thread-safe.** O filtro não guarda estado por-request (o MVC compartilha a instância do attribute
  entre requests concorrentes — a v1 tinha race condition aqui; a v2 é stateless por construção, com
  teste de regressão de 500 requests paralelos).

## Instalação

```bash
dotnet add package AspNet.Cache
```

Requer .NET 8.0+.

## Início rápido

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDistributedMemoryCache();          // ou Redis, SQL Server, Mongo...

var app = builder.Build();
app.MapControllers();
app.Run();
```

### Configurações do attribute

| Propriedade | Default | Significado |
|---|---|---|
| `Folder` | `""` | Prefixo da chave |
| `ExpireAt` | `10` | Quantidade de expiração, na unidade de `TimeSpanType` |
| `TimeSpanType` | `FromMinutes` | Milissegundos, segundos, minutos, horas ou dias |
| `SuccessStatus` | `200 OK` | Status que torna a resposta cacheável, e que é devolvido nos hits |

## Opções globais

```csharp
builder.Services.Configure<AspNetCacheOptions>(options =>
{
    options.MaxPayloadBytes = 32_000;
    options.IgnoreArgument = name =>
        name.Contains("correlationid", StringComparison.OrdinalIgnoreCase);
});
```

## Como a chave é montada

```
{Folder}:{path da request com "/" → "-"}-{hex SHA-256 do JSON dos argumentos da action}
Weather:-api-weather-E39743B91B98367F...
```

Argumentos que casam com `IgnoreArgument` (correlation ids por default) não afetam a chave.
SHA-256 (não um hash rápido não-criptográfico) é proposital: a chave deriva de input não confiável, e
um hash forjável permitiria colisão de cache entre usuários.

## Observações

- Hits são sempre `application/json` (content negotiation não se aplica).
- Só cacheia quando o status da resposta é exatamente `SuccessStatus`.
- Respostas sem corpo (`StatusCodeResult`) são cacheadas com um sentinela pequeno e devolvidas como
  status puro.
- Em um hit a action não executa — efeitos colaterais dentro dela não acontecem.

## Desempenho

Medido na v2 (net8.0, x64, Release; BenchmarkDotNet, job short):

| Caminho | v1 | v2 |
|---|---|---|
| Geração de chave | 1.784 B/op | **344 B/op** |
| Leitura no hit via `IBufferDistributedCache` | n/a | **104 B/op** (pooled) |
| Leitura no hit via `IDistributedCache` legado | n/a | **144 B/op** |
| Gravação buffered (serialize + set) | n/a | **864 B/op** |
| Coletas Gen1 / Gen2 | — | **0** (todos os caminhos Gen0-only) |

O CI aplica os budgets de alocação em todo PR (`build/check-allocation-budgets.ps1`) — bytes por
operação são determinísticos, então regressão quebra o build.

> **Sobre "1 milhão de requests/segundo".** Nessa escala o gargalo é o round-trip do provider de cache
> e a rede, e o alvo se atinge com scale-out horizontal (N instâncias). O papel desta biblioteca é não
> adicionar pressão de GC nem cópia de payload no caminho da request — o que os budgets acima garantem.

## Migração v1 → v2

Requer .NET 8+; serialização passa a System.Text.Json (segue as opções JSON do seu MVC); o cache
esquenta de novo no upgrade (mudança interna do formato de chave); `max-age` corrigido
(`TotalSeconds`); headers preservados no hit; race condition corrigida.

## Licença

[MIT](LICENSE)
