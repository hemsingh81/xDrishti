#pragma warning disable CA2012 // NSubstitute setup and verification calls on ValueTask members are not awaited
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Paper;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Paper;

/// <summary>CF-11 SC-S3 (ADR 0224): criterion I of the paper phase check read on its own: no epoch, no capture run, clean, a disagreement, an epoch that has not had a session yet. Every call is a read.</summary>
public sealed class GetCaptureIntegrityHandlerTests
{
    private static readonly DateOnly Start = new(2026, 11, 2);

    // Friday 2026-11-06, 16:00 India time: the session has closed.
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 30, 0, TimeSpan.Zero);

    private readonly IPaperLedgerData _ledger = Substitute.For<IPaperLedgerData>();
    private readonly IPaperPhaseData _phase = Substitute.For<IPaperPhaseData>();
    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    public GetCaptureIntegrityHandlerTests()
    {
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        Epoch(Start);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Epoch(DateOnly? startedOn) =>
        _ledger.EpochAsync(null, Ct).Returns(ValueTask.FromResult<EpochRow?>(startedOn is { } on ? new EpochRow(2, on, DateTimeOffset.UnixEpoch, "owner_restart", "start", "owner") : null));

    private void Captures(params CaptureRunView[] rows) =>
        _phase.CaptureRunsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<CaptureRunView>>(rows));

    private static CaptureRunView Run(int dayOffset, int integrity = 0, int pending = 0) =>
        new(dayOffset + 1, Start.AddDays(dayOffset), Now, Now, "done", 2, 0, 0, 0, pending, integrity, false, "scheduled run");

    private async Task<CaptureIntegrityView> ReadAsync() =>
        await new GetCaptureIntegrityHandler(_calendar, _ledger, _phase, new FakeTimeProvider(Now)).HandleAsync(new GetCaptureIntegrityQuery(), Ct);

    [Fact]
    public async Task With_no_epoch_there_is_no_capture_run_and_the_criterion_is_not_evaluable()
    {
        Epoch(null);

        var view = await ReadAsync();

        view.Verdict.ShouldBe("NOT EVALUABLE");
        view.Through.ShouldBeNull();
        view.CaptureRuns.ShouldBe(0);
        view.Label.ShouldContain("no paper epoch");
        await _phase.DidNotReceiveWithAnyArgs().CaptureRunsAsync(default, default, Ct);
    }

    [Fact]
    public async Task An_epoch_with_no_capture_row_is_not_evaluable()
    {
        Captures();

        var view = await ReadAsync();

        view.Verdict.ShouldBe("NOT EVALUABLE");
        view.CaptureRuns.ShouldBe(0);
        view.Through.ShouldBe(new DateOnly(2026, 11, 6));
    }

    [Fact]
    public async Task Capture_rows_with_no_disagreement_are_met()
    {
        Captures(Run(0), Run(1), Run(2));

        var view = await ReadAsync();

        view.Verdict.ShouldBe("MET");
        view.CaptureRuns.ShouldBe(3);
    }

    [Fact]
    public async Task A_replay_that_disagreed_with_a_stored_entry_is_not_met()
    {
        Captures(Run(0), Run(1, integrity: 1));

        var view = await ReadAsync();

        view.Verdict.ShouldBe("NOT MET");
        view.Label.ShouldContain("disagreed");
    }

    [Fact]
    public async Task An_epoch_that_starts_after_the_last_closed_session_has_nothing_to_read_and_is_not_evaluable()
    {
        Epoch(new DateOnly(2026, 11, 9));

        var view = await ReadAsync();

        view.Verdict.ShouldBe("NOT EVALUABLE");
        await _phase.DidNotReceiveWithAnyArgs().CaptureRunsAsync(default, default, Ct);
    }

    [Fact]
    public async Task A_port_that_fails_fails_the_read_for_the_caller_to_judge_as_unavailable()
    {
        _phase.CaptureRunsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns<ValueTask<IReadOnlyList<CaptureRunView>>>(_ => throw new InvalidOperationException("db down"));

        await Should.ThrowAsync<InvalidOperationException>(async () => await ReadAsync());
    }
}
