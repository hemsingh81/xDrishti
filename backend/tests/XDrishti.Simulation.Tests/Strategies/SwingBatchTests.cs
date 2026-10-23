using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.File;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>
/// The four swing strategies of P6-17b exist only as stored text (ADR 0081), so the oracle is the hand-calculated daily series of <see cref="SwingBatchDays"/>. Each seed is read and compiled on 1D (BUY only, a swing strategy cannot list SELL) with no
/// problem and no warning, fires once at the close of its trigger session with the stop of the text, walks to the target at 2 R at the open of the next session, gives nothing on its negative series, has every setup gate and trigger part shown
/// to matter (the series with only that part false gives nothing, and the same series without the part gives a candidate), and gives the same candidate when the future is cut off or replaced. Nothing here says a strategy has an edge.
/// </summary>
public sealed class SwingBatchTests
{
    private sealed record Expected(int Session, double Stop, double Entry, double Target);

    private static readonly Dictionary<string, Expected> Table = new()
    {
        ["TREND_PULLBACK"] = new(63, 125.25, 128.50, 135.00),
        ["NR7_ID_BO"] = new(7, 102.25, 103.90, 107.20),
        ["RSI2_REVERSION"] = new(211, 138.60, 139.80, 142.20),
        ["HIGH_52W_MOMENTUM"] = new(260, 90.80, 98.40, 113.60),
    };

    public static readonly TheoryData<string> Every = [.. Table.Keys];

    // id, the name of the series, the list (setup or trigger) and the index of the part that is false on it.
    public static readonly TheoryData<string, string, string, int> GateDays = new()
    {
        { "TREND_PULLBACK", "downtrend", "setup", 0 },
        { "TREND_PULLBACK", "low above the EMA", "trigger", 0 },
        { "TREND_PULLBACK", "close at or under the EMA", "trigger", 1 },
        { "TREND_PULLBACK", "close at or under the open", "trigger", 2 },
        { "NR7_ID_BO", "not an inside day", "setup", 0 },
        { "NR7_ID_BO", "close on the inside day's high", "trigger", 0 },
        { "RSI2_REVERSION", "below the 200 day average", "setup", 0 },
        { "RSI2_REVERSION", "RSI(2) not under 10", "trigger", 0 },
        { "HIGH_52W_MOMENTUM", "below the 252-day high", "trigger", 0 },
        { "HIGH_52W_MOMENTUM", "flat", "trigger", 1 },
    };

    // A session before the inside day that is as narrow as it: sessions 0 to 4 are the 7th to 3rd bars back (the 2nd is the mother, which the inside day implies).
    public static readonly TheoryData<int> NarrowSessions = [0, 1, 2, 3, 4];

    private static StrategyDefinition Load(string id)
    {
        var read = StrategyFile.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Seed", id + ".yaml")));
        read.HasErrors.ShouldBeFalse(string.Join("; ", read.Problems.Select(p => p.Code + " " + p.Message)));
        return read.Definition.ShouldNotBeNull();
    }

    private static List<SwingRow> Rows(string id, bool negative = false, bool walk = true) => id switch
    {
        "TREND_PULLBACK" => SwingBatchDays.TrendPullbackRows(negative, walk: walk),
        "NR7_ID_BO" => SwingBatchDays.Nr7Rows(negative, walk: walk),
        "RSI2_REVERSION" => SwingBatchDays.Rsi2Rows(negative, walk),
        _ => SwingBatchDays.High52Rows(negative, walk),
    };

    private static Bar[] Days(string id, bool negative = false, bool walk = true) => SwingBatchDays.Bars(Rows(id, negative, walk));

