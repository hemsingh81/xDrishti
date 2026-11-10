#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>AU-14 behind the ports: a report over a simulate-everything job reads only finished chunks, never asks the store past the training window, says how complete it is, and names the stocks it found nothing for.</summary>
public sealed class TrainingReportLoaderTests
{
    private static readonly Guid Job = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly DateOnly From = new(2026, 1, 5);
    private static readonly DateOnly WindowTo = new(2026, 3, 31);
    private static readonly DateOnly HoldOut = new(2026, 4, 15);
    private static readonly Guid Reliance = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private readonly ReportWorld _world = new();
    private ReportTradeFilter? _seen;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Job_is(string state, int done, int failed, int running, int pending)
    {
        var job = new SimAllJob(Job, state, null, From, WindowTo, HoldOut, 1, "owner:x", ReportWorld.T0, ReportWorld.T0, null);
        _world.Jobs.GetProgressAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<SimAllSnapshot?>(new SimAllSnapshot(job, new SimAllCounts(done + failed + running + pending, done, failed, running, pending, 0, 0, 0))));
    }

    private void Store_has(params ReportTrade[] trades)
    {
        _world.Training.StreamTradesAsync(Job, Arg.Any<DateTimeOffset>(), Arg.Any<ReportTradeFilter>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _seen = call.ArgAt<ReportTradeFilter>(2);
            return trades.Where(t => (_seen.From is null || t.EntryDate >= _seen.From) && (_seen.To is null || t.EntryDate <= _seen.To)).ToAsyncEnumerable();
        });
        _world.Training.ListRunsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(call =>
            new ValueTask<IReadOnlyList<TrainingRunHeader>>([.. call.ArgAt<IReadOnlyList<string>>(0).Select(k => new TrainingRunHeader(k, "ORB", new string('d', 64), From, WindowTo, 10, 1, 10, 0, ReportWorld.T0))]));
    }

    private static ReportTrade Trade(char run, int day, double r, string symbol = "NSE:REL") =>
        new(ReportWorld.Key(run), $"S{run}|{symbol}|BUY|{day:D3}", "S" + run, "15m", "BUY", Reliance, symbol, new ValueList<string>([]), From.AddDays(day), From.AddDays(day), r, null, 1);

    private static ReportQuery Training(string extra = "") => ReportWorld.Query(new ReportQueryInput(Purpose: "training", SimJob: Job.ToString("D"), From: extra.Length == 0 ? null : extra));

    [Fact]
    public async Task A_running_job_answers_with_what_is_finished_and_says_it_is_not_complete()
    {
        Job_is("running", done: 3, failed: 0, running: 1, pending: 6);
        Store_has(Trade('a', 0, 1), Trade('a', 1, -1), Trade('b', 2, 2));

        var loaded = await _world.Loader.LoadAsync(ReportWorld.Query(new ReportQueryInput(Purpose: "training", SimJob: Job.ToString("D"))), "summary", Ct);

        loaded.IsSuccess.ShouldBeTrue(loaded.IsFailure ? loaded.Error.Message : string.Empty);
        using var report = loaded.Value;
        report.Population.TradeCount.ShouldBe(3);
        var c = report.Completeness!;
        (c.Complete, c.ChunksDone, c.ChunksTotal, c.ChunksRunning, c.ChunksPending, c.JobState).ShouldBe((false, 3, 10, 1, 6, "running"));
        c.Kind.ShouldBe("training_simulation");
        c.Label.ShouldContain("not evidence");
        c.AsOf.ShouldBe(ReportWorld.T0);
        c.TrainingRecord.ShouldBe($"/api/simulate-all/{Job:D}/training");
        report.DefaultRunSet.ShouldBeFalse();
    }

    [Fact]
    public async Task A_completed_job_with_every_chunk_done_is_complete_and_a_completed_job_with_a_failure_is_not()
    {
        Job_is("completed", 10, 0, 0, 0);
        Store_has(Trade('a', 0, 1));
        using (var whole = (await _world.Loader.LoadAsync(Training(), "summary", Ct)).Value)
        {
            whole.Completeness!.Complete.ShouldBeTrue();
        }

        Job_is("completed_with_failures", 9, 1, 0, 0);
        using var partial = (await _world.Loader.LoadAsync(Training(), "summary", Ct)).Value;
        partial.Completeness!.Complete.ShouldBeFalse();
        partial.Completeness.ChunksFailed.ShouldBe(1);
    }

    [Fact]
    public async Task A_range_past_the_window_is_cut_before_the_hold_out_and_the_store_is_never_asked_for_it()
    {
        Job_is("completed", 10, 0, 0, 0);
        Store_has(Trade('a', 0, 1), Trade('a', 100, 5)); // day 100 is 2026-04-15, the first day of the hold-out block

        var query = ReportWorld.Query(new ReportQueryInput(Purpose: "training", From: "2026-01-01", To: "2026-12-31"));
        using var report = (await _world.Loader.LoadAsync(query, "summary", Ct)).Value;

        _seen!.To.ShouldBe(WindowTo);
        _seen.From.ShouldBe(From);
        report.Population.TradeCount.ShouldBe(1);
        report.Completeness!.RangeCut.ShouldBeTrue();
        report.Completeness.RangeTo.ShouldBe(WindowTo);
        report.Completeness.Locked!.ShouldContain("hold-out block is locked");
        report.Completeness.HoldOutStart.ShouldBe(HoldOut);
    }

    [Fact]
    public async Task A_range_wholly_inside_the_hold_out_reads_nothing_at_all_and_says_the_part_before_it_is_empty()
    {
        Job_is("completed", 10, 0, 0, 0);
        Store_has(Trade('a', 0, 1));

        var query = ReportWorld.Query(new ReportQueryInput(Purpose: "training", From: "2026-04-20", To: "2026-05-30"));
        using var report = (await _world.Loader.LoadAsync(query, "summary", Ct)).Value;

        _seen.ShouldBeNull("the store was not asked");
        report.Population.TradeCount.ShouldBe(0);
        report.Completeness!.RangeFrom.ShouldBeNull();
        report.Completeness.Locked!.ShouldContain("which is empty");
    }

    [Fact]
    public async Task A_stock_with_no_result_is_named_and_one_with_results_is_not()
    {
        Job_is("running", 1, 0, 1, 8);
        Store_has(Trade('a', 0, 1));

        var query = ReportWorld.Query(new ReportQueryInput(Purpose: "training", Symbol: "NSE:REL,NSE:NOPE"));
        using var report = (await _world.Loader.LoadAsync(query, "summary", Ct)).Value;

        report.Completeness!.SymbolsWithoutResults.ShouldBe(["NSE:NOPE"]);
        _seen!.Symbols.ShouldBe(["NSE:REL", "NSE:NOPE"]);
    }

    [Fact]
    public async Task No_job_is_not_found_and_the_evidence_reports_never_touch_the_training_port()
    {
        _world.Jobs.GetProgressAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<SimAllSnapshot?>((SimAllSnapshot?)null));
        var missing = await _world.Loader.LoadAsync(Training(), "summary", Ct);
        missing.Error.Code.ShouldBe("reports.not_found");

        _world.Training.ClearReceivedCalls();
        _world.Jobs.ClearReceivedCalls();
        _world.AddRun('a');
        _world.AddTrades('a', [1, -1]);
        using var evidence = (await _world.Loader.LoadAsync(ReportQuery.Default, "summary", Ct)).Value;

        evidence.Completeness.ShouldBeNull();
        evidence.Population.TradeCount.ShouldBe(2);
        _world.Training.ReceivedCalls().ShouldBeEmpty();
        await _world.Jobs.DidNotReceive().GetProgressAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_training_report_larger_than_the_cap_is_refused_and_never_cut()
    {
        Job_is("completed", 10, 0, 0, 0);
        _world.TrainingSettings.MaxTrades = 10_000;
        var many = Enumerable.Range(0, 10_001).Select(i => Trade('a', 0, 1) with { TradeKey = "k" + i });
        _world.Training.StreamTradesAsync(Job, Arg.Any<DateTimeOffset>(), Arg.Any<ReportTradeFilter>(), Arg.Any<CancellationToken>()).Returns(many.ToAsyncEnumerable());

        var result = await _world.Loader.LoadAsync(Training(), "summary", Ct);

        result.Error.Code.ShouldBe("reports.too_large");
        result.Error.Message.ShouldContain("narrow");
    }

    [Fact]
    public async Task Runs_of_different_stocks_are_different_universes_so_they_are_never_taken_for_one_population()
    {
        // two strategies over two stocks: the same strategy over two instruments must not be refused as an overlap
        Job_is("completed", 4, 0, 0, 0);
        var other = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        Store_has(Trade('a', 0, 1), Trade('b', 0, 1, "NSE:TCS") with { InstrumentId = other });

        var result = await _world.Loader.LoadAsync(Training(), "groups", Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Runs.Select(r => r.Universe).Distinct().Count().ShouldBe(2);
        result.Value.Dispose();
    }
}
