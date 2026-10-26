using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>P8-11: does the stored entry still fit the bars (within one tick of the first bar at or after the entry minute).</summary>
public sealed class ReplayCoherenceTests
{
    private const long Entry = 1_772_000_040;

    private static Bar Minute(long epoch, double open, double low = 99, double high = 102) => new(epoch, open, high, low, open, 100);

    [Fact]
    public void An_open_within_one_tick_is_ok()
    {
        var result = ReplayCoherence.Check([Minute(Entry - 60, 90), Minute(Entry, 100.05)], Entry, 100, 0.05, false);

        result.State.ShouldBe(ReplayCoherenceState.Ok);
        result.EntryOnBars.ShouldBe(100.05);
    }

    [Fact]
    public void Six_tenths_of_a_tick_after_a_split_is_still_ok_but_a_halved_price_is_a_mismatch()
    {
        ReplayCoherence.Check([Minute(Entry, 100.03)], Entry, 100, 0.05, false).State.ShouldBe(ReplayCoherenceState.Ok);

        var result = ReplayCoherence.Check([Minute(Entry, 50)], Entry, 100, 0.05, false);

        result.State.ShouldBe(ReplayCoherenceState.EntryMismatch);
        result.EntryStored.ShouldBe(100);
        result.EntryOnBars.ShouldBe(50);
        result.Detail.ShouldContain("no longer match");
    }

    [Fact]
    public void A_limit_entry_only_has_to_lie_inside_the_bar()
    {
        ReplayCoherence.Check([Minute(Entry, 101, low: 99.5, high: 102)], Entry, 100, 0.05, true).State.ShouldBe(ReplayCoherenceState.Ok);
        ReplayCoherence.Check([Minute(Entry, 101, low: 100.5, high: 102)], Entry, 100, 0.05, true).State.ShouldBe(ReplayCoherenceState.EntryMismatch);
    }

    [Fact]
    public void No_bar_at_or_after_the_entry_is_no_bars()
    {
        ReplayCoherence.Check([], Entry, 100, 0.05, false).State.ShouldBe(ReplayCoherenceState.NoBars);
        ReplayCoherence.Check([Minute(Entry - 60, 100)], Entry, 100, 0.05, false).State.ShouldBe(ReplayCoherenceState.NoBars);
    }
}
