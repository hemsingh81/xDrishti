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

    private static readonly Guid[] Instruments = [FabricatedRun.IdA, FabricatedRun.IdB, FabricatedRun.IdC, new("00000000-0000-0000-0000-00000000000d"), new("00000000-0000-0000-0000-00000000000e"), new("00000000-0000-0000-0000-00000000000f")];

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

    /// <summary><paramref name="count"/> intraday fixed-target trades on six instruments (so that each of the five instrument rolls lands on another one) and as many sessions, as the full test pools them.</summary>
    private static FullTestOutcome Full(int count = 40, Func<int, SimTrade>? make = null, string candidate = "FIXED_RR{rr=2}")
    {
        var sources = Enumerable.Range(0, count).Select(i => make?.Invoke(i) ?? Costed(Instruments[i % Instruments.Length], D.AddDays(i))).ToList();
        var oos = sources.Select(t => new SelectionTrade(candidate, t.TradeDate, t.TradeDate, t.Costing!.RNet, t.RExact)).ToList();
        return new FullTestOutcome([], Verdict("PASS"), 2, 2, count, oos, sources);
    }

    private static LabVerdictView Verdict(string outcome) => new(
        outcome, 1, 1,
        new EvidenceView(60, 0.20, 1.5, 8, 6, 3, 0, "walk_forward"),
        [new GateView("oos_trades", 50, 50, 60, true, "ok")],
        ["a limit"], [], [], ["a label"]);

    /// <summary>The placebo walk answers every (signal, shape) pair that has a placement with <paramref name="netR"/> of the shape (null is a pair that made no trade); by default every placebo loses 9 R.</summary>
    private static IReadOnlyList<PlaceboOutcome> Placed(PlaceboWalkPlan plan, Func<int, double?>? netR = null)
    {
        var value = netR ?? (_ => -9);
        return [.. plan.Signals.SelectMany(s => PlaceboShapes.All.Select((shape, i) => (s, i, p: PlaceboShapes.Place(shape, s, plan.RollOrder))).Where(x => x.p is not null))
            .Select(x => value(x.i) is { } v ? new PlaceboOutcome(x.s.Id, x.i, v, null) : new PlaceboOutcome(x.s.Id, x.i, null, "no trade: LimitNotReached"))];
    }

    private static (LabNulls Nulls, List<RunNullWalkQuery> Seen) Make(NullOptions options, Func<NullWalkPlan, IReadOnlyList<double[]>> draws, bool fail = false, Func<int, double?>? placebo = null, List<RunPlaceboWalkQuery>? placeboSeen = null, bool placeboFail = false)
    {
        var seen = new List<RunNullWalkQuery>();
        var placeboWalks = Substitute.For<IQueryHandler<RunPlaceboWalkQuery, Result<PlaceboWalkRun>>>();
        placeboWalks.HandleAsync(Arg.Any<RunPlaceboWalkQuery>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var query = ci.Arg<RunPlaceboWalkQuery>();
            placeboSeen?.Add(query);
            return placeboFail ? Result.Failure<PlaceboWalkRun>(new Error("simulation.p", "no")) : Result.Success(new PlaceboWalkRun(Placed(query.Plan, placebo)));
        });
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
        return (new LabNulls(walks, new LabPlacebos(placeboWalks, options), options, LabSettings.Default), seen);
    }

    /// <summary>Every trade of every seed has the same net R in the seed, shifted by the seed's own offset, so the seed means are <c>offset(d)</c>.</summary>
    private static Func<NullWalkPlan, IReadOnlyList<double[]>> Means(Func<int, double> offset) =>
        plan => [.. plan.Trades.Select(_ => Enumerable.Range(0, plan.Draws).Select(offset).ToArray())];

    [Fact]
    public async Task The_plan_restates_each_intraday_fixed_target_trade_with_its_risk_fraction_target_multiple_and_segment_over_the_window_of_the_run()
    {
        var (nulls, seen) = Make(new NullOptions(), Means(_ => 0));
        var full = Full();

        await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, full, Run.To, true, null, Ct);

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

        var outcome = await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, full, Run.To, true, null, Ct);

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

        var e = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct)).View.RandomEntry;

        e.Status.ShouldBe("fail");
        e.PValue.ShouldNotBeNull().ShouldBeGreaterThan(0.05);
        e.CountAtLeast.ShouldBeGreaterThan(400);
    }

    [Fact]
    public async Task Too_few_trades_is_unavailable_and_never_a_pass_and_carries_the_reason()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => -9));

        var e = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(count: 10), Run.To, true, null, Ct)).View.RandomEntry;

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

        var tooMany = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, some, Run.To, true, null, Ct)).View;
        var enough = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, few, Run.To, true, null, Ct)).View;

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
            (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, full, Run.To, true, null, Ct)).View.RandomEntry.Status.ShouldBe("unavailable");
        }

        seen.ShouldBeEmpty("nothing to restate: the walk is not asked");
    }

    [Fact]
    public async Task A_failed_walk_is_unavailable_with_its_code_and_the_look_is_still_in_the_ledger()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0), fail: true);

        var outcome = await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct);

        outcome.View.RandomEntry.Status.ShouldBe("unavailable");
        outcome.View.RandomEntry.Reason.ShouldContain("simulation.x");
        outcome.Rows.Select(r => r.TrialKind).ShouldAllBe(k => k == "null");
    }

    [Fact]
    public async Task The_family_level_null_is_one_ledger_row_of_kind_null_with_the_same_id_on_a_retry_of_the_same_run_and_another_for_another_run()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0));
        var now = new DateTimeOffset(2026, 11, 5, 6, 0, 0, TimeSpan.Zero);

        var first = (await nulls.RunAsync(Run, now, Template, Full(), Run.To, true, null, Ct)).Rows.Single(r => r.Method == "null");
        var retry = (await nulls.RunAsync(Run, now.AddMinutes(5), Template, Full(), Run.To, true, null, Ct)).Rows.Single(r => r.Method == "null");
        var other = (await nulls.RunAsync(Run with { Id = Guid.NewGuid() }, now, Template, Full(), Run.To, true, null, Ct)).Rows.Single(r => r.Method == "null");

        (first.TrialKind, first.Method, first.ExtraTrials, first.StrategyId, first.StrategyHash, first.RunKey).ShouldBe(("null", "null", 0, "NUL", Run.StrategyHash, Run.Key));
        retry.RunId.ShouldBe(first.RunId);
        other.RunId.ShouldNotBe(first.RunId);
        first.RunId.ShouldNotBe(Run.Id, "the run's own ledger id is the run's");
    }

    [Fact]
    public async Task The_same_seed_for_the_same_key_and_a_different_one_for_another_key()
    {
        var (nulls, seen) = Make(new NullOptions(), Means(_ => 0));

        await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct);
        await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct);
        await nulls.RunAsync(Run with { Key = new string('z', 64) }, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct);

        seen[0].Plan.Seed.ShouldBe(seen[1].Plan.Seed);
        seen[2].Plan.Seed.ShouldNotBe(seen[0].Plan.Seed);
    }

    [Fact]
    public async Task Report_only_adds_the_block_and_leaves_the_outcome_the_gates_and_the_limits_as_they_were()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 9));
        var verdict = Verdict("PASS");
        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct)).View;

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
        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct)).View;

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
        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(count: 5), Run.To, true, null, Ct)).View;

        nulls.Apply(Verdict("PASS"), view).Outcome.ShouldBe("MARGINAL");
    }

    [Fact]
    public void Steps_counts_the_trades_to_restate_up_to_the_development_end()
    {
        int Steps(FullTestOutcome full, DateOnly end) => Make(new NullOptions(Placebos: false), Means(_ => 0)).Nulls.Steps(full, end);
        Steps(Full(), Run.To).ShouldBe(40);
        Steps(Full(), D.AddDays(9)).ShouldBe(10, "the sessions after the development end are not walked");
        Steps(Full(0), Run.To).ShouldBe(0);
    }

    [Fact]
    public async Task The_walk_ends_at_the_development_end_and_a_real_trade_after_it_is_left_out_and_counted()
    {
        var (nulls, seen) = Make(new NullOptions(), Means(_ => 0));
        var end = D.AddDays(34);   // 35 of the 40 sessions are inside

        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), end, true, null, Ct)).View;

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

        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), D.AddDays(19), true, null, Ct)).View;

        seen.Single().Request.To.ShouldBe(D.AddDays(19));
        view.RandomEntry.Status.ShouldBe("unavailable");
        view.RandomEntry.Trades.ShouldBe(20);
    }

    [Fact]
    public async Task The_basis_of_a_window_that_was_not_cut_says_so()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0));

        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct)).View;

        view.Basis.ShouldContain("the run's own window");
        view.Basis.ShouldNotContain("was cut");
    }

    [Fact]
    public async Task The_risk_fraction_is_measured_from_the_trigger_close_the_stop_is_placed_from_and_a_stop_on_the_wrong_side_of_it_is_not_restated()
    {
        var (nulls, seen) = Make(new NullOptions(), Means(_ => 0));
        SimTrade Trigger(int i, double close) => Costed(FabricatedRun.IdA, D.AddDays(i)) with { TriggerBar = new XDrishti.Domain.Market.Bars.Bar(0, 99, close, 99, close, 1) };
        var full = Full(count: 40, make: i => Trigger(i, i == 0 ? 102.5 : i == 1 ? 97 : 100.5));

        await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, full, Run.To, true, null, Ct);

        var plan = seen.Single().Plan;
        plan.Trades.Count.ShouldBe(39, "a buy whose stop (98) is above its trigger close (97) has no loss side to measure from");
        plan.Trades[0].RiskFraction.ShouldBe((102.5 - 98) / 102.5, 1e-12, "|trigger close - stop| / trigger close, not the fill");
    }

    [Fact]
    public async Task The_basis_of_a_run_with_no_hold_out_block_says_none_is_kept()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0));

        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, false, null, Ct)).View;

        view.Basis.ShouldContain("no hold-out block is kept");
        view.Basis.ShouldNotContain("stays clear");
    }

    [Fact]
    public async Task When_every_out_of_sample_trade_is_past_the_development_end_the_reason_says_they_are_inside_the_hold_out_block()
    {
        var (nulls, seen) = Make(new NullOptions(), Means(_ => 0));

        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), D.AddDays(-1), true, null, Ct)).View;

        view.RandomEntry.Status.ShouldBe("unavailable");
        view.RandomEntry.Reason.ShouldContain("every out-of-sample trade is inside the hold-out block");
        view.Placebos!.Status.ShouldBe("unavailable");
        view.Placebos.Reason.ShouldContain("every out-of-sample trade is inside the hold-out block");
        view.Mirror!.Reason.ShouldContain("every out-of-sample trade is inside the hold-out block");
        view.Notes.ShouldNotContain(n => n.StartsWith("Placebo shapes", StringComparison.Ordinal) || n.StartsWith("Instrument rolls", StringComparison.Ordinal));
        seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_restatable_trade_without_a_hold_out_cut_keeps_the_general_reason()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0));

        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(40, candidate: "PARTIAL_BE{t1_rr=1}"), Run.To, true, null, Ct)).View;

        view.RandomEntry.Reason.ShouldContain("no out-of-sample trade is an intraday fixed-target trade");
    }

    // ---- CF-07 part B: the placebo set (D2) and the mirror gate (D3) ----

    [Fact]
    public async Task The_three_blocks_show_one_real_mean_even_when_the_random_entry_walk_leaves_a_trade_out()
    {
        // the random-entry walk gives trade 0 no usable draws, so it is left out of that side; the placebos must leave it out too
        var full = Full(40, i => i == 0 ? Costed(FabricatedRun.IdA, D, entry: 103, target: 108) : Costed(Instruments[i % Instruments.Length], D.AddDays(i)));
        var (nulls, _) = Make(new NullOptions(), plan => [.. plan.Trades.Select((t, i) => i == 0 ? [] : Enumerable.Range(0, plan.Draws).Select(_ => 0.0).ToArray())]);

        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, full, Run.To, true, null, Ct)).View;

        var rest = full.OosSources!.Skip(1).Average(t => t.Costing!.RNet);
        Math.Abs(RealMean(full) - rest).ShouldBeGreaterThan(1e-9, "the left-out trade must move the mean for this test to tell the two sets apart");
        view.RandomEntry.RealMeanR!.Value.ShouldBe(rest, 1e-12);
        view.Placebos!.RealMeanR!.Value.ShouldBe(rest, 1e-12);
        view.Mirror!.RealMeanR!.Value.ShouldBe(rest, 1e-12);
        view.Placebos.Rows.ShouldAllBe(r => r.Trades == 39);
    }

    private static double RealMean(FullTestOutcome full) => full.OosSources!.Average(t => t.Costing!.RNet);

    [Fact]
    public async Task The_placebo_plan_carries_the_signals_the_roll_order_and_the_window_of_the_run_and_reads_no_session_after_the_development_end()
    {
        var seen = new List<RunPlaceboWalkQuery>();
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0), placeboSeen: seen);
        var end = D.AddDays(34);

        await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), end, true, null, Ct);

        var query = seen.Single();
        query.Request.To.ShouldBe(end);
        query.Request.Parameters.ShouldBeEmpty();
        query.Request.Exits!.Candidates.Single().Id.ShouldBe("FIXED_RR{rr=2}");
        query.Plan.Signals.Count.ShouldBe(35);
        query.Plan.Signals.ShouldAllBe(x => x.Date <= end);
        query.Plan.RollOrder.ShouldBe([.. Instruments.Order()]);
        query.Plan.Signals[0].DecisionEpochSeconds.ShouldBe(Full().OosSources![0].DecisionEpochSeconds);
        (query.Plan.Signals[0].Id, query.Plan.Signals[0].InstrumentId, query.Plan.Signals[0].Side).ShouldBe((0, FabricatedRun.IdA, Side.Buy));
    }

    [Fact]
    public async Task A_real_mean_above_every_placebo_ranks_first_of_twenty_one_and_passes_with_the_value_the_threshold_and_the_status()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0));

        var set = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct)).View.Placebos!;

        set.Status.ShouldBe("pass");
        (set.Shapes, set.Comparable, set.RealRank).ShouldBe((20, 20, 1));
        set.PValue!.Value.ShouldBe(1.0 / 21.0, 1e-12);
        (set.Threshold, set.Comparison, set.MinComparable).ShouldBe((0.05, "<=", 19));
        set.Rows.Select(r => r.Shape).ShouldBe(PlaceboShapes.All.Select(x => x.Name));
        set.Rows.ShouldAllBe(r => r.Comparable && r.Trades == 40);
    }

    [Fact]
    public async Task A_placebo_that_earns_more_than_the_real_mean_puts_the_strategy_second_and_fails()
    {
        var full = Full();
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0), placebo: shape => shape == 5 ? RealMean(full) + 0.01 : -9);

        var set = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, full, Run.To, true, null, Ct)).View.Placebos!;

        set.Status.ShouldBe("fail");
        set.RealRank.ShouldBe(2);
        set.PValue!.Value.ShouldBe(2.0 / 21.0, 1e-12);
        set.Rows[5].Rank.ShouldBe(1);
    }

    [Fact]
    public async Task Eighteen_comparable_placebos_is_inconclusive_whatever_the_ranks_and_never_a_pass()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0), placebo: shape => shape is 3 or 4 ? null : -9);

        var set = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct)).View.Placebos!;

        set.Status.ShouldBe("unavailable");
        set.Comparable.ShouldBe(18);
        set.RealRank.ShouldBeNull();
        set.PValue.ShouldBeNull();
        set.Reason.ShouldContain("inconclusive");
        set.Rows.Where(r => !r.Comparable).Select(r => r.Shape).ShouldBe(["shift_+45", "shift_+60"]);
    }

    [Fact]
    public async Task Nineteen_comparable_placebos_is_enough_and_the_strategy_ranks_first_of_twenty()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0), placebo: shape => shape == 4 ? null : -9);

        var set = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct)).View.Placebos!;

        set.Status.ShouldBe("pass");
        set.PValue!.Value.ShouldBe(1.0 / 20.0, 1e-12);
    }

    [Fact]
    public async Task Three_instruments_leave_one_roll_on_itself_so_nineteen_are_compared_and_a_first_place_is_one_in_twenty()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0));
        var three = Full(40, i => Costed(Instruments[i % 3], D.AddDays(i)));

        var set = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, three, Run.To, true, null, Ct)).View.Placebos!;

        set.Comparable.ShouldBe(19, "instrument_roll_3 lands on the signal's own instrument");
        set.Rows.Single(r => !r.Comparable).Reason.ShouldBe("the roll lands on the signal's own instrument (only 3 instruments have a trade)");
        set.Status.ShouldBe("pass");
        set.PValue!.Value.ShouldBe(0.05, 1e-12);
    }

    [Fact]
    public async Task Two_instruments_leave_two_rolls_on_themselves_so_the_set_is_inconclusive()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0));
        var two = Full(40, i => Costed(i % 2 == 0 ? FabricatedRun.IdA : FabricatedRun.IdB, D.AddDays(i)));

        var set = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, two, Run.To, true, null, Ct)).View.Placebos!;

        set.Comparable.ShouldBe(18, "instrument_roll_2 and _4 land on the signal's own instrument");
        set.Rows.Where(r => !r.Comparable).ShouldAllBe(r => r.Reason == "the roll lands on the signal's own instrument (only 2 instruments have a trade)");
        set.Status.ShouldBe("unavailable");
    }

    [Theory]
    [InlineData(-0.10, "pass", false)]
    [InlineData(0.0, "pass", false)]
    [InlineData(0.08, "fail", true)]
    public async Task The_mirror_passes_at_or_below_zero_and_a_positive_mirror_reads_mostly_drift(double mirror, string status, bool drift)
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0), placebo: shape => shape == 0 ? mirror : -9);

        var m = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct)).View.Mirror!;

        m.Status.ShouldBe(status);
        (m.Threshold, m.Comparison, m.Trades).ShouldBe((0.0, "<=", 40));
        m.MirrorMeanR!.Value.ShouldBe(mirror, 1e-12);
        m.Reason.Contains("mostly drift", StringComparison.Ordinal).ShouldBe(drift);
    }

    [Fact]
    public async Task A_mirror_that_placed_too_few_trades_is_unavailable_and_never_a_pass()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0), placebo: shape => shape == 0 ? null : -9);

        var m = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct)).View.Mirror!;

        m.Status.ShouldBe("unavailable");
        m.MirrorMeanR.ShouldBeNull();
    }

    [Fact]
    public async Task A_failed_placebo_walk_is_unavailable_with_its_code_for_the_set_and_the_mirror_and_the_look_is_still_in_the_ledger()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0), placeboFail: true);

        var outcome = await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct);

        outcome.View.Placebos!.Status.ShouldBe("unavailable");
        outcome.View.Placebos.Reason.ShouldContain("simulation.p");
        outcome.View.Mirror!.Status.ShouldBe("unavailable");
        outcome.View.RandomEntry.Status.ShouldBe("pass");
        outcome.Rows.Count.ShouldBe(2);
    }

    [Fact]
    public async Task With_the_placebo_switch_off_there_is_no_placebo_block_no_walk_and_no_second_ledger_row()
    {
        var seen = new List<RunPlaceboWalkQuery>();
        var (nulls, _) = Make(new NullOptions(Placebos: false), Means(_ => 0), placeboSeen: seen);

        var outcome = await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct);

        (outcome.View.Placebos, outcome.View.Mirror).ShouldBe((null, null));
        seen.ShouldBeEmpty();
        outcome.Rows.Single().Method.ShouldBe("null");
    }

    [Fact]
    public async Task The_placebo_set_is_a_second_look_of_kind_null_with_method_placebo_and_an_id_that_is_stable_on_a_retry_and_differs_from_the_random_entry_row()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0));
        var now = new DateTimeOffset(2026, 11, 5, 6, 0, 0, TimeSpan.Zero);

        var first = (await nulls.RunAsync(Run, now, Template, Full(), Run.To, true, null, Ct)).Rows;
        var retry = (await nulls.RunAsync(Run, now.AddMinutes(5), Template, Full(), Run.To, true, null, Ct)).Rows;

        first.Select(r => (r.TrialKind, r.Method)).ShouldBe([("null", "null"), ("null", "placebo")]);
        first.Select(r => r.RunId).Distinct().Count().ShouldBe(2);
        retry.Select(r => r.RunId).ShouldBe(first.Select(r => r.RunId));
        first.ShouldAllBe(r => r.ExtraTrials == 0 && r.RunKey == Run.Key && r.StrategyHash == Run.StrategyHash);
    }

    [Fact]
    public void Steps_count_the_random_entry_trades_then_the_placed_placebo_pairs()
    {
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0));
        var (off, _) = Make(new NullOptions(Placebos: false), Means(_ => 0));

        nulls.Steps(Full(), Run.To).ShouldBe(40 + (40 * 20), "six instruments: every roll has a place");
        off.Steps(Full(), Run.To).ShouldBe(40);
    }

    [Fact]
    public async Task The_placebo_progress_counts_on_from_the_random_entry_trades()
    {
        var seen = new List<RunPlaceboWalkQuery>();
        var (nulls, _) = Make(new NullOptions(), Means(_ => 0), placeboSeen: seen);
        var reported = new List<int>();

        await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, new Collect(reported), Ct);
        seen.Single().Progress!.Report(3);

        reported.ShouldBe([43], "40 trades of the random-entry walk come first");
    }

    private sealed class Collect(List<int> into) : IProgress<int>
    {
        public void Report(int value) => into.Add(value);
    }

    [Fact]
    public async Task Report_only_leaves_the_outcome_alone_when_the_placebo_set_or_the_mirror_fails_and_enforce_caps_a_pass_and_says_which()
    {
        var full = Full();
        var (reportOnly, _) = Make(new NullOptions(), Means(_ => -9), placebo: shape => shape == 0 ? 0.5 : -9);
        var (enforcing, _) = Make(new NullOptions(Enforce: true), Means(_ => -9), placebo: shape => shape == 0 ? 0.5 : -9);
        var view = (await reportOnly.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, full, Run.To, true, null, Ct)).View;

        view.RandomEntry.Status.ShouldBe("pass");
        view.Mirror!.Status.ShouldBe("fail");
        reportOnly.Apply(Verdict("PASS"), view).Outcome.ShouldBe("PASS");
        var capped = enforcing.Apply(Verdict("PASS"), view);
        capped.Outcome.ShouldBe("MARGINAL");
        capped.Limits.ShouldContain(l => l.Contains("the mirror gate (fail)", StringComparison.Ordinal));
        capped.Limits.ShouldNotContain(l => l.Contains("the random-entry null", StringComparison.Ordinal));
        enforcing.Apply(Verdict("FAIL"), view).Outcome.ShouldBe("FAIL");
    }

    [Fact]
    public async Task Enforce_treats_an_inconclusive_placebo_set_as_a_miss()
    {
        var (nulls, _) = Make(new NullOptions(Enforce: true), Means(_ => -9), placebo: shape => shape < 5 ? null : -9);
        var view = (await nulls.RunAsync(Run, DateTimeOffset.UnixEpoch, Template, Full(), Run.To, true, null, Ct)).View;

        view.Placebos!.Status.ShouldBe("unavailable");
        nulls.Apply(Verdict("PASS"), view).Limits.ShouldContain(l => l.Contains("the placebo set (unavailable)", StringComparison.Ordinal));
    }
}
