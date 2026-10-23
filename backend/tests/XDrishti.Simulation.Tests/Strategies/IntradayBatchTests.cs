using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.File;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>
/// The eight intraday strategies of P6-17a exist only as stored text (ADR 0079). Each seed file is read and compiled with no problem and no warning on both sides, fires on a fabricated day at the decision instant, with the stop and
/// the exit written in <see cref="IntradayBatchDays"/>, does not fire on its negative day, fires as a SELL on the mirrored day, and gives the same candidate when the future is cut off or replaced (no look-ahead).
/// Nothing here says a strategy has an edge: it says the mechanics are what the text says.
/// </summary>
public sealed class IntradayBatchTests
{
    private static readonly string[] Ids = ["PDH_PDL_BREAK", "PDH_PDL_REJECT", "VWAP_RECLAIM", "EMA_PULLBACK", "GAP_GO", "GAP_FILL", "INSIDE_BAR_BO", "MEAN_REV_BANDS"];

    // id, decision time, BUY stop, entry, target (exit at the target on the walk row of the day).
    public static readonly TheoryData<string, string, double, double, double> Cases = new()
    {
        { "PDH_PDL_BREAK", "10:15", 110.25, 111.25, 113.25 },
        { "PDH_PDL_REJECT", "10:00", 109.75, 110.40, 111.70 },
        { "VWAP_RECLAIM", "10:00", 109.25, 110.00, 111.50 },
        { "EMA_PULLBACK", "10:00", 110.40, 110.60, 111.00 },
        { "GAP_GO", "10:00", 111.00, 112.10, 114.30 },
        { "GAP_FILL", "10:00", 109.25, 110.00, 111.50 },
        { "INSIDE_BAR_BO", "10:00", 110.25, 111.40, 113.70 },
        { "MEAN_REV_BANDS", "10:45", 108.80, 109.60, 110.80 },
    };

    public static readonly TheoryData<string> Every = [.. Ids];

    private static StrategyDefinition Load(string id)
    {
        var read = StrategyFile.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Seed", id + ".yaml")));
        read.HasErrors.ShouldBeFalse(string.Join("; ", read.Problems.Select(p => p.Code + " " + p.Message)));
        return read.Definition.ShouldNotBeNull();
    }

    private static Bar[] Days(string id, bool negative) => id switch
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

    private static string Clock(long epoch) => Render.Instant(epoch)[11..];

    private static IReadOnlyList<EntryCandidate> Candidates(StrategyDefinition definition, Side side, IEnumerable<Bar> bars) => Scan.Run(definition, side, bars).Candidates;

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
    [MemberData(nameof(Cases))]
    public void The_buy_day_gives_one_candidate_at_the_decision_instant_with_the_stop_of_the_text(string id, string decision, double stop, double entry, double target)
    {
        _ = (entry, target);

        var candidate = Candidates(Load(id), Side.Buy, Days(id, negative: false)).Single();

        candidate.StrategyId.ShouldBe(id);
        Clock(candidate.DecisionEpochSeconds).ShouldBe(decision);
        candidate.StopLevel.ShouldBe(stop, 1e-9);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_buy_day_walks_to_the_target_at_the_reward_risk_of_the_text(string id, string decision, double stop, double entry, double target)
    {
        _ = decision;
        var definition = Load(id);

        var result = Sim.Run(Sim.Create(definition, fills: Cand.NoSlippage), Days(id, negative: false));

        var trade = result.Trades.Single();
        result.OpenAtEnd.ShouldBeEmpty();
        trade.Side.ShouldBe(Side.Buy);
        trade.ExitReason.ShouldBe(ExitReason.Target);
        trade.EntryPrice.ShouldBe(entry, 1e-9);
        trade.StopPrice.ShouldBe(stop, 1e-9);
        trade.TargetPrice.GetValueOrDefault().ShouldBe(target, 1e-6);
        trade.ExitPrice.ShouldBe(target, 1e-6);
        trade.RExact.ShouldBe(id == "MEAN_REV_BANDS" ? 1.5 : 2.0, 1e-6);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_mirrored_day_gives_the_sell_candidate_with_the_mirrored_stop(string id, string decision, double stop, double entry, double target)
    {
        _ = (entry, target);

        var candidate = Candidates(Load(id), Side.Sell, BandedDays.Mirror(Days(id, negative: false))).Single();

        Clock(candidate.DecisionEpochSeconds).ShouldBe(decision);
        candidate.StopLevel.ShouldBe(2 * BandedDays.Pivot - stop, 1e-9);
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void The_negative_day_gives_no_candidate_on_either_side(string id)
    {
        var definition = Load(id);
        var bars = Days(id, negative: true);

        Candidates(definition, Side.Buy, bars).ShouldBeEmpty();
        Candidates(definition, Side.Sell, BandedDays.Mirror(bars)).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void The_candidate_is_the_same_when_the_future_is_cut_off_or_replaced(string id)
    {
        var definition = Load(id);
        var bars = Days(id, negative: false);
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
}
