#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Reads;
using XDrishti.Application.Tests.Replay.Job;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay.Reads;

/// <summary>SR-01 part C: the read side over stored runs: labels first, the costs label, completeness while running, the blocked reasons, the equity with no benchmark, and the pair that is not comparable until both runs are complete.</summary>
public sealed class ReplayReadsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] Expected = ["settings_now", "data_seen_before", "before_costs", "survivorship"];

    private static readonly DateOnly D1 = new(2026, 10, 5);
    private static readonly DateOnly D2 = new(2026, 10, 6);
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 14, 30, 0, TimeSpan.Zero);

    private static NewReplayRun New(string mode, string? sheet = null, int minutes = 0)
    {
        var p = ReplayRig.Params(2, sheet, mode: mode == "learning" ? ReplayMode.Learning : ReplayMode.Frozen) with { From = D1, To = D2 };
        return new NewReplayRun(Guid.NewGuid(), D1, D2, mode, ReplayJson.Canonical(p), "{\"fill\":{}}", "{}", null, new string('a', 64), new string('b', 64), 2, T0.AddMinutes(minutes));
    }

    private static ReplaySessionRow Session(DateOnly day, decimal equity, int open = 0, params ReplayBlocked[] blocked)
    {
        var record = new ReplaySessionRecord(day, [new ReplayCandidateRow("AAA", "BUY", "S1|1D|BUY|swing|FIXED_RR", 1)], [], [], blocked, [], [], equity, 0m, 0m, open, ["survivorship"], []);
        var text = ReplayJson.Canonical(record);
        return new ReplaySessionRow(day, text, equity, 0m, ReplayJson.Sha256(text));
    }

    private static async Task<(FakeReplayStore Store, Guid Id)> StoredAsync(string mode = "frozen", string? sheet = null, bool finish = false)
    {
        var store = new FakeReplayStore();
        var run = New(mode, sheet);
        await store.InsertAsync(null, [run], Ct);
        store.RecordAll();
        await store.ClaimAsync(Ct);
        var blocked = new ReplayBlocked("AAA", "S1|1D|BUY|swing|FIXED_RR", BlockReason.MaxPositions, "full");
        await store.SaveSessionAsync(run.Id, Session(D1, 1_000_000m, 0, blocked), [], Ct);
        if (finish)
        {
            await store.SaveSessionAsync(run.Id, Session(D2, 1_001_000m), [], Ct);
            await store.FinishAsync(run.Id, ReplayRunStates.Completed, new string('d', 64), null, [], T0, Ct);
        }

        return (store, run.Id);
    }

    [Fact]
    public async Task A_running_gross_run_reads_incomplete_with_the_hindsight_labels_first_and_the_costs_label()
    {
        var (store, id) = await StoredAsync();

        var view = (await new GetReplayRunHandler(store).HandleAsync(new GetReplayRunQuery(id), Ct)).Value;

        (view.Completeness.Complete, view.Completeness.SessionsDone, view.Completeness.SessionsTotal, view.Completeness.AsOfSession).ShouldBe((false, 1, 2, D1));
        view.CostsLabel.ShouldBe("before costs (charge sheet not filled, ISS-25)");
        Expected.ShouldBeSubsetOf(view.Hindsight.Select(l => l.Code));
        view.Hindsight.ShouldAllBe(l => l.Text.Length > 0);
        view.Basis.ShouldBe("gross");
    }

    [Fact]
    public async Task A_run_with_a_sheet_reads_net_and_without_the_before_costs_label()
    {
        var (store, id) = await StoredAsync(sheet: "sheet-1");

        var view = (await new GetReplayRunHandler(store).HandleAsync(new GetReplayRunQuery(id), Ct)).Value;

        view.Basis.ShouldBe("net");
        view.CostsLabel.ShouldContain("sheet-1");
        view.Hindsight.Select(l => l.Code).ShouldNotContain("before_costs");
    }

    [Fact]
    public async Task An_unknown_run_is_not_found()
    {
        var result = await new GetReplayRunHandler(new FakeReplayStore()).HandleAsync(new GetReplayRunQuery(Guid.NewGuid()), Ct);

        result.Error.Code.ShouldBe("replay.not_found");
    }

    [Fact]
    public async Task The_session_list_counts_blocked_candidates_by_reason_and_the_days_result()
    {
        var (store, id) = await StoredAsync(finish: true);

        var list = (await new ListReplaySessionsHandler(store).HandleAsync(new ListReplaySessionsQuery(id, null, null), Ct)).Value;

        list.Sessions.Select(s => s.Session).ShouldBe([D1, D2]);
        list.Sessions[0].BlockedByReason.ShouldBe(new Dictionary<string, int> { ["max_positions"] = 1 });
        list.Sessions[1].Pnl.ShouldBe(1000m);
    }

    [Fact]
    public async Task A_stored_session_is_read_in_full_and_one_not_stored_yet_is_not_found()
    {
        var (store, id) = await StoredAsync();
        var handler = new GetReplaySessionHandler(store);

        var full = (await handler.HandleAsync(new GetReplaySessionQuery(id, D1), Ct)).Value;
        var missing = await handler.HandleAsync(new GetReplaySessionQuery(id, D2), Ct);

        full.Blocked.Single().Reason.ShouldBe("max_positions");
        full.OpenBookComplete.ShouldBeFalse("the run is still active");
        missing.Error.Code.ShouldBe("replay.session_not_found");
    }

    [Fact]
    public async Task The_equity_with_no_benchmark_index_says_why_and_still_gives_the_curve()
    {
        var (store, id) = await StoredAsync(finish: true);
        var benchmark = Substitute.For<IBasketBenchmark>();
        benchmark.GetAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new BasketBenchmark(null, true, "Nifty 500", "not in the master list")));

        var view = (await new GetReplayEquityHandler(store, benchmark, Substitute.For<IReplayFacts>()).HandleAsync(new GetReplayEquityQuery(id, null, null), Ct)).Value;

        view.BenchmarkReason.ShouldBe("not in the master list");
        view.Points.Select(p => p.Equity).ShouldBe([1_000_000m, 1_001_000m]);
        view.Points.ShouldAllBe(p => p.Benchmark == null);
        view.CashComplete.ShouldBeTrue();
    }

    [Fact]
    public async Task A_pair_has_no_answer_until_both_runs_are_complete()
    {
        var store = new FakeReplayStore();
        var pair = Guid.NewGuid();
        await store.InsertAsync(pair, [New("frozen"), New("learning", minutes: 1)], Ct);

        var view = (await new GetReplayPairHandler(store).HandleAsync(new GetReplayPairQuery(pair), Ct)).Value;

        view.Answer.ShouldBeNull();
        view.Reason!.ShouldContain("complete");
        (await new GetReplayPairHandler(store).HandleAsync(new GetReplayPairQuery(Guid.NewGuid()), Ct)).Error.Code.ShouldBe("replay.pair_not_found");
    }

    [Fact]
    public async Task A_complete_pair_with_too_few_days_answers_too_little_history_with_its_n()
    {
        var store = new FakeReplayStore();
        var pair = Guid.NewGuid();
        var (f, l) = (New("frozen"), New("learning", minutes: 1));
        await store.InsertAsync(pair, [f, l], Ct);
        store.RecordAll();
        foreach (var run in new[] { f, l })
        {
            await store.ClaimAsync(Ct);
            await store.SaveSessionAsync(run.Id, Session(D1, 1_000_000m), [], Ct);
            await store.SaveSessionAsync(run.Id, Session(D2, 1_000_500m), [], Ct);
            await store.FinishAsync(run.Id, ReplayRunStates.Completed, new string('d', 64), null, [], T0, Ct);
        }

        var view = (await new GetReplayPairHandler(store).HandleAsync(new GetReplayPairQuery(pair), Ct)).Value;

        view.Answer.ShouldBe("too_little_history");
        view.Days.ShouldBe(2);
    }

    private static (IBasketBenchmark Benchmark, IReplayFacts Facts) Index(params (DateOnly Day, double Close)[] closes)
    {
        var benchmark = Substitute.For<IBasketBenchmark>();
        benchmark.GetAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new BasketBenchmark(new BenchmarkInstrument(Guid.NewGuid(), "NIFTY500", "Nifty 500", "index", false, true, true), true, "Nifty 500", null)));
        var facts = Substitute.For<IReplayFacts>();
        facts.DayClosesAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<(DateOnly Session, double Close)>>([.. closes]));
        return (benchmark, facts);
    }

    [Fact]
    public async Task The_benchmark_is_rebased_to_the_capital_at_the_close_before_the_first_session_and_carried_over_a_missing_day()
    {
        var (store, id) = await StoredAsync(finish: true);
        var (benchmark, facts) = Index((D1.AddDays(-3), 100), (D1, 110)); // no bar on D2

        var view = (await new GetReplayEquityHandler(store, benchmark, facts).HandleAsync(new GetReplayEquityQuery(id, null, null), Ct)).Value;

        view.Points.Select(p => p.Benchmark).ShouldBe([1_100_000m, 1_100_000m]);
        view.BenchmarkSymbol.ShouldBe("NIFTY500");
    }

    [Fact]
    public async Task A_from_date_does_not_move_the_benchmark_base()
    {
        var (store, id) = await StoredAsync(finish: true);
        var (benchmark, facts) = Index((D1.AddDays(-3), 100), (D1, 110), (D2, 121));

        var view = (await new GetReplayEquityHandler(store, benchmark, facts).HandleAsync(new GetReplayEquityQuery(id, D2, null), Ct)).Value;

        view.Points.Select(p => (p.Session, p.Benchmark)).ShouldBe([(D2, 1_210_000m)]);
    }

    [Fact]
    public async Task A_benchmark_read_past_the_hold_out_fence_gives_a_reason_and_not_an_error()
    {
        var (store, id) = await StoredAsync(finish: true);
        var (benchmark, facts) = Index();
        facts.DayClosesAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<(DateOnly, double)>>(_ => throw new ReplayHoldOutException(D2, D1));

        var result = await new GetReplayEquityHandler(store, benchmark, facts).HandleAsync(new GetReplayEquityQuery(id, null, null), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.BenchmarkReason.ShouldNotBeNull();
        result.Value.Points.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Every_read_of_a_run_states_the_hindsight_labels_first_and_the_list_has_the_last_equity_from_the_run_row()
    {
        var (store, id) = await StoredAsync(finish: true);
        var gross = "before_costs";

        var list = (await new ListReplayRunsHandler(store).HandleAsync(new ListReplayRunsQuery(0, 10), Ct)).Value;
        var equity = (await new GetReplayEquityHandler(store, Index().Benchmark, Index().Facts).HandleAsync(new GetReplayEquityQuery(id, null, null), Ct)).Value;
        var sessions = (await new ListReplaySessionsHandler(store).HandleAsync(new ListReplaySessionsQuery(id, null, null), Ct)).Value;
        var session = (await new GetReplaySessionHandler(store).HandleAsync(new GetReplaySessionQuery(id, D1), Ct)).Value;
        var trades = (await new ListReplayTradesHandler(store).HandleAsync(new ListReplayTradesQuery(id), Ct)).Value;

        foreach (var labels in new[] { list[0].Hindsight, equity.Hindsight, sessions.Hindsight, session.Hindsight, trades.Hindsight })
        {
            labels.Select(l => l.Code).ShouldContain(gross);
        }

        (list[0].FinalEquity, list[0].MaxDrawdownPct).ShouldBe((1_001_000m, 0m));
        sessions.Sessions[0].Labels.Single().Code.ShouldBe("survivorship");
    }

    [Fact]
    public void Run_parameters_that_cannot_be_read_are_treated_as_gross_and_still_say_before_costs()
    {
        ReplayReadSupport.Labels(null).Select(l => l.Code).ShouldContain("before_costs");
    }

    [Fact]
    public async Task The_summary_figures_come_from_the_paper_ledger_over_the_stored_trades()
    {
        var (store, id) = await StoredAsync(finish: true);
        await store.FinishAsync(id, ReplayRunStates.Completed, new string('d', 64), null, [], T0, Ct); // no-op on a finished run

        var view = (await new GetReplayRunHandler(store).HandleAsync(new GetReplayRunQuery(id), Ct)).Value;

        view.Figures!.FinalEquity.ShouldBe(1_000_000m, "capital plus the realised result of the stored trades (none); the ledger's, not the session's");
    }
}
