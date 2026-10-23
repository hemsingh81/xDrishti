using System.Text.Json;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.File;

namespace XDrishti.Simulation.Tests.Ticketing;

public sealed class TicketBuilderTests
{
    private static readonly DateOnly Start = new(2026, 1, 5);

    private static DateOnly Day(int index)
    {
        var d = Start;
        for (var n = 0; n < index; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                n++;
            }
        }

        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            d = d.AddDays(1);
        }

        return d;
    }

    private static long Epoch(DateOnly date, int minuteOfDay) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5)).AddMinutes(minuteOfDay).ToUnixTimeSeconds();

    /// <summary>A quiet uptrend: every minute of a day sits at that day's price (0.1 % up a day); the last day may have a different volume or jump.</summary>
    private static List<Bar> Days(int count, long lastVolume = 3000, double lastJump = 0)
    {
        var bars = new List<Bar>();
        for (var i = 0; i < count; i++)
        {
            var date = Day(i);
            var price = 100 * (1 + (0.001 * i)) + (i == count - 1 ? lastJump : 0);
            var volume = i == count - 1 ? lastVolume : 1000;
            for (var m = 0; m < SessionClock.RegularBars; m++)
            {
                bars.Add(new Bar(Epoch(date, SessionClock.OpenMinute + m), price, price + 0.05, price - 0.05, price, volume));
            }
        }

        return bars;
    }

    private static CompiledSide Compile(XDrishti.Strategies.Model.StrategyDefinition definition, Timeframe tf, Side side) =>
        StrategyCompiler.Compile(definition, tf, side).Compiled ?? throw new InvalidOperationException("does not compile");

    private static TicketDraft BaseBreakout(List<Bar> bars, int sessions) =>
        TicketBuilder.Build(Compile(BaseBreakoutV1.Create(), Timeframe.D1, Side.Buy), "AAA", "h", Day(sessions - 1), Day(sessions), bars, null);

    [Fact]
    public void A_daily_strategy_whose_setup_and_trigger_hold_is_known_true_with_levels()
    {
        var draft = BaseBreakout(Days(100), 100);

        Assert.Equal(TicketVerdict.Conditional, draft.Verdict);
        Assert.Equal(SetupTruth.True, draft.Overall);
        Assert.All(draft.Parts, p => Assert.Equal(SetupTruth.True, p.Truth));
        Assert.NotNull(draft.StopLevel);
        Assert.True(draft.StopLevel < draft.IndicativeEntry);
        Assert.Equal(2.0, draft.RewardRisk);
        Assert.Contains("nothing left to watch", draft.TriggerText, StringComparison.Ordinal);
        Assert.Contains("valid for", draft.ValidityText, StringComparison.Ordinal);
        Assert.Equal("BUY", draft.Side);
        Assert.NotNull(draft.AverageDailyTurnover);
    }

    [Fact]
    public void A_part_false_at_the_close_rejects_and_unknown_is_not_true()
    {
        var draft = BaseBreakout(Days(100, lastVolume: 1000), 100);

        Assert.Equal(TicketVerdict.Rejected, draft.Verdict);
        Assert.Equal(SetupTruth.False, draft.Overall);
        Assert.Contains(draft.Parts, p => p.Text.Contains("rel_volume", StringComparison.Ordinal) && p.Truth == SetupTruth.False);
    }

    [Fact]
    public void Too_little_history_is_unavailable_never_a_guess()
    {
        var draft = BaseBreakout(Days(15), 15);

        Assert.Equal(TicketVerdict.Unavailable, draft.Verdict);
        Assert.Contains("not ready", draft.Reason, StringComparison.Ordinal);
        Assert.NotEqual(SetupTruth.True, draft.Overall);
    }

    [Fact]
    public void An_incomplete_last_session_is_unavailable()
    {
        var bars = Days(100);
        bars.RemoveAt(bars.Count - 1);

        Assert.Equal(TicketVerdict.Unavailable, BaseBreakout(bars, 100).Verdict);
    }

    [Fact]
    public void An_intraday_trigger_is_unknown_and_makes_a_conditional_ticket()
    {
        var side = Compile(OrbV1.Create(), Timeframe.M15, Side.Buy);
        var draft = TicketBuilder.Build(side, "AAA", "h", Day(99), Day(100), Days(100), null);

        Assert.Equal(TicketVerdict.Conditional, draft.Verdict);
        Assert.Equal(SetupTruth.True, draft.Setup);
        Assert.Equal(SetupTruth.Unknown, draft.Trigger);
        Assert.Equal(SetupTruth.Unknown, draft.Overall);
        Assert.Contains("or_high", draft.TriggerText, StringComparison.Ordinal);
        Assert.Null(draft.StopLevel);
        Assert.Contains("entries 09:30 to 11:30", draft.ValidityText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Bars_after_the_close_of_D_cannot_change_a_ticket(int extraDays)
    {
        var baseBars = Days(100);
        var withFuture = new List<Bar>(Days(100 + extraDays, lastVolume: 1, lastJump: 50));
        // the same first 100 sessions, then different later sessions
        var before = JsonSerializer.Serialize(BaseBreakout(baseBars, 100));
        var future = withFuture.Where(b => b.EpochSeconds >= Epoch(Day(100), 0)).ToList();
        var mixed = baseBars.Concat(future).Reverse().ToList();

        Assert.Equal(before, JsonSerializer.Serialize(BaseBreakout(mixed, 100)));
        var orb = Compile(OrbV1.Create(), Timeframe.M15, Side.Buy);
        Assert.Equal(
            JsonSerializer.Serialize(TicketBuilder.Build(orb, "AAA", "h", Day(99), Day(100), baseBars, null)),
            JsonSerializer.Serialize(TicketBuilder.Build(orb, "AAA", "h", Day(99), Day(100), mixed, null)));
    }

    [Fact]
    public void The_same_inputs_give_the_same_ticket()
    {
        var bars = Days(100);

        Assert.Equal(JsonSerializer.Serialize(BaseBreakout(bars, 100)), JsonSerializer.Serialize(BaseBreakout(bars, 100)));
    }

    [Fact]
    public void A_stored_strategy_text_is_classified_and_a_wrong_hash_is_unavailable()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Seed", "BASE_BREAKOUT.yaml"));
        var hash = CanonicalText.Sha256Hex(CanonicalStrategy.Json(StrategyFile.Read(text).Definition!));
        var bars = Days(100);

        var ok = TicketBuilder.Classify(text, hash, Timeframe.D1, Side.Buy, "AAA", Day(99), Day(100), bars, null);
        var bad = TicketBuilder.Classify(text, new string('0', 64), Timeframe.D1, Side.Buy, "AAA", Day(99), Day(100), bars, null);

        Assert.Equal(TicketVerdict.Conditional, ok.Verdict);
        Assert.Equal("BASE_BREAKOUT", ok.StrategyId);
        Assert.Equal(TicketVerdict.Unavailable, bad.Verdict);
    }
}
