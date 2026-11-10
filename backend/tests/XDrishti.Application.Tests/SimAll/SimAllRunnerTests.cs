using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.SimAll;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.Tests.SimAll;

/// <summary>AU-12 (ADR 0243, ADR 0248) acceptance 2 to 7 and the owner rules, with fakes: resume after a crash, cancel, pause, failed chunks, the market-hours and disk yields, the worker limit and the sweep ledger rows. No broker is involved anywhere.</summary>
public sealed class SimAllRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // 2026-11-10 is a Tuesday; 14:30 UTC is 20:00 IST, 05:00 UTC is 10:30 IST
    private static readonly DateTimeOffset Evening = new(2026, 11, 10, 14, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MarketMorning = new(2026, 11, 10, 5, 0, 0, TimeSpan.Zero);

    private readonly FakeSimAllStore _store = new();
    private readonly FakeLedger _ledger = new();
    private readonly FakeGate _gate = new();
    private readonly FakeDisk _disk = new();
    private readonly FakeExecutor _executor = new();
    private readonly FakeTimeProvider _time = new(MarketMorning); // each test sets the hour it needs; the clock can only move forward

    private SimAllRunner Runner(SimAllSettings? settings = null)
    {
        var calendar = Substitute.For<ITradingCalendar>();
        calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        var scopes = new FakeScopes((typeof(ISimAllStore), _store), (typeof(ITestLedger), _ledger), (typeof(ISimAllBackfillGate), _gate), (typeof(IFreeDiskProbe), _disk), (typeof(ISimAllChunkExecutor), _executor), (typeof(ITradingCalendar), calendar));
        return new SimAllRunner(scopes, settings ?? new SimAllSettings(), _time, NullLogger<SimAllRunner>.Instance);
    }

    [Fact]
    public async Task A_restart_resumes_at_the_first_unfinished_chunk_and_redoes_the_one_left_running()
    {
        _time.SetUtcNow(Evening);
        _store.Seed(strategies: 1, instruments: 6);
        _store.Chunks[0].State = SimAllStates.Done;
        _store.Chunks[1].State = SimAllStates.Done;
        _store.Chunks[2].State = SimAllStates.ChunkRunning; // the host died with this one in flight
        var runner = Runner();

        await runner.RecoverAsync(Ct);
        await runner.TickAsync(Ct);

        _executor.Ran.Count.ShouldBe(4);
        _executor.Ran.Select(r => r.Instrument).ShouldNotContain(_store.Chunks[0].Chunk.InstrumentId);
        _executor.Ran.Select(r => r.Instrument).ShouldNotContain(_store.Chunks[1].Chunk.InstrumentId);
        _executor.Ran.Select(r => r.Instrument).ShouldContain(_store.Chunks[2].Chunk.InstrumentId);
        _store.Job!.State.ShouldBe(SimAllStates.Completed);
        _store.Count(SimAllStates.Done).ShouldBe(6);
    }

    [Fact]
    public async Task A_failed_chunk_is_recorded_with_its_reason_the_job_goes_on_and_ends_with_failures()
    {
        _time.SetUtcNow(Evening);
        _store.Seed(1, 4);
        var bad = _store.Chunks[1].Chunk.InstrumentId;
        _executor.Script = c => c.InstrumentId == bad ? new SimAllChunkResult(SimAllStates.Failed, null, 0, 0, 5, "simulation.invalid", "no bars") : null;

        await Runner().TickAsync(Ct);

        _store.Count(SimAllStates.Done).ShouldBe(3);
        _store.Count(SimAllStates.Failed).ShouldBe(1);
        _store.Chunks[1].Result!.FailCode.ShouldBe("simulation.invalid");
        _store.Job!.State.ShouldBe(SimAllStates.CompletedWithFailures);
    }

    [Fact]
    public async Task A_cancel_while_a_chunk_runs_stops_the_claims_and_leaves_the_rest_pending()
    {
        _time.SetUtcNow(Evening);
        _store.Seed(1, 6);
        _executor.OnRun = _ => _store.CancelAsync(_store.Job!.Id, _time.GetUtcNow(), Ct).AsTask().Wait(Ct);

        await Runner(new SimAllSettings { Workers = 1 }).TickAsync(Ct);

        _store.Job!.State.ShouldBe(SimAllStates.Cancelled);
        _executor.Ran.Count.ShouldBe(1);
        _store.Count(SimAllStates.Pending).ShouldBe(5);
    }

    [Fact]
    public async Task A_job_paused_by_the_owner_stays_paused_and_runs_nothing()
    {
        _time.SetUtcNow(Evening);
        _store.Seed(1, 3, SimAllStates.Paused, SimAllPause.Owner);
        var runner = Runner();

        await runner.TickAsync(Ct);

        _executor.Ran.ShouldBeEmpty();
        _store.Job!.State.ShouldBe(SimAllStates.Paused);
    }

    [Fact]
    public async Task In_market_hours_the_job_pauses_and_after_the_close_it_resumes_by_itself()
    {
        _store.Seed(1, 3);
        var runner = Runner();

        await runner.TickAsync(Ct);

        _store.Job!.State.ShouldBe(SimAllStates.Paused);
        _store.Job.PauseReason.ShouldBe(SimAllPause.MarketHours);
        _executor.Ran.ShouldBeEmpty();

        _time.SetUtcNow(Evening);
        await runner.TickAsync(Ct);

        _store.Job!.State.ShouldBe(SimAllStates.Completed);
        _executor.Ran.Count.ShouldBe(3);
    }

    [Fact]
    public async Task The_market_hours_rule_can_be_switched_off_by_the_setting()
    {
        _store.Seed(1, 2);

        await Runner(new SimAllSettings { RunInMarketHours = true }).TickAsync(Ct);

        _store.Job!.State.ShouldBe(SimAllStates.Completed);
    }

    [Fact]
    public async Task Free_disk_under_the_floor_pauses_with_a_reason_and_resumes_when_space_is_back()
    {
        _time.SetUtcNow(Evening);
        _store.Seed(1, 2);
        _disk.Free = 39L * SimAllSettings.Gb;
        var runner = Runner();

        await runner.TickAsync(Ct);

        _store.Job!.PauseReason.ShouldBe(SimAllPause.LowDisk);
        _executor.Ran.ShouldBeEmpty();

        _disk.Free = 41L * SimAllSettings.Gb;
        await runner.TickAsync(Ct);

        _store.Job!.State.ShouldBe(SimAllStates.Completed);
    }

    [Fact]
    public async Task A_disk_that_cannot_be_read_counts_as_too_low()
    {
        _time.SetUtcNow(Evening);
        _store.Seed(1, 2);
        _disk.Free = null;

        await Runner().TickAsync(Ct);

        _store.Job!.PauseReason.ShouldBe(SimAllPause.LowDisk);
    }

    [Fact]
    public async Task A_running_bar_history_job_makes_the_simulation_wait_and_a_lost_backfill_pauses_it()
    {
        _time.SetUtcNow(Evening);
        _store.Seed(1, 2);
        _gate.DataJob = true;
        var runner = Runner();

        await runner.TickAsync(Ct);
        _store.Job!.PauseReason.ShouldBe(SimAllPause.DataJob);

        _gate.DataJob = false;
        _gate.Incomplete = "3 of 10 tracked instruments have no stored bars yet.";
        await runner.TickAsync(Ct);
        _store.Job!.State.ShouldBe(SimAllStates.Paused); // the backfill is not complete, so it keeps waiting
        _executor.Ran.ShouldBeEmpty();

        _gate.Incomplete = null;
        await runner.TickAsync(Ct);
        _store.Job!.State.ShouldBe(SimAllStates.Completed);
    }

    [Fact]
    public async Task The_number_of_chunks_at_once_is_the_worker_setting_and_never_more()
    {
        _time.SetUtcNow(Evening);
        _store.Seed(1, 12);
        _executor.BarrierSize = 3; // three at once are reached, which proves the limit is held at three and not two

        await Runner(new SimAllSettings { Workers = 3 }).TickAsync(Ct);

        _executor.Peak.ShouldBe(3);
        _store.Job!.State.ShouldBe(SimAllStates.Completed);
    }

    [Fact]
    public async Task One_training_sweep_ledger_row_is_written_per_strategy_version_before_any_chunk_and_not_twice_after_a_restart()
    {
        _time.SetUtcNow(Evening);
        _store.Seed(strategies: 2, instruments: 3);
        var seenAtFirstChunk = -1;
        _executor.OnRun = _ => { if (seenAtFirstChunk < 0) { seenAtFirstChunk = _ledger.Rows.Count; } };
        var runner = Runner();

        await runner.TickAsync(Ct);
        _store.Job = _store.Job! with { State = SimAllStates.Running, FinishedAt = null }; // a restart ticks the same job again
        await runner.TickAsync(Ct);

        seenAtFirstChunk.ShouldBe(2);
        _ledger.Rows.Count.ShouldBe(2);
        _ledger.Rows.ShouldAllBe(r => r.TrialKind == LedgerRow.TrainingSweepKind && r.To == _store.Job.WindowTo);
        _ledger.Rows.Select(r => r.StrategyId).Order().ShouldBe(["S1", "S2"]);
    }

    [Fact]
    public async Task With_no_job_a_tick_does_nothing()
    {
        _time.SetUtcNow(Evening);
        await Runner().TickAsync(Ct);

        _executor.Ran.ShouldBeEmpty();
        _ledger.Rows.ShouldBeEmpty();
    }
}
