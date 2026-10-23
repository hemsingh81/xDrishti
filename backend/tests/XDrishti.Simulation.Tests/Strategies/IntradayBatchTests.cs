using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.File;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>
/// The eight intraday strategies of P6-17a exist only as stored text (ADR 0079), so there is no C# builder to compare with: the oracle is the hand-calculated day. Each seed file is read and compiled with no problem and no warning on
/// both sides, fires on a fabricated day at the decision instant, with the stop and the exit written in <see cref="IntradayBatchDays"/>, does not fire on its negative day, fires as a SELL on the mirrored day (and the opposite side stays
/// empty on both), has every setup gate shown to matter (a day with the trigger and one false gate gives nothing, and the same day with that gate deleted gives a candidate), and gives the same candidate when the future is cut off or replaced.
/// Nothing here says a strategy has an edge: it says the mechanics are what the text says.
/// </summary>
public sealed class IntradayBatchTests
{
    private sealed record Expected(string Decision, double Stop, double Entry, double Target, double R = 2.0);

    private static readonly Dictionary<string, Expected> Table = new()
    {
        ["PDH_PDL_BREAK"] = new("10:15", 110.25, 111.25, 113.25),
        ["PDH_PDL_REJECT"] = new("10:00", 109.75, 110.40, 111.70),
        ["VWAP_RECLAIM"] = new("10:00", 109.25, 110.00, 111.50),
        ["EMA_PULLBACK"] = new("10:00", 110.20, 110.70, 111.70),
        ["GAP_GO"] = new("10:00", 111.00, 112.10, 114.30),
        ["GAP_FILL"] = new("10:00", 109.25, 110.00, 111.50),
        ["INSIDE_BAR_BO"] = new("10:00", 110.25, 111.40, 113.70),
        ["MEAN_REV_BANDS"] = new("10:45", 108.80, 109.60, 110.80, 1.5),
    };

    public static readonly TheoryData<string> Every = [.. Table.Keys];

    // id, the name of the day, the index of the false setup gate (the same index on both sides).
    public static readonly TheoryData<string, string, int> SetupDays = new()
    {
        { "VWAP_RECLAIM", "falling daily prefix", 0 },
        { "EMA_PULLBACK", "EMA(9) below EMA(21)", 0 },
        { "EMA_PULLBACK", "hourly close below hourly EMA(21)", 1 },
        { "GAP_GO", "gap too small", 0 },
        { "GAP_FILL", "gap too large", 0 },
        { "MEAN_REV_BANDS", "strong trend", 0 },
    };

    private static StrategyDefinition Load(string id)
    {
        var read = StrategyFile.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Seed", id + ".yaml")));
        read.HasErrors.ShouldBeFalse(string.Join("; ", read.Problems.Select(p => p.Code + " " + p.Message)));
        return read.Definition.ShouldNotBeNull();
    }

    private static Bar[] Days(string id, bool negative = false) => id switch
    {
        "PDH_PDL_BREAK" => IntradayBatchDays.PdhBreak(negative),
        "PDH_PDL_REJECT" => IntradayBatchDays.PdlReject(negative),
        "VWAP_RECLAIM" => IntradayBatchDays.VwapReclaim(negative),
        "EMA_PULLBACK" => IntradayBatchDays.EmaPullback(negative),
        "GAP_GO" => IntradayBatchDays.GapGo(negative),
        "GAP_FILL" => IntradayBatchDays.GapFill(negative),
        "INSIDE_BAR_BO" => IntradayBatchDays.InsideBar(negative),
        _ => IntradayBatchDays.MeanRev(negative),
    };

    private static Bar[] SetupDay(string id, string name) => (id, name) switch
    {
        ("VWAP_RECLAIM", _) => IntradayBatchDays.VwapReclaimFallingDaily(),
        ("EMA_PULLBACK", "EMA(9) below EMA(21)") => IntradayBatchDays.EmaPullbackEmaGate(),
        ("EMA_PULLBACK", _) => IntradayBatchDays.EmaPullbackHourlyGate(),
        ("GAP_GO", _) => IntradayBatchDays.GapGo(negative: true),
        ("GAP_FILL", _) => IntradayBatchDays.GapFill(negative: true),
        _ => IntradayBatchDays.MeanRevTrending(),
    };

