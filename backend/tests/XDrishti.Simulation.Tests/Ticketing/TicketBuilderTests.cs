using System.Diagnostics;
using System.Text.Json;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.File;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Ticketing;

public sealed class TicketBuilderTests
{
    private static readonly DateOnly Start = new(2026, 1, 5);

    private static readonly TradingCalendar Calendar = new();

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

    /// <summary>A quiet uptrend: every minute of a day sits at that day's price (0.1 % up a day); the last day may have a different volume, jump or length.</summary>
    private static List<Bar> Days(int count, long lastVolume = 3000, double lastJump = 0, int lastDayMinutes = SessionClock.RegularBars)
    {
        var bars = new List<Bar>();
        for (var i = 0; i < count; i++)
        {
            var date = Day(i);
            var last = i == count - 1;
            var price = 100 * (1 + (0.001 * i)) + (last ? lastJump : 0);
            var volume = last ? lastVolume : 1000;
            for (var m = 0; m < (last ? lastDayMinutes : SessionClock.RegularBars); m++)
            {
                bars.Add(new Bar(Epoch(date, SessionClock.OpenMinute + m), price, price + 0.05, price - 0.05, price, volume));
            }
        }

        return bars;
    }

    private static CompiledSide Compile(StrategyDefinition definition, Timeframe tf, Side side) =>
        StrategyCompiler.Compile(definition, tf, side).Compiled ?? throw new InvalidOperationException("does not compile: " + string.Join("; ", StrategyCompiler.Compile(definition, tf, side).Problems.Select(p => p.Path + " " + p.Problem.Message)));

