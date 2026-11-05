using System.Text.Json;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Lab;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Lab;

/// <summary>
/// CF-07 part A (ADR 0208), the random-entry null at the verdict: the plan is the real intraday fixed-target trades with the real risk fraction and target multiple, over the run's own window; a null that passes, fails or cannot be made reads so; thin data and
/// trades that cannot be restated are unavailable and never a pass; the family-level null is one ledger row of kind null, the same id on a retry; Enforce caps a PASS at MARGINAL and never raises it, report-only changes nothing.
/// </summary>
public sealed class LabNullsTests
{
    private static readonly DateOnly D = new(2026, 3, 2);

    private static readonly CostContext Costs = TestCostSheet.Context();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly LabRun Run = new(
        Guid.NewGuid(), new string('k', 64), "NUL", 1, new string('a', 64),
        new LabUniverse("symbols", null, "symbols as listed", 2, [new LabInstrument(FabricatedRun.IdA, "AAA"), new LabInstrument(FabricatedRun.IdB, "BBB")], false, null),
        new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "robust", new string('c', 64), "sim-9", LabRunState.Running, "nulls", 0, 0, [], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);

    private static readonly SimulationRequest Template = new("NUL", new Dictionary<string, double> { ["period"] = 10 }, "15m", Run.From, Run.To, [FabricatedRun.IdA, FabricatedRun.IdB], null, CostChoice.Sheet("fixture-a"), false, null, null, null, null, Run.StrategyHash);

    private static SimTrade Costed(Guid instrument, DateOnly date, double entry = 100.5, double stop = 98, double? target = 105.5, ValueList<Leg>? legs = null)
    {
        var trade = FabricatedRun.Trade(instrument, "X", Side.Buy, date, entry: entry, stop: stop, target: target);
        return TradeCostApplier.Apply(trade, Costs, CostSegment.IntradayEquity).Value.Trade! with { Legs = legs };
    }

    /// <summary><paramref name="count"/> intraday fixed-target trades on two instruments and as many sessions, as the full test pools them.</summary>
    private static FullTestOutcome Full(int count = 40, Func<int, SimTrade>? make = null, string candidate = "FIXED_RR{rr=2}")
    {
        var sources = Enumerable.Range(0, count).Select(i => make?.Invoke(i) ?? Costed(i % 2 == 0 ? FabricatedRun.IdA : FabricatedRun.IdB, D.AddDays(i))).ToList();
        var oos = sources.Select(t => new SelectionTrade(candidate, t.TradeDate, t.TradeDate, t.Costing!.RNet, t.RExact)).ToList();
        return new FullTestOutcome([], Verdict("PASS"), 2, 2, count, oos, sources);
    }

    private static LabVerdictView Verdict(string outcome) => new(
        outcome, 1, 1,
        new EvidenceView(60, 0.20, 1.5, 8, 6, 3, 0, "walk_forward"),
        [new GateView("oos_trades", 50, 50, 60, true, "ok")],
        ["a limit"], [], [], ["a label"]);