    private static string Clock(long epoch) => Render.Instant(epoch)[11..];

    private static IReadOnlyList<EntryCandidate> Candidates(StrategyDefinition definition, Side side, IEnumerable<Bar> bars) => Scan.Run(definition, side, bars).Candidates;

    private static Bar[] Mirror(Bar[] bars) => BandedDays.Mirror(bars);

    private static SideRules? WithoutGate(SideRules? rules, int index)
    {
        var items = rules!.Setup!.Items.Where((_, i) => i != index).ToList();
        return rules with { Setup = items.Count == 0 ? null : new ConditionList(rules.Setup.Mode, items) };
    }

    private static StrategyDefinition WithoutGate(StrategyDefinition definition, int index) => definition with { Buy = WithoutGate(definition.Buy, index), Sell = WithoutGate(definition.Sell, index) };

    [Theory]
    [MemberData(nameof(Every))]
    public void A_seed_reads_and_compiles_on_15m_on_both_sides_with_no_problem_and_no_warning_and_the_simulator_accepts_it(string id)
    {
        var definition = Load(id);

        definition.Id.ShouldBe(id);
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            var compiled = StrategyCompiler.Compile(definition, Timeframe.M15, side);
            compiled.IsSuccess.ShouldBeTrue(string.Join("; ", compiled.Problems.Select(p => p.Path + " " + p.Problem.Code)));
            compiled.Problems.ShouldBeEmpty();
        }

