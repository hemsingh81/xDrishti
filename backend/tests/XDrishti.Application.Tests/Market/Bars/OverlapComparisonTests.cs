using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars;

public sealed class OverlapComparisonTests
{
    private const long T0 = 1_711_943_100;

    private static Bar Bar(int minute, double price = 100, long volume = 10) => new(T0 + minute * 60L, price, price + 1, price - 1, price + 0.5, volume);

    [Fact]
    public void Identical_bars_match()
    {
        OverlapComparison.Compare([Bar(0), Bar(1)], [Bar(0), Bar(1)]).ShouldBe(new OverlapResult(2, 0));
    }

    [Fact]
    public void Only_the_minutes_both_sides_have_are_compared()
    {
        OverlapComparison.Compare([Bar(0), Bar(1), Bar(2), Bar(5)], [Bar(1), Bar(2), Bar(3), Bar(4)]).ShouldBe(new OverlapResult(2, 0));
        OverlapComparison.Compare([Bar(0)], [Bar(1)]).ShouldBe(new OverlapResult(0, 0));
        OverlapComparison.Compare([], [Bar(1)]).ShouldBe(new OverlapResult(0, 0));
    }

    [Theory]
    [InlineData(100.04, false)]  // 0.04 % off
    [InlineData(100.06, true)]   // 0.06 % off
    [InlineData(99.96, false)]
    [InlineData(99.94, true)]
    public void A_price_differing_by_more_than_five_hundredths_of_a_percent_is_a_mismatch(double fetchedOpen, bool mismatch)
    {
        var stored = Bar(0);
        var fetched = stored with { Open = fetchedOpen };

        OverlapComparison.Compare([fetched], [stored]).Mismatched.ShouldBe(mismatch ? 1 : 0);
    }

    [Theory]
    [InlineData("high")]
    [InlineData("low")]
    [InlineData("close")]
    public void Every_price_of_the_bar_is_compared(string field)
    {
        var stored = Bar(0);
        var fetched = field switch
        {
            "high" => stored with { High = stored.High * 1.01 },
            "low" => stored with { Low = stored.Low * 0.99 },
            _ => stored with { Close = stored.Close * 1.01 },
        };

        OverlapComparison.Compare([fetched], [stored]).ShouldBe(new OverlapResult(1, 1));
    }

    [Fact]
    public void The_volume_is_not_compared_because_sources_count_it_differently()
    {
        OverlapComparison.Compare([Bar(0, volume: 5)], [Bar(0, volume: 5000)]).ShouldBe(new OverlapResult(1, 0));
    }

    [Fact]
    public void A_clock_shifted_by_a_whole_number_of_minutes_shows_up_as_mismatches_everywhere()
    {
        var stored = Enumerable.Range(0, 20).Select(i => Bar(i, 100 + i)).ToList();
        var shifted = stored.Select(b => b with { EpochSeconds = b.EpochSeconds - 60 }).ToList();

        var result = OverlapComparison.Compare(shifted, stored);

        result.Compared.ShouldBe(19);
        result.Mismatched.ShouldBe(19);
    }
}
