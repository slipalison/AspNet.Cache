using System.Buffers;
using System.Text.Json;
using AspNet.Cache.Internal;
using AspNet.Cache.Test;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AspNet.Cache.Benchmarks;

[MemoryDiagnoser]
public class CacheBenchmarks
{
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web);
    private static readonly Func<string, bool> Ignore =
        static name => name.Contains("correlationid", StringComparison.OrdinalIgnoreCase);

    private Dictionary<string, object?> _arguments = null!;
    private InMemoryBufferCache _buffered = null!;
    private MemoryDistributedCache _legacy = null!;
    private string _key = null!;
    private Item[] _items = null!;
    private DistributedCacheEntryOptions _entryOptions = null!;

    [GlobalSetup]
    public void Setup()
    {
        _arguments = new Dictionary<string, object?> { ["id"] = 42, ["name"] = "abc" };
        _key = CacheKeyBuilder.Build("Bench", "/api/bench", _arguments, Ignore, Serializer);
        _items = new Item[10];
        for (var i = 0; i < _items.Length; i++)
            _items[i] = new Item(i, $"item-{i}", i * 1.5);
        _entryOptions = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

        var payload = JsonSerializer.SerializeToUtf8Bytes(_items, Serializer);
        _buffered = new InMemoryBufferCache();
        _buffered.Set(_key, payload, _entryOptions);
        _legacy = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        _legacy.Set(_key, payload, _entryOptions);
    }

    [Benchmark]
    public string KeyGen() => CacheKeyBuilder.Build("Bench", "/api/bench", _arguments, Ignore, Serializer);

    [Benchmark]
    public async Task<int> BufferedHit()
    {
        using var buffer = new PooledArrayBufferWriter();
        await _buffered.TryGetAsync(_key, buffer, default);
        return buffer.WrittenCount;
    }

    [Benchmark]
    public async Task<int> LegacyHit()
    {
        var payload = await _legacy.GetAsync(_key);
        return payload!.Length;
    }

    [Benchmark]
    public async Task StoreSerializeBuffered()
    {
        using var buffer = new PooledArrayBufferWriter(84_000);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(writer, _items, Serializer);
        }

        await _buffered.SetAsync("store", new ReadOnlySequence<byte>(buffer.WrittenMemory), _entryOptions, default);
    }

    public sealed record Item(int Id, string Name, double Value);
}