        Simulator.Create(definition, Timeframe.M15).Simulator.ShouldNotBeNull();
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void The_buy_day_gives_one_candidate_at_the_decision_instant_with_the_stop_of_the_text_and_no_sell(string id)
    {
        var expected = Table[id];

        var candidate = Candidates(Load(id), Side.Buy, Days(id)).Single();

        candidate.StrategyId.ShouldBe(id);
        Clock(candidate.DecisionEpochSeconds).ShouldBe(expected.Decision);
        candidate.StopLevel.ShouldBe(expected.Stop, 1e-9);
        Candidates(Load(id), Side.Sell, Days(id)).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void The_buy_day_walks_to_the_target_at_the_reward_risk_of_the_text(string id)
    {
        var expected = Table[id];

        var result = Sim.Run(Sim.Create(Load(id), fills: Cand.NoSlippage), Days(id));

        var trade = result.Trades.Single();
        result.OpenAtEnd.ShouldBeEmpty();
        trade.Side.ShouldBe(Side.Buy);
        trade.ExitReason.ShouldBe(ExitReason.Target);
        trade.EntryPrice.ShouldBe(expected.Entry, 1e-9);
        trade.StopPrice.ShouldBe(expected.Stop, 1e-9);
        trade.TargetPrice.GetValueOrDefault().ShouldBe(expected.Target, 1e-6);
        trade.ExitPrice.ShouldBe(expected.Target, 1e-6);
        trade.RExact.ShouldBe(expected.R, 1e-6);
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void The_mirrored_day_walks_as_a_sell_to_the_mirrored_target_and_the_buy_side_stays_empty(string id)
    {
        var expected = Table[id];
        var mirrored = Mirror(Days(id));
        const double Pivot2 = 2 * BandedDays.Pivot;

        var result = Sim.Run(Sim.Create(Load(id), fills: Cand.NoSlippage), mirrored);

        var trade = result.Trades.Single();
        trade.Side.ShouldBe(Side.Sell);
        trade.ExitReason.ShouldBe(ExitReason.Target);
        Clock(Candidates(Load(id), Side.Sell, mirrored).Single().DecisionEpochSeconds).ShouldBe(expected.Decision);
        trade.EntryPrice.ShouldBe(Pivot2 - expected.Entry, 1e-9);
        trade.StopPrice.ShouldBe(Pivot2 - expected.Stop, 1e-9);
        trade.ExitPrice.ShouldBe(Pivot2 - expected.Target, 1e-6);
        trade.RExact.ShouldBe(expected.R, 1e-6);
        Candidates(Load(id), Side.Buy, mirrored).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void The_negative_day_gives_no_candidate_on_either_side(string id)
    {
        var bars = Days(id, negative: true);

        Candidates(Load(id), Side.Buy, bars).ShouldBeEmpty();
        Candidates(Load(id), Side.Sell, Mirror(bars)).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(SetupDays))]
    public void A_day_with_the_trigger_and_one_false_setup_gate_gives_nothing_and_the_same_day_without_that_gate_gives_a_candidate(string id, string name, int gate)
    {
        var definition = Load(id);
        var without = WithoutGate(definition, gate);
        var bars = SetupDay(id, name);

        Candidates(definition, Side.Buy, bars).ShouldBeEmpty();
        Candidates(without, Side.Buy, bars).ShouldNotBeEmpty($"{id} BUY: {name}");
        Candidates(definition, Side.Sell, Mirror(bars)).ShouldBeEmpty();
        Candidates(without, Side.Sell, Mirror(bars)).ShouldNotBeEmpty($"{id} SELL: {name}");
    }

    [Fact]
    public void The_ema_pullback_prefix_has_the_averages_the_hand_calculation_says_at_the_trigger_bar()
    {
        var closes = Closes15(IntradayBatchDays.EmaPullback()).ToArray();

        var (ema9, ema21) = (Ema(closes, 9), Ema(closes, 21));

        closes[^1].ShouldBe(110.70, 1e-9);
        ema9.ShouldBe(110.554, 0.005);
        ema21.ShouldBe(110.484, 0.01);
        (ema9 > ema21).ShouldBeTrue();
        (110.20 <= ema21 - 0.25).ShouldBeTrue("the dip reaches the EMA(21) with a margin of about 0.28");
        (closes[^1] - ema9 > 0.1).ShouldBeTrue("the close clears the EMA(9) by about 0.15");
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void The_candidate_is_the_same_when_the_future_is_cut_off_or_replaced(string id)
    {
        var definition = Load(id);
        var bars = Days(id);
        var full = Candidates(definition, Side.Buy, bars)[0];
        var at = full.DecisionBarIndex;
        var wild = bars.Select((b, i) => i <= at ? b : new Bar(b.EpochSeconds, b.Open * 0.5, b.High * 0.5, b.Low * 0.5, b.Close * 0.5, b.Volume)).ToArray();

        var cut = Candidates(definition, Side.Buy, bars[..(at + 1)]);
        var replaced = Candidates(definition, Side.Buy, wild);

        cut.Count.ShouldBe(1);
        Render.Candidate(cut[0]).ShouldBe(Render.Candidate(full));
        Render.Candidate(replaced[0]).ShouldBe(Render.Candidate(full));
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void A_seed_declares_the_reward_risk_and_no_more_than_four_parameters(string id)
    {
        var definition = Load(id);

        definition.Params.Select(p => p.Name).ShouldContain("rr");
        definition.Params.Count.ShouldBeLessThanOrEqualTo(StrategyDefinition.MaxParams);
        definition.TimeRules!.SquareOff.ShouldBe(new TimeOnly(15, 15));
    }

    /// <summary>The 15-minute closes of the minutes before 10:00 on the fixture day (a bar starts on a multiple of 900 seconds since the epoch, 09:15 India time included).</summary>
    private static IEnumerable<double> Closes15(Bar[] minutes) =>
        minutes.Where(b => b.EpochSeconds < Day.Epoch(IntradayBatchDays.Date, 10 * 60)).GroupBy(b => b.EpochSeconds / 900).OrderBy(g => g.Key).Select(g => g.Last().Close);

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
}
