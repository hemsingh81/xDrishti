#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Lab;
using XDrishti.Application.SimAll;
using XDrishti.Application.Tests.Lab;
using XDrishti.Application.Tests.LearnedSettings;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.Tests.SimAll;

/// <summary>AU-12 acceptance 1 and the entry conditions of ADR 0243 point 7: the start fails closed before backfill completion and without a hold-out, creates one chunk per (strategy version, instrument) and answers a second start with a conflict.</summary>
public sealed class SimAllHandlerTests
{
    private const string Owner = "owner:me@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeSimAllStore _store = new();
    private readonly FakeGate _gate = new();
    private readonly FakeAccountEvents _events = new();
    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();
    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();
    private readonly ISimAllUniverse _universe = Substitute.For<ISimAllUniverse>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 14, 30, 0, TimeSpan.Zero));

    private StartSimAllHandler Handler(bool history = true)
    {
        var options = new HoldOutOptions();
        _bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(history
            ? new BarTotals(2, 1000, new DateTimeOffset(2024, 10, 1, 3, 45, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 30, 10, 0, 0, TimeSpan.Zero), 0, 0)
            : new BarTotals(0, 0, null, null, 0, 0));
        _universe.InstrumentIdsAsync(Arg.Any<CancellationToken>()).Returns([Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]);
        _strategies.ListAsync(Arg.Any<CancellationToken>()).Returns([new StoredStrategy("ORB", "x", true, _time.GetUtcNow(), 2, 2), new StoredStrategy("VWAP", "x", false, _time.GetUtcNow(), 1, 1)]);
        _strategies.GetVersionAsync("ORB", null, Arg.Any<CancellationToken>()).Returns(new StoredVersion("ORB", 2, 1, new string('a', 64), "t", "{}", null, null, _time.GetUtcNow()));
        _strategies.GetVersionAsync("VWAP", null, Arg.Any<CancellationToken>()).Returns(new StoredVersion("VWAP", 1, 1, new string('b', 64), "t", "{}", null, null, _time.GetUtcNow()));
        var resolver = new TrainingWindowResolver(new LabHoldOutGuard(new MemoryHoldOutStore(), _bars, options, _time), options);
        return new StartSimAllHandler(_gate, resolver, _universe, _strategies, _store, new SimAllSettings(), _events, _events, _time, NullLogger<StartSimAllHandler>.Instance);
    }

    [Fact]
    public async Task A_start_creates_one_chunk_per_latest_strategy_version_and_instrument_in_a_window_clear_of_the_hold_out()
    {
        var result = await Handler().HandleAsync(new StartSimAllCommand(Owner), Ct);

        result.IsSuccess.ShouldBeTrue();
        _store.Chunks.Count.ShouldBe(6);
        _store.Chunks.Select(c => (c.Chunk.StrategyId, c.Chunk.Version)).Distinct().Count().ShouldBe(2);
        _store.Chunks.Select(c => c.Chunk.Ordinal).ShouldBe([1, 2, 3, 4, 5, 6]);
        result.Value.ChunksTotal.ShouldBe(6);
        result.Value.ChunksRemaining.ShouldBe(6);
        result.Value.WindowTo.ShouldBeLessThan(result.Value.HoldOutStart.AddDays(-30));
        _events.Recorded.Single().Event.ShouldBe(XDrishti.Domain.Accounts.AccountEventType.SimulateAllStarted);
    }

    [Fact]
    public async Task A_start_before_the_backfill_is_complete_is_refused_with_the_reason_and_creates_nothing()
    {
        _gate.Incomplete = "2 of 9 tracked instruments have no stored bars yet.";

        var result = await Handler().HandleAsync(new StartSimAllCommand(Owner), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(SimAllErrors.BackfillIncompleteCode);
        result.Error.Message.ShouldContain("2 of 9");
        _store.Inserts.ShouldBe(0);
        _events.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_start_without_a_determinable_hold_out_fails_closed()
    {
        var result = await Handler(history: false).HandleAsync(new StartSimAllCommand(Owner), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LabErrors.HoldOutUnavailableCode);
        _store.Inserts.ShouldBe(0);
    }

    [Fact]
    public async Task A_second_start_while_one_is_active_is_a_conflict()
    {
        var handler = Handler();
        (await handler.HandleAsync(new StartSimAllCommand(Owner), Ct)).IsSuccess.ShouldBeTrue();

        var second = await handler.HandleAsync(new StartSimAllCommand(Owner), Ct);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe(SimAllErrors.ActiveCode);
        second.Error.Kind.ShouldBe(XDrishti.Domain.Common.ErrorKind.Conflict);
        _store.Inserts.ShouldBe(1);
    }

    [Fact]
    public async Task Only_the_signed_in_owner_starts_a_job()
    {
        var result = await Handler().HandleAsync(new StartSimAllCommand("someone"), Ct);

        result.Error.Code.ShouldBe(SimAllErrors.OwnerOnlyCode);
        _store.Inserts.ShouldBe(0);
    }

    [Fact]
    public async Task Pause_resume_and_cancel_follow_the_state_and_are_recorded()
    {
        await Handler().HandleAsync(new StartSimAllCommand(Owner), Ct);
        var pause = new PauseSimAllHandler(_store, _events, _events, _time, NullLogger<PauseSimAllHandler>.Instance);
        var resume = new ResumeSimAllHandler(_store, _gate, _events, _events, _time, NullLogger<ResumeSimAllHandler>.Instance);
        var cancel = new CancelSimAllHandler(_store, _events, _events, _time, NullLogger<CancelSimAllHandler>.Instance);

        (await pause.HandleAsync(new PauseSimAllCommand(Owner), Ct)).Value.PauseReason.ShouldBe(SimAllPause.Owner);
        (await resume.HandleAsync(new ResumeSimAllCommand(Owner), Ct)).Value.State.ShouldBe(SimAllStates.Running);
        (await resume.HandleAsync(new ResumeSimAllCommand(Owner), Ct)).Error.Code.ShouldBe(SimAllErrors.NotPausedCode);
        (await cancel.HandleAsync(new CancelSimAllCommand(Owner), Ct)).Value.State.ShouldBe(SimAllStates.Cancelled);
        (await cancel.HandleAsync(new CancelSimAllCommand(Owner), Ct)).Error.Code.ShouldBe(SimAllErrors.NoActiveJobCode);
        _events.Recorded.Select(e => e.Event).ShouldBe([XDrishti.Domain.Accounts.AccountEventType.SimulateAllStarted, XDrishti.Domain.Accounts.AccountEventType.SimulateAllPaused, XDrishti.Domain.Accounts.AccountEventType.SimulateAllResumed, XDrishti.Domain.Accounts.AccountEventType.SimulateAllCancelled]);
    }

    [Fact]
    public async Task A_resume_while_the_backfill_is_incomplete_is_refused_and_the_job_stays_paused()
    {
        await Handler().HandleAsync(new StartSimAllCommand(Owner), Ct);
        await new PauseSimAllHandler(_store, _events, _events, _time, NullLogger<PauseSimAllHandler>.Instance).HandleAsync(new PauseSimAllCommand(Owner), Ct);
        _gate.Incomplete = "no instrument is tracked for history.";

        var result = await new ResumeSimAllHandler(_store, _gate, _events, _events, _time, NullLogger<ResumeSimAllHandler>.Instance).HandleAsync(new ResumeSimAllCommand(Owner), Ct);

        result.Error.Code.ShouldBe(SimAllErrors.BackfillIncompleteCode);
        _store.Job!.State.ShouldBe(SimAllStates.Paused);
    }

    [Fact]
    public void The_estimate_appears_once_one_percent_of_the_chunks_is_done_and_says_what_it_rests_on()
    {
        var job = new SimAllJob(Guid.NewGuid(), SimAllStates.Running, null, new DateOnly(2020, 1, 1), new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), 2, Owner, _time.GetUtcNow(), _time.GetUtcNow(), null);

        SimAllView.Of(new SimAllSnapshot(job, new SimAllCounts(1000, 9, 0, 2, 989, 9000, 90_000, 900))).Estimate.ShouldBeNull();

        var view = SimAllView.Of(new SimAllSnapshot(job, new SimAllCounts(1000, 10, 0, 2, 988, 10_000, 100_000, 1000)));

        view.Estimate.ShouldNotBeNull();
        view.Estimate.AverageChunkMilliseconds.ShouldBe(1000);
        view.Estimate.RemainingSeconds.ShouldBe(990 * 1000 / 2 / 1000);
        view.Estimate.RemainingStoredBytes.ShouldBe(990 * (10_000 + 100 * SimAllView.BytesPerTrade));
        view.CoveredFrom.ShouldBe(job.WindowFrom);
    }
}