    private static Bar[] GateDay(string id, string name) => SwingBatchDays.Bars((id, name) switch
    {
        ("TREND_PULLBACK", "downtrend") => SwingBatchDays.TrendPullbackDowntrendRows(),
        ("TREND_PULLBACK", "low above the EMA") => SwingBatchDays.TrendPullbackRows(lowAbove: true),
        ("TREND_PULLBACK", "close at or under the EMA") => SwingBatchDays.TrendPullbackRows(negative: true),
        ("TREND_PULLBACK", _) => SwingBatchDays.TrendPullbackRows(closeUnderOpen: true),
        ("NR7_ID_BO", "not an inside day") => SwingBatchDays.Nr7Rows(notInside: true),
        ("NR7_ID_BO", _) => SwingBatchDays.Nr7Rows(negative: true),
        ("RSI2_REVERSION", "below the 200 day average") => SwingBatchDays.Rsi2BelowAverageRows(),
        ("RSI2_REVERSION", _) => SwingBatchDays.Rsi2Rows(negative: true),
        ("HIGH_52W_MOMENTUM", "below the 252-day high") => SwingBatchDays.High52Rows(negative: true),
        _ => SwingBatchDays.High52FlatRows(),
    });

    private static IReadOnlyList<EntryCandidate> Candidates(StrategyDefinition definition, IEnumerable<Bar> bars) => Scan.Run(definition, Side.Buy, bars, Timeframe.D1).Candidates;

    private static DateOnly DecisionDate(EntryCandidate candidate) => SessionClock.DateOf(candidate.DecisionEpochSeconds - 60);

    private static ConditionList? Without(ConditionList? list, int index)
    {
        var items = list!.Items.Where((_, i) => i != index).ToList();
        return items.Count == 0 ? null : new ConditionList(list.Mode, items);
    }

