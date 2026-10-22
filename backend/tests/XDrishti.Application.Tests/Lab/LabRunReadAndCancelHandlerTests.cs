using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Tests.Lab;

/// <summary>The reads and the cancel of Lab runs (P5-02 criterion 6 and the contract): states as words, the limit, the three outcomes of a cancel.</summary>
public sealed class LabRunReadAndCancelHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ILabRunStore _runs = Substitute.For<ILabRunStore>();

    private readonly ILabProgress _progress = Substitute.For<ILabProgress>();

    private static LabRun Run(Guid id, LabRunState state) => new(
        id, new string('k', 64), "S", 1, new string('a', 64), new LabUniverse("symbols", null, "l", 0, [], false, null),
        new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), "quick", new string('c', 64), "sim-5", state, null, 3, 12, ["r"], null, [], [], ["x"], null, false, DateTimeOffset.UnixEpoch, null, null);

    private CancelLabRunHandler Cancel() => new(_runs, _progress, TimeProvider.System, NullLogger<CancelLabRunHandler>.Instance);

    [Fact]
    public async Task The_view_of_a_run_has_the_state_as_a_word_the_progress_and_no_verdict()
    {
        var id = Guid.NewGuid();
        _runs.GetAsync(id, Ct).Returns(Run(id, LabRunState.Running));

        var view = (await new GetLabRunHandler(_runs).HandleAsync(new GetLabRunQuery(id), Ct)).Value;

        view.State.ShouldBe("running");
        view.Progress.ShouldBe(new LabProgressView(3, 12));
        view.Strategy.ShouldBe(new LabStrategyRef("S", 1, new string('a', 64)));
        view.SimRunKeys.ShouldBe(["r"]);
        view.Verdict.ShouldBeNull();
    }

    [Fact]
    public async Task A_run_that_is_not_stored_is_not_found()
    {
        var result = await new GetLabRunHandler(_runs).HandleAsync(new GetLabRunQuery(Guid.NewGuid()), Ct);

        result.Error.Code.ShouldBe(LabErrors.NotFoundCode);
    }

    [Fact]
    public async Task The_list_maps_the_state_word_caps_the_limit_and_refuses_an_unknown_state()
    {
        _runs.ListAsync("S", LabRunState.Failed, 200, Ct).Returns([Run(Guid.NewGuid(), LabRunState.Failed)]);
        var handler = new ListLabRunsHandler(_runs);

        var listed = await handler.HandleAsync(new ListLabRunsQuery("S", "failed", 5000), Ct);
        var refused = await handler.HandleAsync(new ListLabRunsQuery(null, "paused", null), Ct);
        var zero = await handler.HandleAsync(new ListLabRunsQuery(null, null, 0), Ct);

        listed.Value.Single().State.ShouldBe("failed");
        refused.Error.Code.ShouldBe(LabErrors.InvalidInputCode);
        zero.Error.Code.ShouldBe(LabErrors.InvalidInputCode);
    }

    [Fact]
    public async Task Cancelling_a_queued_run_cancels_it_at_once_and_sends_its_last_event()
    {
        var id = Guid.NewGuid();
        _runs.RequestCancelAsync(id, Arg.Any<DateTimeOffset>(), Ct).Returns(LabCancelOutcome.Cancelled);
        _runs.GetAsync(id, Ct).Returns(Run(id, LabRunState.Cancelled));

        var result = await Cancel().HandleAsync(new CancelLabRunCommand(id), Ct);

        result.Value.State.ShouldBe("cancelled");
        await _progress.Received(1).FinishedAsync(new LabRunFinished(id, "cancelled"), Ct);
    }

    [Fact]
    public async Task Cancelling_a_running_run_only_asks_and_the_job_sends_the_last_event_itself()
    {
        var id = Guid.NewGuid();
        _runs.RequestCancelAsync(id, Arg.Any<DateTimeOffset>(), Ct).Returns(LabCancelOutcome.Requested);
        _runs.GetAsync(id, Ct).Returns(Run(id, LabRunState.Running));

        var result = await Cancel().HandleAsync(new CancelLabRunCommand(id), Ct);

        result.IsSuccess.ShouldBeTrue();
        await _progress.DidNotReceiveWithAnyArgs().FinishedAsync(default!, Ct);
    }

    [Fact]
    public async Task Cancelling_a_finished_run_is_a_conflict_and_an_unknown_run_is_not_found()
    {
        var id = Guid.NewGuid();
        _runs.RequestCancelAsync(id, Arg.Any<DateTimeOffset>(), Ct).Returns(LabCancelOutcome.NotCancellable);
        _runs.GetAsync(id, Ct).Returns(Run(id, LabRunState.Succeeded));

        var finished = await Cancel().HandleAsync(new CancelLabRunCommand(id), Ct);
        var unknown = await Cancel().HandleAsync(new CancelLabRunCommand(Guid.NewGuid()), Ct);

        finished.Error.Code.ShouldBe(LabErrors.NotCancellableCode);
        finished.Error.Kind.ShouldBe(ErrorKind.Conflict);
        unknown.Error.Code.ShouldBe(LabErrors.NotFoundCode);
    }
}
