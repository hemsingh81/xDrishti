using XDrishti.Application.Research;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>CF-05, D2 (ADR 0229): the tier of an instrument on an entry date from the 20 sessions before it; too little history is unavailable, never tier A.</summary>
public sealed class EntryTiersTests
{
    private static readonly DateOnly Start = new(2026, 1, 5);

    /// <summary>One bar a day from <see cref="Start"/> with the given traded value in crore (close 100, volume from the value).</summary>
    private static List<DailyBar> Bars(int days, decimal crore) =>
        [.. Enumerable.Range(0, days).Select(i => new DailyBar(Start.AddDays(i), 100, 100, 100, 100, (long)(crore * LiquidityTiers.Crore / 100m)))];

    [Theory]
    [InlineData(150, LiquidityTier.A)]
    [InlineData(50, LiquidityTier.B)]
    [InlineData(5, LiquidityTier.C)]
    public void The_median_of_the_twenty_sessions_before_the_entry_sets_the_tier(int crore, LiquidityTier expected) =>
        EntryTiers.Of(Bars(25, crore), new CostStressOptions())(Start.AddDays(22)).ShouldBe(expected);

    [Fact]
    public void Fewer_than_twenty_sessions_before_the_entry_is_unavailable_and_so_is_an_empty_history()
    {
        var tier = EntryTiers.Of(Bars(25, 500), new CostStressOptions());

        tier(Start.AddDays(19)).ShouldBe(LiquidityTier.Unavailable, "19 sessions before it");
        tier(Start.AddDays(20)).ShouldBe(LiquidityTier.A);
        EntryTiers.Of([], new CostStressOptions())(Start).ShouldBe(LiquidityTier.Unavailable);
    }

    [Fact]
    public void The_entry_day_itself_is_not_read()
    {
        var bars = Bars(20, 5);
        bars.Add(new DailyBar(Start.AddDays(20), 100, 100, 100, 100, (long)(500m * LiquidityTiers.Crore / 100m)));

        EntryTiers.Of(bars, new CostStressOptions())(Start.AddDays(20)).ShouldBe(LiquidityTier.C);
    }
}
