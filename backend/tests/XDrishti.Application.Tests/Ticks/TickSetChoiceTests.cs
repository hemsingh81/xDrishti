using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Application.Ticks;
using static XDrishti.Application.Tests.Ticks.TickTestData;

namespace XDrishti.Application.Tests.Ticks;

/// <summary>The day's tick set (P7-16b criterion 5): the depth set when stored, wait before 09:14:30 when it is not, built otherwise.</summary>
public sealed class TickSetChoiceTests
{
    private static DateTimeOffset At(int hour, int minute, int second) => new(2026, 10, 12, hour, minute, second, Ist);

    [Theory]
    [InlineData(9, 14, 0)]
    [InlineData(9, 14, 30)]
    [InlineData(11, 0, 0)]
    public void A_stored_depth_set_is_used_whenever_it_is_there(int hour, int minute, int second)
    {
        TickSetChoice.Decide(At(hour, minute, second), depthSetStored: true).ShouldBe(TickSetAction.UseDepthSet);
    }

    [Fact]
    public void Without_a_depth_set_before_09_14_30_it_waits()
    {
        TickSetChoice.Decide(At(9, 14, 29), depthSetStored: false).ShouldBe(TickSetAction.Wait);
    }

    [Theory]
    [InlineData(9, 14, 30)]
    [InlineData(10, 30, 0)]
    public void Without_a_depth_set_from_09_14_30_on_it_is_built(int hour, int minute, int second)
    {
        TickSetChoice.Decide(At(hour, minute, second), depthSetStored: false).ShouldBe(TickSetAction.Build);
    }

    [Fact]
    public void A_built_set_keeps_order_reasons_and_says_built()
    {
        var built = TickSetChoice.FromBuilt([new DepthSetEntry(Reliance, RelianceId, "NSE:RELIANCE", "armed", 1), new DepthSetEntry(Tcs, TcsId, "NSE:TCS", "turnover", 2)]);

        built.Select(e => (e.Source, e.Reason, e.Rank)).ShouldBe([(TickSources.Built, "armed", 1), (TickSources.Built, "turnover", 2)]);
    }
}