    private static StrategyDefinition Swing(string[] setup, string[] trigger, string[]? invalidate = null, ListMode setupMode = ListMode.All, ListMode triggerMode = ListMode.All, double[]? rr = null) => new(
        StrategyDefinition.CurrentSchema,
        "TEST_SWING",
        Style.Swing,
        [Timeframe.D1],
        [Side.Buy],
        [],
        new SideRules(
            new ConditionList(setupMode, setup),
            new ConditionList(triggerMode, trigger),
            invalidate is null ? null : new ConditionList(ListMode.All, invalidate),
            new EntrySpec(EntryKind.AtClose),
            new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(5)),
            [new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [.. (rr ?? [2.0]).Select(Quantity.Of)])])],
            null),
        null,
        new TimeRules(null, null, null, 5),
        []);

    private static TicketDraft Run(StrategyDefinition definition, int sessions = 100, TradingCalendar? calendar = null, string exit = "FIXED_RR", List<Bar>? bars = null, Timeframe tf = Timeframe.D1) =>
        TicketBuilder.Build(Compile(definition, tf, Side.Buy), "AAA", "h", definition.Style == Style.Swing ? "swing" : "intraday", exit, Day(sessions - 1), Day(sessions), bars ?? Days(sessions), calendar ?? Calendar);

    private const string Up = "close > sma(20)";
    private const string Down = "close < sma(20)";
    private const string NotReadyA = "sma(200) > 0";
    private const string NotReadyB = "sma(150) > 0";

    private static TicketDraft BaseBreakout(List<Bar> bars, int sessions) =>
        TicketBuilder.Build(Compile(BaseBreakoutV1.Create(), Timeframe.D1, Side.Buy), "AAA", "h", "swing", "FIXED_RR", Day(sessions - 1), Day(sessions), bars, Calendar);

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
        Assert.Contains("indicative level", draft.StopText, StringComparison.Ordinal);
        Assert.Equal("BUY", draft.Side);
        Assert.NotNull(draft.AverageDailyTurnover);
    }

    [Fact]
    public void A_swing_low_stop_with_a_fixed_rr_exit_carries_a_level_anchor_and_a_target_that_matches_the_text()
    {
        var draft = BaseBreakout(Days(100), 100);

        Assert.Equal(StopAnchor.Level, draft.StopAnchor);
        var entry = draft.IndicativeEntry!.Value;
        Assert.Equal(entry + (2 * (entry - draft.StopLevel!.Value)), draft.TargetLevel!.Value, 9);
        var number = draft.TargetLevel.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("indicative level " + number, draft.TargetText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_percentage_stop_is_anchored_to_the_close_and_no_fixed_exit_gives_no_target_level()
    {
        var pct = Swing(["close > 0"], ["close > 0"]) with { };
        var rules = pct.Buy! with { Stop = new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(5)) };
        var definition = pct with { Buy = rules };

        var fixedRr = Run(definition);
        var rule = Run(definition, exit: "NOT_AN_EXIT");

        Assert.Equal(StopAnchor.CloseOfD, fixedRr.StopAnchor);
        Assert.Equal(fixedRr.IndicativeEntry!.Value * 0.95, fixedRr.StopLevel!.Value, 9);
        Assert.Null(rule.TargetLevel);
        Assert.Null(rule.RewardRisk);
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
        var draft = TicketBuilder.Build(side, "AAA", "h", "intraday", "FIXED_RR", Day(99), Day(100), Days(100), Calendar);

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
        var withFuture = Days(100 + extraDays, lastVolume: 1, lastJump: 50);
        var before = JsonSerializer.Serialize(BaseBreakout(baseBars, 100));
        var future = withFuture.Where(b => b.EpochSeconds >= Epoch(Day(100), 0)).ToList();
        var mixed = baseBars.Concat(future).Reverse().ToList();

        Assert.Equal(before, JsonSerializer.Serialize(BaseBreakout(mixed, 100)));
        var orb = Compile(OrbV1.Create(), Timeframe.M15, Side.Buy);
        Assert.Equal(
            JsonSerializer.Serialize(TicketBuilder.Build(orb, "AAA", "h", "intraday", "FIXED_RR", Day(99), Day(100), baseBars, Calendar)),
            JsonSerializer.Serialize(TicketBuilder.Build(orb, "AAA", "h", "intraday", "FIXED_RR", Day(99), Day(100), mixed, Calendar)));
    }

    [Fact]
    public void Adding_changing_or_removing_bars_after_the_close_of_D_leaves_the_levels_unchanged()
    {
        var baseBars = Days(100);
        var baseline = BaseBreakout(baseBars, 100);
        var added = baseBars.Concat(Days(103, lastVolume: 1, lastJump: 50).Where(b => b.EpochSeconds >= Epoch(Day(100), 0))).ToList();
        var changed = baseBars.Concat(Days(101, lastJump: -40).Where(b => b.EpochSeconds >= Epoch(Day(100), 0))).ToList();

        foreach (var bars in new[] { baseBars, added, changed })
        {
            var d = BaseBreakout(bars, 100);
            Assert.Equal(baseline.StopLevel, d.StopLevel);
            Assert.Equal(baseline.TargetLevel, d.TargetLevel);
            Assert.Equal(baseline.StopAnchor, d.StopAnchor);
            Assert.Equal(baseline.IndicativeEntry, d.IndicativeEntry);
            Assert.Equal(baseline.RewardRisk, d.RewardRisk);
        }

        Assert.NotNull(baseline.TargetLevel);
    }

    [Fact]
    public void An_opening_range_stop_has_no_anchor_and_no_target_level()
    {
        var draft = Run(Swing([Up], ["tf(\"15m\").close > 0"]).WithIntraday(), tf: Timeframe.M15);

        Assert.Null(draft.StopAnchor);
        Assert.Null(draft.TargetLevel);
        Assert.NotNull(draft.IndicativeEntry);
    }

    [Fact]
    public void An_atr_stop_whose_atr_is_not_ready_has_no_level_anchor_or_target()
    {
        var definition = Swing([Up], [Up]);
        definition = definition with { Buy = definition.Buy! with { Stop = new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2), AtrPeriod: Quantity.Of(200)) } };

        var draft = Run(definition);

        Assert.Null(draft.StopLevel);
        Assert.Null(draft.StopAnchor);
        Assert.Null(draft.TargetLevel);
        Assert.Contains("level set on tomorrow's data", draft.StopText, StringComparison.Ordinal);
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

        var ok = TicketBuilder.Classify(text, hash, Timeframe.D1, Side.Buy, "swing", "FIXED_RR", "AAA", Day(99), Day(100), bars, Calendar);
        var bad = TicketBuilder.Classify(text, new string('0', 64), Timeframe.D1, Side.Buy, "swing", "FIXED_RR", "AAA", Day(99), Day(100), bars, Calendar);

        Assert.Equal(TicketVerdict.Conditional, ok.Verdict);
        Assert.Equal("BASE_BREAKOUT", ok.StrategyId);
        Assert.Equal(TicketVerdict.Unavailable, bad.Verdict);
    }

    // --- not-ready parts: every assignment is tried, polarity matters ---

    [Fact]
    public void A_not_ready_part_in_the_setup_and_another_in_the_invalidate_list_is_unavailable()
    {
        // A true and B false gives a ticket; A false gives none: the outcome depends on the missing data.
        var draft = Run(Swing([NotReadyA], [Up], [NotReadyB]));

        Assert.Equal(TicketVerdict.Unavailable, draft.Verdict);
        Assert.Contains("not ready", draft.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_not_ready_part_cannot_save_a_ticket_that_a_known_false_part_rejects()
    {
        var draft = Run(Swing([NotReadyA], [Down]));

        Assert.Equal(TicketVerdict.Rejected, draft.Verdict);
    }

    [Fact]
    public void The_same_condition_text_in_the_setup_and_the_invalidate_list_is_one_part()
    {
        // setup A and not A is never true, whatever A is.
        var draft = Run(Swing([NotReadyA], [Up], [NotReadyA]));

        Assert.Equal(TicketVerdict.Rejected, draft.Verdict);
    }

    [Fact]
    public void A_not_ready_part_in_an_any_list_beside_a_false_one_is_unavailable_and_beside_a_true_one_is_a_ticket()
    {
        Assert.Equal(TicketVerdict.Unavailable, Run(Swing([Down, NotReadyA], [Up], setupMode: ListMode.Any)).Verdict);
        Assert.Equal(TicketVerdict.Conditional, Run(Swing([Up, NotReadyA], [Up], setupMode: ListMode.Any)).Verdict);
        Assert.Equal(TicketVerdict.Rejected, Run(Swing([Down, "close < sma(10)"], [Up], setupMode: ListMode.Any)).Verdict);
    }

    [Fact]
    public void An_invalidate_list_that_holds_rejects_and_one_that_does_not_leaves_a_ticket()
    {
        Assert.Equal(TicketVerdict.Rejected, Run(Swing([Up], [Up], [Up])).Verdict);
        var ok = Run(Swing([Up], [Up], [Down]));

        Assert.Equal(TicketVerdict.Conditional, ok.Verdict);
        Assert.Equal(SetupTruth.False, ok.Invalidate);
        Assert.Equal(SetupTruth.True, ok.Overall);
    }

    [Fact]
    public void More_not_ready_parts_than_the_cap_is_unavailable()
    {
        var many = Enumerable.Range(0, TicketBuilder.MaxNotReadyParts + 1).Select(i => "sma(" + (150 + i) + ") > 0").ToArray();

        Assert.Equal(TicketVerdict.Unavailable, Run(Swing(many, [Up])).Verdict);
    }

    // --- parts that are not known at the close ---

    [Fact]
    public void The_clock_an_intraday_feature_and_a_weekday_are_unknown_not_true()
    {
        var draft = Run(Swing([Up], ["time >= \"09:45\" and tf(\"15m\").close > 0"]).WithIntraday(), tf: Timeframe.M15);
        var weekday = Run(Swing([Up, "day_of_week == 1"], [Up]));

        Assert.Equal(TicketVerdict.Conditional, draft.Verdict);
        Assert.Equal(SetupTruth.Unknown, draft.Trigger);
        Assert.False(draft.Parts[^1].KnownAtClose);
        Assert.Equal(SetupTruth.Unknown, weekday.Setup);
        Assert.Contains(weekday.Parts, p => p.Text == "day_of_week == 1" && !p.KnownAtClose && p.Truth == SetupTruth.Unknown);
    }

    [Theory]
    [InlineData("vix > 20")]
    [InlineData("is_expiry_day == 1")]
    public void A_market_context_or_expiry_day_part_cannot_be_evaluated_and_is_unavailable(string condition)
    {
        Assert.Equal(TicketVerdict.Unavailable, Run(Swing([Up, condition], [Up])).Verdict);
    }

    [Fact]
    public void Weekly_and_monthly_parts_need_the_calendar()
    {
        var weekly = Swing(["tf(\"1W\").close > tf(\"1W\").sma(5)", "tf(\"1M\").close > tf(\"1M\").sma(2)"], [Up]);

        var with = Run(weekly, calendar: Calendar);
        var without = TicketBuilder.Build(Compile(weekly, Timeframe.D1, Side.Buy), "AAA", "h", "swing", "FIXED_RR", Day(99), Day(100), Days(100), null);

        Assert.Equal(TicketVerdict.Conditional, with.Verdict);
        Assert.All(with.Parts, p => Assert.Equal(SetupTruth.True, p.Truth));
        Assert.Equal(TicketVerdict.Unavailable, without.Verdict);
    }

    [Fact]
    public void A_short_session_closes_at_the_calendars_end_and_later_bars_are_dropped()
    {
        var shortDay = Day(99);
        var calendar = new TradingCalendar([TradingDay.Special(shortDay, SessionClock.OpenMinute, 13 * 60, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);
        var bars = Days(100, lastDayMinutes: 13 * 60 - SessionClock.OpenMinute);
        var extra = new Bar(Epoch(shortDay, 13 * 60), 500, 500, 500, 500, 1);

        var with = Run(Swing([Up], [Up]), calendar: calendar, bars: bars);
        var withLater = Run(Swing([Up], [Up]), calendar: calendar, bars: [.. bars, extra]);
        var without = Run(Swing([Up], [Up]), bars: bars);

        Assert.Equal(TicketVerdict.Conditional, with.Verdict);
        Assert.Equal(JsonSerializer.Serialize(with), JsonSerializer.Serialize(withLater));
        Assert.Equal(TicketVerdict.Unavailable, without.Verdict);
    }

    // --- the cell's exit and style ---

    [Fact]
    public void Two_cells_of_one_strategy_with_different_exits_give_different_targets()
    {
        var definition = Swing([Up], [Up], rr: [2.0, 3.0]);

        var two = Run(definition, exit: "FIXED_RR{rr=2}");
        var three = Run(definition, exit: "FIXED_RR{rr=3}");

        Assert.Equal(2.0, two.RewardRisk);
        Assert.Equal(3.0, three.RewardRisk);
        Assert.NotEqual(two.TargetText, three.TargetText);
    }

    [Theory]
    [InlineData("FIXED_RR")]
    [InlineData("ATR_TRAIL")]
    [InlineData("FIXED_RR{rr=5}")]
    public void An_exit_that_matches_no_single_candidate_has_no_target_never_the_first(string exit)
    {
        var draft = Run(Swing([Up], [Up], rr: [2.0, 3.0]), exit: exit);

        Assert.Equal(TicketVerdict.Conditional, draft.Verdict);
        Assert.Null(draft.RewardRisk);
        Assert.Contains("target", draft.Unavailable);
    }

    [Fact]
    public void A_cell_style_that_is_not_the_strategys_is_unavailable()
    {
        var draft = TicketBuilder.Build(Compile(Swing([Up], [Up]), Timeframe.D1, Side.Buy), "AAA", "h", "intraday", "FIXED_RR", Day(99), Day(100), Days(100), Calendar);

        Assert.Equal(TicketVerdict.Unavailable, draft.Verdict);
    }

    // --- the daily path gives what the minute path gives ---

    [Fact]
    public void A_daily_only_side_stepped_with_daily_bars_gives_what_every_minute_gives()
    {
        var daily = Swing([Up], [Up]);
        var minute = Swing([Up], [Up], ["tf(\"15m\").close < 0"]);
        var dailySide = PreparedTicketStrategy.FromSide(Compile(daily, Timeframe.D1, Side.Buy), "h", "swing", "FIXED_RR");
        var minuteSide = PreparedTicketStrategy.FromSide(Compile(minute, Timeframe.D1, Side.Buy), "h", "swing", "FIXED_RR");
        var bars = TicketBars.Prepare(Days(100), Day(99), Calendar);

        var a = TicketBuilder.Build(dailySide, bars, "AAA", Day(99), Day(100), Calendar);
        var b = TicketBuilder.Build(minuteSide, bars, "AAA", Day(99), Day(100), Calendar);

        Assert.True(dailySide.DailyOnly);
        Assert.False(minuteSide.DailyOnly);
        Assert.Equal(a.StopLevel, b.StopLevel);
        Assert.Equal(a.IndicativeEntry, b.IndicativeEntry);
        Assert.Equal(a.Parts.Select(p => p.Truth), b.Parts.Take(2).Select(p => p.Truth));
    }

    // --- timing note, not a gate ---

    [Fact]
    public void Timing_of_214_instruments_by_several_cells_on_fabricated_bars()
    {
        var raw = Days(120);
        var session = Day(119);
        var cells = new[]
        {
            PreparedTicketStrategy.FromSide(Compile(BaseBreakoutV1.Create(), Timeframe.D1, Side.Buy), "h", "swing", "FIXED_RR"),
            PreparedTicketStrategy.FromSide(Compile(Swing([Up], [Up], rr: [2.0, 3.0]), Timeframe.D1, Side.Buy), "h", "swing", "FIXED_RR{rr=3}"),
            PreparedTicketStrategy.FromSide(Compile(OrbV1.Create(), Timeframe.M15, Side.Buy), "h", "intraday", "FIXED_RR"),
            PreparedTicketStrategy.FromSide(Compile(OrbV1.Create(), Timeframe.M15, Side.Sell), "h", "intraday", "FIXED_RR"),
        };
        var watch = Stopwatch.StartNew();
        var built = 0;
        for (var i = 0; i < 214; i++)
        {
            var bars = TicketBars.Prepare(raw, session, Calendar);
            foreach (var cell in cells)
            {
                Assert.NotEqual(TicketVerdict.Unavailable, TicketBuilder.Build(cell, bars, "S" + i, session, Day(120), Calendar).Verdict);
                built++;
            }
        }

        watch.Stop();
        TestContext.Current.SendDiagnosticMessage($"P7-04 timing: {built} drafts (214 instruments x {cells.Length} cells, 120 sessions of minute bars) in {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal(214 * cells.Length, built);
    }
}

internal static class DefinitionExtensions
{
    /// <summary>The same rules as an intraday strategy on 15m with an entry window.</summary>
    public static StrategyDefinition WithIntraday(this StrategyDefinition d) => d with
    {
        Style = Style.Intraday,
        Timeframes = [Timeframe.M15],
        TimeRules = new TimeRules(new TimeOnly(9, 30), new TimeOnly(11, 30), new TimeOnly(15, 15), null),
        Buy = d.Buy! with { Stop = new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(15)) },
    };
}
