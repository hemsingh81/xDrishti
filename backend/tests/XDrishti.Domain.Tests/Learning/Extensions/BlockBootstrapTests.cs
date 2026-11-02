using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>P13-05 criterion 8: the paired block bootstrap.</summary>
public sealed class BlockBootstrapTests
{
    private static BootstrapBlock[] Blocks(int count, Func<int, double> mean, int rows = 4) => [.. Enumerable.Range(0, count).Select(i => new BootstrapBlock(mean(i) * rows, rows))];

    [Fact]
    public void Equal_differences_in_every_block_give_one_value_for_both_bounds_and_the_point()
    {
        var result = BlockBootstrap.Bounds(Blocks(20, _ => 0.5), 1_000, 1337, 0.9);

        (result.Point, result.Lower, result.Upper).ShouldBe((0.5, 0.5, 0.5));
        result.StandardError.ShouldBe(0.0);
    }

    [Fact]
    public void The_bounds_surround_the_point_for_random_cases()
    {
        for (var seed = 1; seed <= 50; seed++)
        {
            var random = new XDrishti.Domain.Lab.SeededRandom((ulong)seed);
            var blocks = Blocks(30, _ => (random.Next(2001) - 1000) / 1000.0, 1 + random.Next(6));

            var result = BlockBootstrap.Bounds(blocks, 1_000, 1337, 0.9);

            result.Lower.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(result.Point!.Value);
            result.Upper.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(result.Point!.Value);
        }
    }

    [Fact]
    public void Fewer_than_two_blocks_leave_the_bounds_unavailable_and_never_a_value()
    {
        var one = BlockBootstrap.Bounds(Blocks(1, _ => 0.3), 1_000, 1337, 0.9);
        var none = BlockBootstrap.Bounds([], 1_000, 1337, 0.9);

        (one.Lower, one.Upper, one.StandardError).ShouldBe((null, null, null));
        one.Point.ShouldBe(0.3);
        (none.Point, none.Lower, none.Upper).ShouldBe((null, null, null));
    }

    [Fact]
    public void The_draws_depend_on_the_seed_only()
    {
        var random = new XDrishti.Domain.Lab.SeededRandom(5);
        var blocks = Blocks(40, _ => (random.Next(2001) - 1000) / 1000.0);

        var a = BlockBootstrap.Bounds(blocks, 2_000, 1337, 0.9);
        var b = BlockBootstrap.Bounds(blocks, 2_000, 1337, 0.9);
        var other = BlockBootstrap.Bounds(blocks, 2_000, 1338, 0.9);

        b.ShouldBe(a);
        other.Lower.ShouldNotBe(a.Lower);
    }

    [Fact]
    public void The_bounds_are_the_one_thousandth_and_nine_thousandth_of_ten_thousand_draws()
    {
        var random = new XDrishti.Domain.Lab.SeededRandom(9);
        var blocks = Blocks(25, _ => (random.Next(2001) - 1000) / 1000.0);

        var result = BlockBootstrap.Bounds(blocks, 10_000, 1337, 0.9);

        // Reproduce the draws with the same generator and read the order statistics by hand.
        var generator = new XDrishti.Domain.Lab.SeededRandom(1337);
        var values = new double[10_000];
        for (var d = 0; d < values.Length; d++)
        {
            double sum = 0;
            var count = 0;
            for (var i = 0; i < blocks.Length; i++)
            {
                var b = blocks[generator.Next(blocks.Length)];
                sum += b.Sum;
                count += b.Count;
            }

            values[d] = sum / count;
        }

        Array.Sort(values);
        result.Lower.ShouldBe(values[999]);
        result.Upper.ShouldBe(values[8_999]);
    }
}
