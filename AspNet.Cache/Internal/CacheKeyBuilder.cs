using System;
using System.Buffers;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;

namespace AspNet.Cache.Internal;

/// <summary>Builds the cache key: <c>{Folder}:{path with '/' replaced by '-'}-{SHA256 hex of the JSON of the action arguments}</c>.</summary>
internal static class CacheKeyBuilder
{
    private const int HashSize = 32;
    private const int HexLength = HashSize * 2;
    private const int StackallocCharThreshold = 256;
    private const string HexDigits = "0123456789ABCDEF";

    public static string Build(string folder, string? path, IDictionary<string, object?> arguments,
        Func<string, bool> ignoreArgument, JsonSerializerOptions keySerializerOptions)
    {
        Span<byte> hash = stackalloc byte[HashSize];
        HashArguments(arguments, ignoreArgument, keySerializerOptions, hash);

        var pathSpan = path.AsSpan();
        var prefixLength = folder.Length == 0 ? 0 : folder.Length + 1;
        var totalLength = prefixLength + pathSpan.Length + 1 + HexLength;

        char[]? rented = null;
        Span<char> destination = totalLength <= StackallocCharThreshold
            ? stackalloc char[StackallocCharThreshold]
            : rented = ArrayPool<char>.Shared.Rent(totalLength);

        var position = 0;
        if (prefixLength != 0)
        {
            folder.CopyTo(destination);
            position = folder.Length;
            destination[position++] = ':';
        }

        foreach (var character in pathSpan)
            destination[position++] = character == '/' ? '-' : character;

        destination[position++] = '-';
        WriteUpperHex(hash, destination.Slice(position, HexLength));
        position += HexLength;

        var key = new string(destination[..position]);
        if (rented is not null)
            ArrayPool<char>.Shared.Return(rented);
        return key;
    }

    private static void HashArguments(IDictionary<string, object?> arguments, Func<string, bool> ignoreArgument,
        JsonSerializerOptions serializerOptions, Span<byte> destination)
    {
        using var buffer = new PooledArrayBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (arguments is Dictionary<string, object?> concrete)
            {
                foreach (var pair in concrete)
                    WriteArgument(writer, pair.Key, pair.Value, ignoreArgument, serializerOptions);
            }
            else
            {
                foreach (var pair in arguments)
                    WriteArgument(writer, pair.Key, pair.Value, ignoreArgument, serializerOptions);
            }

            writer.WriteEndObject();
        }

        SHA256.HashData(buffer.WrittenSpan, destination);
    }

    private static void WriteArgument(Utf8JsonWriter writer, string name, object? value,
        Func<string, bool> ignoreArgument, JsonSerializerOptions serializerOptions)
    {
        if (ignoreArgument(name))
            return;
        writer.WritePropertyName(name);
        JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object), serializerOptions);
    }

    private static void WriteUpperHex(ReadOnlySpan<byte> source, Span<char> destination)
    {
        for (var i = 0; i < source.Length; i++)
        {
            destination[i * 2] = HexDigits[source[i] >> 4];
            destination[i * 2 + 1] = HexDigits[source[i] & 0xF];
        }
    }
}