    private static StrategyDefinition WithoutPart(StrategyDefinition definition, string list, int index)
    {
        var buy = definition.Buy!;
        return definition with { Buy = list == "setup" ? buy with { Setup = Without(buy.Setup, index) } : buy with { Trigger = Without(buy.Trigger, index) ?? new ConditionList(ListMode.All, ["close > 0"]) } };
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void A_seed_reads_and_compiles_on_1d_buy_only_with_no_problem_and_no_warning_and_the_simulator_accepts_it(string id)
    {
        var definition = Load(id);

        definition.Id.ShouldBe(id);
        definition.Style.ShouldBe(Style.Swing);
        definition.Sides.ShouldBe([Side.Buy]);
        definition.Sell.ShouldBeNull();
        var compiled = StrategyCompiler.Compile(definition, Timeframe.D1, Side.Buy);
        compiled.IsSuccess.ShouldBeTrue(string.Join("; ", compiled.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        compiled.Problems.ShouldBeEmpty();
        Simulator.Create(definition, Timeframe.D1).Simulator.ShouldNotBeNull();
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void The_series_gives_one_candidate_at_the_close_of_the_trigger_session_with_the_stop_of_the_text(string id)
    {
        var expected = Table[id];

        var candidate = Candidates(Load(id), Days(id)).Single();

        candidate.StrategyId.ShouldBe(id);
        DecisionDate(candidate).ShouldBe(BaseDays.Date(expected.Session));
        Render.Instant(candidate.DecisionEpochSeconds)[11..].ShouldBe("15:30");
        candidate.StopLevel.ShouldBe(expected.Stop, 1e-9);
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void The_series_walks_to_the_target_at_two_r_from_the_open_of_the_next_session(string id)
    {
        var expected = Table[id];

        var result = Sim.Run(Sim.Create(Load(id), Timeframe.D1, Cand.NoSlippage), Days(id), calendar: Rep.DefaultCalendar);

        var trade = result.Trades.Single();
        result.OpenAtEnd.ShouldBeEmpty();
        trade.Side.ShouldBe(Side.Buy);
        trade.TradeDate.ShouldBe(BaseDays.Date(expected.Session + 1));
        trade.ExitReason.ShouldBe(ExitReason.Target);
        trade.EntryPrice.ShouldBe(expected.Entry, 1e-9);
        trade.StopPrice.ShouldBe(expected.Stop, 1e-9);
        trade.TargetPrice.GetValueOrDefault().ShouldBe(expected.Target, 1e-6);
        trade.ExitPrice.ShouldBe(expected.Target, 1e-6);
        trade.RExact.ShouldBe(2.0, 1e-6);
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void The_negative_series_gives_no_candidate(string id) => Candidates(Load(id), Days(id, negative: true, walk: false)).ShouldBeEmpty();

    [Theory]
    [MemberData(nameof(GateDays))]
    public void A_series_with_the_trigger_and_one_false_part_gives_nothing_and_the_same_series_without_that_part_gives_a_candidate(string id, string name, string list, int index)
    {
        var definition = Load(id);
        var bars = GateDay(id, name);

        Candidates(definition, bars).ShouldBeEmpty();
        Candidates(WithoutPart(definition, list, index), bars).ShouldNotBeEmpty($"{id}: {name}");
    }

    [Theory]
    [MemberData(nameof(NarrowSessions))]
    public void An_inside_day_that_is_not_the_narrowest_of_seven_gives_nothing_for_each_of_the_five_older_ranges_and_the_range_gate_alone_blocks_it(int narrow)
    {
        var definition = Load("NR7_ID_BO");
        var bars = SwingBatchDays.Bars(SwingBatchDays.Nr7Rows(narrowSession: narrow));

        Candidates(definition, bars).ShouldBeEmpty();
        Candidates(WithoutPart(definition, "setup", 1), bars).ShouldNotBeEmpty($"session {narrow} is narrower than the inside day");
    }

    [Theory]
    [MemberData(nameof(NarrowSessions))]
    public void An_older_range_equal_to_the_inside_days_is_not_strictly_narrower_and_blocks_the_entry(int tied)
    {
        var definition = Load("NR7_ID_BO");
        var bars = SwingBatchDays.Bars(SwingBatchDays.Nr7Rows(narrowSession: tied, tie: true));

        Candidates(definition, bars).ShouldBeEmpty();
        Candidates(WithoutPart(definition, "setup", 1), bars).ShouldNotBeEmpty($"session {tied} has the same range, 0.75, as the inside day");
    }

    [Fact]
    public void A_position_that_reaches_neither_stop_nor_target_leaves_at_the_close_of_its_fifth_session()
    {
        var result = Sim.Run(Sim.Create(Load("NR7_ID_BO"), Timeframe.D1, Cand.NoSlippage), SwingBatchDays.Bars(SwingBatchDays.Nr7HoldRows()), calendar: Rep.DefaultCalendar);

        var trade = result.Trades.Single();
        trade.ExitReason.ShouldBe(ExitReason.MaxHold);
        trade.TradeDate.ShouldBe(BaseDays.Date(8));
        trade.SessionsHeld.ShouldBe(5);
        trade.ExitPrice.ShouldBe(104.00, 1e-9);
    }

    [Theory]
    [InlineData("TREND_PULLBACK", 20, "44 sessions, the SMA(50) is not ready")]
    [InlineData("NR7_ID_BO", 1, "7 sessions, the range of 7 days back does not exist")]
    [InlineData("RSI2_REVERSION", 20, "191 sessions, the SMA(200) is not ready")]
    [InlineData("HIGH_52W_MOMENTUM", 20, "241 sessions, the 252-session high is not ready")]
    public void A_series_that_is_too_short_for_the_warm_up_gives_no_candidate_though_the_trigger_session_is_the_same(string id, int skipped, string why)
    {
        var definition = Load(id);

        Candidates(definition, Days(id)).ShouldNotBeEmpty();
        Candidates(definition, SwingBatchDays.Bars([.. Rows(id).Skip(skipped)])).ShouldBeEmpty(why);
    }

    [Fact]
    public void The_trend_pullback_averages_are_the_ramp_arithmetic_and_the_dip_reaches_the_average()
    {
        var closes = SwingBatchDays.TrendPullbackRows().Select(r => r.C).ToArray();

        Ema(closes[..60], 20).ShouldBe(124.75, 1e-9, "on a perfect ramp the EMA(20) is the close less 9.5 steps of 0.5");
        Sma(closes[..60], 50).ShouldBe(117.25, 1e-9, "and the SMA(50) is the close less 24.5 steps");
        var ema = Ema(closes[..64], 20);
        ema.ShouldBe(125.854, 0.001);
        (125.5 <= ema).ShouldBeTrue("the low of the trigger session is at or under the EMA(20)");
        (closes[63] > ema).ShouldBeTrue();
        (ema > Sma(closes[..64], 50)).ShouldBeTrue();
        closes[60..63].Zip(SwingBatchDays.TrendPullbackRows().Skip(60).Take(3), (c, r) => c < r.O).ShouldAllBe(down => down, "sessions 60 to 62 close under their open, so none is a trigger even where the low reaches the average");
    }

    [Fact]
    public void The_rsi2_series_has_the_wilder_values_the_hand_calculation_says()
    {
        var closes = SwingBatchDays.Rsi2Rows().Select(r => r.C).ToArray();

        Rsi2(closes[..211]).ShouldBe(100 - (100 / 1.2), 1e-9, "gain 0.1, loss 0.5: RSI 16.67, not under 10");
        Rsi2(closes[..212]).ShouldBe(6.25, 1e-9, "gain 0.05, loss 0.75: RS 1/15, RSI 6.25");
        Sma(closes[..212], 200).ShouldBe(122.282, 0.001);
        (closes[211] > Sma(closes[..212], 200)).ShouldBeTrue();
    }

    [Fact]
    public void The_52_week_series_has_no_tie_and_the_trigger_close_is_above_every_close_of_its_window()
    {
        var closes = SwingBatchDays.High52Rows().Select(r => r.C).ToArray();

        closes[..260].Skip(8).Max().ShouldBe(98.30, 1e-9, "the highest close of the 252 sessions before the trigger is session 9");
        closes[260].ShouldBe(98.40, 1e-9);
        closes[260].ShouldBeGreaterThan(closes[259]);
        var odd = closes[..260].Where((_, k) => k % 2 == 1).ToArray();
        odd.Zip(odd.Skip(1), (a, b) => a - b).ShouldAllBe(d => d > 0.04, "the up sessions of the base (odd) fall steadily, so none ties or tops an earlier one and no earlier session fires");
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void The_candidate_is_the_same_when_the_future_is_cut_off_or_replaced(string id)
    {
        var definition = Load(id);
        var bars = Days(id);
        var full = Candidates(definition, bars)[0];
        var at = full.DecisionBarIndex;
        var wild = bars.Select((b, i) => i <= at ? b : new Bar(b.EpochSeconds, b.Open * 0.5, b.High * 0.5, b.Low * 0.5, b.Close * 0.5, b.Volume)).ToArray();

        var cut = Candidates(definition, bars[..(at + 1)]);
        var replaced = Candidates(definition, wild);

        cut.Count.ShouldBe(1);
        Render.Candidate(cut[0]).ShouldBe(Render.Candidate(full));
        Render.Candidate(replaced[0]).ShouldBe(Render.Candidate(full));
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void A_seed_declares_the_reward_risk_no_more_than_four_parameters_and_a_hold_of_five_sessions(string id)
    {
        var definition = Load(id);

        definition.Params.Select(p => p.Name).ShouldContain("rr");
        definition.Params.Count.ShouldBeLessThanOrEqualTo(StrategyDefinition.MaxParams);
        definition.TimeRules!.MaxHoldSessions.ShouldBe(5);
    }

    private static double Sma(double[] values, int n) => values[^n..].Average();

    /// <summary>The EMA seeded with the mean of the first n values.</summary>
    private static double Ema(double[] values, int n)
    {
        var ema = values.Take(n).Average();
        var alpha = 2.0 / (n + 1);
        foreach (var v in values.Skip(n))
        {
            ema += alpha * (v - ema);
        }

        return ema;
    }

    /// <summary>Wilder RSI(2): the average gain and loss seeded with the mean of the first two changes, then (avg + x) / 2.</summary>
    private static double Rsi2(double[] values)
    {
        var changes = values.Zip(values.Skip(1), (a, b) => b - a).ToArray();
        var gain = changes.Take(2).Average(c => Math.Max(c, 0));
        var loss = changes.Take(2).Average(c => Math.Max(-c, 0));
        foreach (var c in changes.Skip(2))
        {
            gain = (gain + Math.Max(c, 0)) / 2;
            loss = (loss + Math.Max(-c, 0)) / 2;
        }

        return loss == 0 ? 100 : 100 - (100 / (1 + (gain / loss)));
    }
}
