using System.Text.Json;

namespace AspNet.Cache.Internal;

internal static class SerializerDefaults
{
    internal static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}