    private static (LabNulls Nulls, List<RunNullWalkQuery> Seen) Make(NullOptions options, Func<NullWalkPlan, IReadOnlyList<double[]>> draws, bool fail = false)
    {
        var seen = new List<RunNullWalkQuery>();
        var walks = Substitute.For<IQueryHandler<RunNullWalkQuery, Result<NullWalkRun>>>();
        walks.HandleAsync(Arg.Any<RunNullWalkQuery>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var query = ci.Arg<RunNullWalkQuery>();
            seen.Add(query);
            if (fail)
            {
                return Result.Failure<NullWalkRun>(new Error("simulation.x", "no"));
            }

            var rows = draws(query.Plan);
            return Result.Success(new NullWalkRun([.. query.Plan.Trades.Select((t, i) => new NullTradeDraws(t.Id, rows[i], 0, null))]));
        });
        return (new LabNulls(walks, options, LabSettings.Default), seen);
    }

    /// <summary>Every trade of every seed has the same net R in the seed, shifted by the seed's own offset, so the seed means are <c>offset(d)</c>.</summary>
    private static Func<NullWalkPlan, IReadOnlyList<double[]>> Means(Func<int, double> offset) =>
        plan => [.. plan.Trades.Select(_ => Enumerable.Range(0, plan.Draws).Select(offset).ToArray())];

    [Fact]
    public async Task The_plan_restates_each_intraday_fixed_target_trade_with_its_risk_fraction_target_multiple_and_segment_over_the_window_of_the_run()
    {
        var (nulls, seen) = Make(new NullOptions(), Means(_ => 0));
        var full = Full();

        await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, full, Run.To, null, Ct);

        var query = seen.Single();
        query.Request.To.ShouldBe(Run.To, "the walk is cut to the window of the run, which is the development window");
        query.Request.From.ShouldBe(Run.From);
        query.Request.Parameters.ShouldBeEmpty();
        query.Request.Exits!.Candidates.Single().Id.ShouldBe("FIXED_RR{rr=2}");
        query.Plan.Trades.Count.ShouldBe(40);
        query.Plan.Trades.ShouldAllBe(t => t.Date <= Run.To);
        var first = query.Plan.Trades[0];
        (first.Id, first.InstrumentId, first.Date, first.Side, first.Segment).ShouldBe((0, FabricatedRun.IdA, D, Side.Buy, CostSegment.IntradayEquity));
        first.RiskFraction.ShouldBe((100.5 - 98) / 100.5, 1e-12);
        first.RewardRisk.ShouldBe(2.0, 1e-9, "(105.5 - 100.5) / (100.5 - 98)");
        (query.Plan.Draws, query.Plan.Redraws).ShouldBe((1000, 5));
        query.Plan.Seed.ShouldBe(SeededRandom.SeedOf(Run.Key + ":random-entry"));
    }

    [Fact]
    public async Task A_real_mean_above_nearly_every_seed_passes_with_the_p_value_the_threshold_and_the_seed()
    {
        var (nulls, _) = Make(new NullOptions(), Means(d => -0.5 + (d * 0.0001)));
        var full = Full();

        var outcome = await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, full, Run.To, null, Ct);

        var e = outcome.View.RandomEntry;
        e.Status.ShouldBe("pass");
        e.PValue!.Value.ShouldBe(1.0 / 1001.0, 1e-12);
        (e.Threshold, e.Comparison, e.Trades, e.OosTrades, e.Draws, e.ValidSeeds, e.CountAtLeast).ShouldBe((0.05, "<=", 40, 40, 1000, 1000, 0));
        e.RealMeanR!.Value.ShouldBe(full.OosTrades.Average(t => t.R), 1e-12);
        e.Seed.ShouldBe(SeededRandom.SeedOf(Run.Key + ":random-entry").ToString(System.Globalization.CultureInfo.InvariantCulture));
        outcome.View.Mode.ShouldBe("ReportOnly");
        outcome.View.Enforce.ShouldBeFalse();
        outcome.View.SettingsHash.ShouldBe(new NullOptions().SnapshotHash());
    }

    [Fact]
    public async Task A_real_mean_inside_the_seed_means_fails()
    {
        var real = Full().OosTrades.Average(t => t.R);
        var (nulls, _) = Make(new NullOptions(), Means(d => real + ((d - 500) * 0.0001)));

        var e = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, null, Ct)).View.RandomEntry;

        e.Status.ShouldBe("fail");
        e.PValue.ShouldNotBeNull().ShouldBeGreaterThan(0.05);
        e.CountAtLeast.ShouldBeGreaterThan(400);
    }

    [Fact]
    public async Task Too_few_trades_is_unavailable_and_never_a_pass_and_carries_the_reason()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => -9));

        var e = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(count: 10), Run.To, null, Ct)).View.RandomEntry;

        e.Status.ShouldBe("unavailable");
        e.PValue.ShouldBeNull();
        e.Reason.ShouldStartWith("unavailable:");
    }

    [Fact]
    public async Task Trades_that_cannot_be_restated_are_left_out_counted_and_a_run_that_leaves_out_too_many_is_unavailable()
    {
        var (nulls, seen) = Make(new NullOptions(), Means(_ => -9));
        // 40 trades: 8 with a target missing (a trail), 2 with legs is still within 80 % kept ... here 9 left out of 40 is 77.5 % kept
        var some = Full(40, i => i < 9 ? Costed(FabricatedRun.IdA, D.AddDays(i), target: null) : Costed(FabricatedRun.IdB, D.AddDays(i)));
        var few = Full(40, i => i < 7 ? Costed(FabricatedRun.IdA, D.AddDays(i), target: null) : Costed(FabricatedRun.IdB, D.AddDays(i)));

        var tooMany = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, some, Run.To, null, Ct)).View;
        var enough = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, few, Run.To, null, Ct)).View;

        tooMany.RandomEntry.Status.ShouldBe("unavailable");
        tooMany.RandomEntry.Reason.ShouldContain("only 31 of 40");
        enough.RandomEntry.Status.ShouldBe("pass");
        (enough.RandomEntry.Trades, enough.RandomEntry.OosTrades).ShouldBe((33, 40));
        enough.Notes.ShouldContain(n => n.StartsWith("7 of 40 out-of-sample trades could not be restated", StringComparison.Ordinal));
        seen.Last().Plan.Trades.Count.ShouldBe(33);
    }

    [Fact]
    public async Task A_trade_with_legs_or_a_candidate_that_is_not_a_fixed_target_or_carries_a_time_stop_is_not_restated()
    {
        var (nulls, seen) = Make(new NullOptions(), Means(_ => -9));
        var legs = new ValueList<Leg>([new Leg(1, 100.5, 100.5, 1m, LegKind.Entry, string.Empty)]);

        var withLegs = Full(40, i => Costed(FabricatedRun.IdA, D.AddDays(i), legs: legs));
        var partial = Full(candidate: "PARTIAL_BE{t1_rr=1}");
        var timeStop = Full(candidate: "FIXED_RR{rr=2}+time_stop");

        foreach (var full in new[] { withLegs, partial, timeStop })
        {
            (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, full, Run.To, null, Ct)).View.RandomEntry.Status.ShouldBe("unavailable");
        }

        seen.ShouldBeEmpty("nothing to restate: the walk is not asked");
    }

    [Fact]
    public async Task A_failed_walk_is_unavailable_with_its_code_and_the_look_is_still_in_the_ledger()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0), fail: true);

        var outcome = await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, null, Ct);

        outcome.View.RandomEntry.Status.ShouldBe("unavailable");
        outcome.View.RandomEntry.Reason.ShouldContain("simulation.x");
        outcome.Row.TrialKind.ShouldBe("null");
    }

    [Fact]
    public async Task The_family_level_null_is_one_ledger_row_of_kind_null_with_the_same_id_on_a_retry_of_the_same_run_and_another_for_another_run()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0));
        var now = new DateTimeOffset(2026, 11, 5, 6, 0, 0, TimeSpan.Zero);

        var first = (await nulls.RunAsync(Run, now, Template, Full(), Run.To, null, Ct)).Row;
        var retry = (await nulls.RunAsync(Run, now.AddMinutes(5), Template, Full(), Run.To, null, Ct)).Row;
        var other = (await nulls.RunAsync(Run with { Id = Guid.NewGuid() }, now, Template, Full(), Run.To, null, Ct)).Row;

        (first.TrialKind, first.Method, first.ExtraTrials, first.StrategyId, first.StrategyHash, first.RunKey).ShouldBe(("null", "null", 0, "NUL", Run.StrategyHash, Run.Key));
        retry.RunId.ShouldBe(first.RunId);
        other.RunId.ShouldNotBe(first.RunId);
        first.RunId.ShouldNotBe(Run.Id, "the run's own ledger id is the run's");
    }

    [Fact]
    public async Task The_same_seed_for_the_same_key_and_a_different_one_for_another_key()
    {
        var (nulls, seen) = Make(new NullOptions(), Means(_ => 0));

        await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, null, Ct);
        await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, null, Ct);
        await nulls.RunAsync(Run with { Key = new string('z', 64) }, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, null, Ct);

        seen[0].Plan.Seed.ShouldBe(seen[1].Plan.Seed);
        seen[2].Plan.Seed.ShouldNotBe(seen[0].Plan.Seed);
    }

    [Fact]
    public async Task Report_only_adds_the_block_and_leaves_the_outcome_the_gates_and_the_limits_as_they_were()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 9));
        var verdict = Verdict("PASS");
        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, null, Ct)).View;

        var applied = nulls.Apply(verdict, view);

        view.RandomEntry.Status.ShouldBe("fail");
        applied.Outcome.ShouldBe("PASS");
        (applied with { Nulls = null }).ShouldBe(verdict);
        applied.Nulls.ShouldBe(view);
        JsonSerializer.Serialize(applied with { Nulls = null }).ShouldBe(JsonSerializer.Serialize(verdict));
    }

    [Theory]
    [InlineData("PASS", 9.0, "MARGINAL", true)]
    [InlineData("PASS", -9.0, "PASS", false)]
    [InlineData("MARGINAL", 9.0, "MARGINAL", false)]
    [InlineData("FAIL", 9.0, "FAIL", false)]
    public async Task Enforce_caps_a_pass_at_marginal_when_the_null_is_not_a_pass_and_never_raises(string outcome, double seedMean, string expected, bool capped)
    {
        var options = new NullOptions(Enforce: true);
        var (nulls, _) = Make(options, Means(_ => seedMean));
        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, null, Ct)).View;

        var applied = nulls.Apply(Verdict(outcome), view);

        applied.Outcome.ShouldBe(expected);
        applied.Nulls!.Capped.ShouldBe(capped);
        applied.Gates.ShouldBe(Verdict(outcome).Gates);
        if (capped)
        {
            applied.Limits.ShouldContain(l => l.Contains("random-entry null", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Enforce_treats_an_unavailable_null_as_a_miss()
    {
        var (nulls, _) = Make(new NullOptions(Enforce: true), Means(_ => 0));
        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(count: 5), Run.To, null, Ct)).View;

        nulls.Apply(Verdict("PASS"), view).Outcome.ShouldBe("MARGINAL");
    }

    [Fact]
    public void Steps_counts_the_trades_to_restate_up_to_the_development_end()
    {
        LabNulls.Steps(Full(), Run.To).ShouldBe(40);
        LabNulls.Steps(Full(), D.AddDays(9)).ShouldBe(10, "the sessions after the development end are not walked");
        LabNulls.Steps(Full(0), Run.To).ShouldBe(0);
    }

    [Fact]
    public async Task The_walk_ends_at_the_development_end_and_a_real_trade_after_it_is_left_out_and_counted()
    {
        var (nulls, seen) = Make(new NullOptions(), Means(_ => 0));
        var end = D.AddDays(34);   // 35 of the 40 sessions are inside

        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), end, null, Ct)).View;

        var query = seen.Single();
        query.Request.To.ShouldBe(end, "the request ends at the development end, not at the end of the run");
        query.Plan.Trades.Count.ShouldBe(35);
        query.Plan.Trades.ShouldAllBe(t => t.Date <= end);
        view.RandomEntry.OosTrades.ShouldBe(40);
        view.RandomEntry.Trades.ShouldBe(35);
        view.RandomEntry.Status.ShouldNotBe("unavailable", "35 of 40 is 87 %, over the 80 % rule");
        view.Notes.ShouldContain(n => n.Contains("5 out-of-sample trades are after", StringComparison.Ordinal) && n.Contains("hold-out", StringComparison.Ordinal));
        view.Notes.ShouldContain(n => n.StartsWith("5 of 40", StringComparison.Ordinal));
        view.Basis.ShouldContain("cut to stay clear of the hold-out block");
        view.Basis.ShouldContain(end.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Too_many_real_trades_after_the_development_end_is_unavailable_and_the_walk_reads_nothing_further()
    {
        var (nulls, seen) = Make(new NullOptions(), Means(_ => 0));

        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), D.AddDays(19), null, Ct)).View;

        seen.Single().Request.To.ShouldBe(D.AddDays(19));
        view.RandomEntry.Status.ShouldBe("unavailable");
        view.RandomEntry.Trades.ShouldBe(20);
    }

    [Fact]
    public async Task The_basis_of_a_window_that_was_not_cut_says_so()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0));

        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, null, Ct)).View;

        view.Basis.ShouldContain("the run's own window");
        view.Basis.ShouldNotContain("was cut");
    }

    [Fact]
    public async Task The_risk_fraction_is_measured_from_the_trigger_close_the_stop_is_placed_from_and_a_stop_on_the_wrong_side_of_it_is_not_restated()
    {
        var (nulls, seen) = Make(new NullOptions(), Means(_ => 0));
        SimTrade Trigger(int i, double close) => Costed(FabricatedRun.IdA, D.AddDays(i)) with { TriggerBar = new XDrishti.Domain.Market.Bars.Bar(0, 99, close, 99, close, 1) };
        var full = Full(count: 40, make: i => Trigger(i, i == 0 ? 102.5 : i == 1 ? 97 : 100.5));

        await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, full, Run.To, null, Ct);

        var plan = seen.Single().Plan;
        plan.Trades.Count.ShouldBe(39, "a buy whose stop (98) is above its trigger close (97) has no loss side to measure from");
        plan.Trades[0].RiskFraction.ShouldBe((102.5 - 98) / 102.5, 1e-12, "|trigger close - stop| / trigger close, not the fill");
    }
}
