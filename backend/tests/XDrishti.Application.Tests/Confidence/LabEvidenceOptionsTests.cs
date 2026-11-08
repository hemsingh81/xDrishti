#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Readiness;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>CF-05 (ADR 0229): a run posted with non-default options is a what-if and never the scorecard's evidence basis; a conservative verdict's survivors are never clean tier evidence for row 14 or the tier C hard fail.</summary>
public sealed class LabEvidenceOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LabRunView RunOf(Guid id, DateTimeOffset finished, LabRunOptionsView? options, string method = LabSettings.RobustMethod) =>
        new(id, "k", "succeeded", null, new LabStrategyRef(ReadinessFixture.Strategy, 1, "h"), null!, null!, method, null!, [], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, null, finished, null, options);

    [Fact]
    public async Task The_newest_default_option_run_is_read_and_a_newer_conservative_or_stress_changed_run_is_skipped()
    {
        var oldDefault = Guid.NewGuid();
        var newerConservative = Guid.NewGuid();
        var newestStressOff = Guid.NewGuid();
        var f = new ReadinessFixture();
        f.Runs.HandleAsync(Arg.Any<ListLabRunsQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Result.Success<IReadOnlyList<LabRunView>>(
        [
            RunOf(oldDefault, DateTimeOffset.UnixEpoch.AddDays(1), new LabRunOptionsView("default", true, true)),
            RunOf(newerConservative, DateTimeOffset.UnixEpoch.AddDays(2), new LabRunOptionsView("conservative", true, false)),
            RunOf(newestStressOff, DateTimeOffset.UnixEpoch.AddDays(3), new LabRunOptionsView("default", false, false)),
        ])));

        await f.LabReader.ReadAsync(ReadinessFixture.Strategy, 1, Ct);

        await f.Run.Received(1).HandleAsync(Arg.Is<GetLabRunQuery>(q => q.Id == oldDefault), Arg.Any<CancellationToken>());
        await f.Run.DidNotReceive().HandleAsync(Arg.Is<GetLabRunQuery>(q => q.Id != oldDefault), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_non_default_runs_leave_the_scorecard_without_a_lab_verdict()
    {
        var f = new ReadinessFixture();
        f.Runs.HandleAsync(Arg.Any<ListLabRunsQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Result.Success<IReadOnlyList<LabRunView>>(
            [RunOf(Guid.NewGuid(), DateTimeOffset.UnixEpoch.AddDays(1), new LabRunOptionsView("conservative", true, false))])));

        var evidence = await f.LabReader.ReadAsync(ReadinessFixture.Strategy, 1, Ct);

        evidence.LabTiers.State.ShouldBe(TierSetState.Unavailable);
        evidence.Checklist.CapacityOk.ShouldBeNull();
        evidence.Notes.ShouldContain(n => n.Contains("No finished full Lab run", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_verdict_with_a_tier_exclusion_reads_as_a_conservative_run_for_row_14_and_the_tier_set()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { TierExclusion = new TierExclusionView(1000, 120) };

        var evidence = await f.LabReader.ReadAsync(ReadinessFixture.Strategy, 1, Ct);

        evidence.LabTiers.State.ShouldBe(TierSetState.Unavailable);
        evidence.LabTiers.Reason.ShouldBe("conservative run");
        evidence.Checklist.CapacityOk.ShouldBeNull();
        evidence.Checklist.Notes[14].ShouldContain("conservative run");
    }

    [Fact]
    public async Task A_verdict_whose_strategy_names_min_turnover_fails_closed_for_row_14_and_the_tier_set()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { TurnoverExclusion = new TurnoverExclusionView(1000, 300, 20m) };

        var evidence = await f.LabReader.ReadAsync(ReadinessFixture.Strategy, 1, Ct);

        evidence.LabTiers.State.ShouldBe(TierSetState.Unavailable);
        evidence.LabTiers.Reason.ShouldBe("min_turnover survivors");
        evidence.Checklist.CapacityOk.ShouldBeNull();
        evidence.Checklist.Notes[14].ShouldContain("min_turnover survivors");
    }
}
