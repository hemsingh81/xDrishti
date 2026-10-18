using System.Buffers.Binary;
using System.Security.Cryptography;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>Criterion 9 of P3-04: the hash of the stored bars. The known answers were computed with an independent tool (Python 3: <c>struct.pack('&lt;q')</c> and <c>'&lt;d'</c> into <c>hashlib.sha256</c>) for this layout on 2026-10-18 and recomputed during the build.</summary>
public sealed class BarStreamHashTests
{
    private static readonly Bar First = new(1760000040, 100.5, 101.25, 99.75, 100.0, 1234);

    private static readonly Bar Second = new(1760000100, 100.0, 100.5, 99.5, 100.25, 987);

    [Fact]
    public void Two_bars_give_the_known_answer()
    {
        BarStreamHash.Compute([First, Second]).ShouldBe("5028e51da21ebd7fb6f911b4fde15bd16587816fcc70376d5b656e5f608a1a24");
    }

    [Fact]
    public void The_first_bar_alone_gives_the_known_answer()
    {
        BarStreamHash.Compute([First]).ShouldBe("d3022bcc4be32bfe31fca53ccb19bda16b655cda6573491f05267cf378fce29b");
    }

    [Fact]
    public void The_second_close_changed_to_100_5_gives_the_known_answer()
    {
        BarStreamHash.Compute([First, Second with { Close = 100.5 }]).ShouldBe("c9b1f5f0ba2e05ddb9c92d7b065de60fd1f70cc65c0d06fafcae219c8eff1dfe");
    }

    [Fact]
    public void No_bars_give_the_hash_of_the_empty_input()
    {
        BarStreamHash.Compute([]).ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void Two_calls_on_equal_lists_give_equal_hashes_and_the_hash_is_lower_case_hex_of_64_characters()
    {
        var a = BarStreamHash.Compute([First, Second]);
        var b = BarStreamHash.Compute([First, Second]);

        a.ShouldBe(b);
        a.Length.ShouldBe(64);
        a.ShouldBe(a.ToLowerInvariant());
    }

    [Fact]
    public void A_close_changed_by_one_tick_changes_the_hash()
    {
        var changed = BarStreamHash.Compute([First, Second with { Close = Second.Close + 0.05 }]);

        changed.ShouldNotBe(BarStreamHash.Compute([First, Second]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(300)]
    public void The_chunked_hash_equals_the_hash_of_the_whole_byte_string_at_every_chunk_boundary(int count)
    {
        var bars = Enumerable.Range(0, count).Select(i => new Bar(1760000000 + (i * 60L), 100 + i, 101 + i, 99 + i, 100.5 + i, 1000 + i)).ToArray();
        var whole = new byte[count * 48];
        for (var i = 0; i < count; i++)
        {
            var at = i * 48;
            BinaryPrimitives.WriteInt64LittleEndian(whole.AsSpan(at), bars[i].EpochSeconds);
            BinaryPrimitives.WriteDoubleLittleEndian(whole.AsSpan(at + 8), bars[i].Open);
            BinaryPrimitives.WriteDoubleLittleEndian(whole.AsSpan(at + 16), bars[i].High);
            BinaryPrimitives.WriteDoubleLittleEndian(whole.AsSpan(at + 24), bars[i].Low);
            BinaryPrimitives.WriteDoubleLittleEndian(whole.AsSpan(at + 32), bars[i].Close);
            BinaryPrimitives.WriteInt64LittleEndian(whole.AsSpan(at + 40), bars[i].Volume);
        }

        BarStreamHash.Compute(bars).ShouldBe(Convert.ToHexStringLower(SHA256.HashData(whole)));
    }

    [Fact]
    public void Every_field_counts_the_epoch_each_price_and_the_volume()
    {
        var baseline = BarStreamHash.Compute([First]);

        new[]
        {
            First with { EpochSeconds = First.EpochSeconds + 60 },
            First with { Open = First.Open + 0.05 },
            First with { High = First.High + 0.05 },
            First with { Low = First.Low + 0.05 },
            First with { Close = First.Close + 0.05 },
            First with { Volume = First.Volume + 1 },
        }.Select(b => BarStreamHash.Compute([b])).ShouldAllBe(h => h != baseline);
    }

    [Fact]
    public void A_null_list_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => BarStreamHash.Compute(null!));
    }
}
