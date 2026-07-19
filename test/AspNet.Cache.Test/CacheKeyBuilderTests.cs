using System.Text.Json;
using AspNet.Cache.Internal;
using Shouldly;
using Xunit;

namespace AspNet.Cache.Test;

public sealed class CacheKeyBuilderTests
{
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web);
    private static readonly Func<string, bool> Ignore =
        static name => name.Contains("correlationid", StringComparison.OrdinalIgnoreCase);

    private static string Build(string folder, string path, Dictionary<string, object?> args) =>
        CacheKeyBuilder.Build(folder, path, args, Ignore, Serializer);

    [Fact]
    public void SameArguments_ProduceSameKey_IgnoringCorrelationId()
    {
        var first = Build("Test", "/api/default",
            new() { ["id"] = 42, ["xCorrelationId"] = Guid.NewGuid().ToString() });
        var second = Build("Test", "/api/default",
            new() { ["id"] = 42, ["xCorrelationId"] = Guid.NewGuid().ToString() });

        first.ShouldBe(second);
    }

    [Fact]
    public void DifferentArguments_ProduceDifferentKeys()
    {
        var first = Build("Test", "/api/default", new() { ["id"] = 42 });
        var second = Build("Test", "/api/default", new() { ["id"] = 43 });

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Key_HasFolderPrefix_SlashesReplaced_And64HexSuffix()
    {
        var key = Build("Test", "/api/default", new() { ["id"] = 1 });

        key.ShouldStartWith("Test:-api-default-");
        key.Length.ShouldBe("Test:-api-default-".Length + 64);
        key.ShouldNotContain("/");
    }

    [Fact]
    public void EmptyFolder_OmitsPrefixSeparator()
    {
        var key = Build(string.Empty, "/api/default", new() { ["id"] = 1 });

        key.ShouldStartWith("-api-default-");
    }

    [Fact]
    public void LongPath_UsesPooledBuffer_AndKeepsCorrectness()
    {
        var longPath = "/api/" + new string('x', 400);

        var key = Build("Test", longPath, new() { ["id"] = 1 });

        key.Length.ShouldBe("Test:".Length + longPath.Length + 1 + 64);
        key.ShouldNotContain("/");
    }

    [Fact]
    public void ComplexObjectArgument_InfluencesKey()
    {
        var withBody = Build("Test", "/api/default", new() { ["body"] = new Payload(7, "coffee") });
        var without = Build("Test", "/api/default", new());

        withBody.ShouldNotBe(without);
    }

    private sealed record Payload(int Id, string Name);
}
