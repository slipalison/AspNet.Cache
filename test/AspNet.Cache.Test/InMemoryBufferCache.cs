using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;

namespace AspNet.Cache.Test;

/// <summary>In-process <see cref="IBufferDistributedCache"/> used by tests and benchmarks. Not for production.</summary>
internal sealed class InMemoryBufferCache : IBufferDistributedCache
{
    private readonly ConcurrentDictionary<string, byte[]> _entries = new();

    public byte[]? Get(string key) => _entries.TryGetValue(key, out var value) ? value : null;

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

    public void Refresh(string key)
    {
    }

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Remove(string key) => _entries.TryRemove(key, out _);

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _entries[key] = value;

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }

    public bool TryGet(string key, IBufferWriter<byte> destination)
    {
        if (!_entries.TryGetValue(key, out var value))
            return false;
        destination.Write(value);
        return true;
    }

    public ValueTask<bool> TryGetAsync(string key, IBufferWriter<byte> destination, CancellationToken token = default) =>
        new(TryGet(key, destination));

    public void Set(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options) =>
        _entries[key] = value.ToArray();

    public ValueTask SetAsync(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        Set(key, value, options);
        return ValueTask.CompletedTask;
    }
}
