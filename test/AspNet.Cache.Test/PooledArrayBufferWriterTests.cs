using AspNet.Cache.Internal;
using Shouldly;
using Xunit;

namespace AspNet.Cache.Test;

public sealed class PooledArrayBufferWriterTests
{
    [Fact]
    public void Growth_PreservesContentAcrossRents()
    {
        using var writer = new PooledArrayBufferWriter(initialCapacity: 16);

        for (var i = 0; i < 100; i++)
        {
            var span = writer.GetSpan(64);
            span[..64].Fill((byte)i);
            writer.Advance(64);
        }

        writer.WrittenCount.ShouldBe(6400);
        writer.WrittenSpan[0].ShouldBe((byte)0);
        writer.WrittenSpan[^1].ShouldBe((byte)99);
    }

    [Fact]
    public void SoftLimitExceeded_SetsOverflowed()
    {
        using var writer = new PooledArrayBufferWriter(softLimit: 8);

        writer.GetSpan(32)[..32].Fill(0xAB);
        writer.Advance(32);

        writer.Overflowed.ShouldBeTrue();
        writer.WrittenCount.ShouldBe(32);
    }

    [Fact]
    public void NegativeAdvance_Throws()
    {
        using var writer = new PooledArrayBufferWriter();

        Should.Throw<ArgumentOutOfRangeException>(() => writer.Advance(-1));
    }

    [Fact]
    public void AdvanceBeyondCapacity_Throws()
    {
        using var writer = new PooledArrayBufferWriter(initialCapacity: 16);

        Should.Throw<ArgumentOutOfRangeException>(() => writer.Advance(1_000_000));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var writer = new PooledArrayBufferWriter();
        writer.Dispose();
        writer.Dispose();

        writer.WrittenCount.ShouldBe(0);
    }
}
