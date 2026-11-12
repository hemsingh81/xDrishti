using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Learning;

/// <summary>CF-10e parts 10e-1 and 10e-2: the influence evidence from the shadow scores and the stored monitor row, and the rollback evidence from the champion's scores. Unavailable is never a pass.</summary>
public sealed class ShadowEvidenceTests
{
    private readonly IShadowScoreReader _scores = Substitute.For<IShadowScoreReader>();

    private readonly IMlMonitorChecks _checks = Substitute.For<IMlMonitorChecks>();

    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    // Friday 2026-11-06 12:00 UTC: the last closed session is Thursday 2026-11-05 (15:30 India time is 10:00 UTC, so Friday's is not closed yet at 09:00 UTC)
    private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StoredModel Model => MlInfluenceKit.Model(17);

    private ShadowMlInfluenceEvidence Evidence()
    {
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        _scores.OutcomesAsync(17, Ct).Returns((IReadOnlyList<ScoredOutcome>)[]);
        return new ShadowMlInfluenceEvidence(_scores, _checks, _calendar, _time);
    }

    private async Task<bool?> AlarmAsync(string verdicts, DateOnly day)
    {
        var evidence = Evidence();
        _checks.LatestAsync(17, Ct).Returns(new StoredMlMonitorCheck(day, verdicts));
        return (await evidence.ReadAsync(Model, Ct)).CalibrationAlarm;
    }

    private static string Verdict(string state) => $"[{{\"Kind\":\"calibration\",\"State\":\"{state}\",\"Value\":null,\"Limit\":null,\"Text\":\"t\"}},{{\"Kind\":\"band\",\"State\":\"Unavailable\",\"Value\":null,\"Limit\":null,\"Text\":\"t\"}}]";

    private static readonly DateOnly Thursday = new(2026, 11, 5);

    [Fact]
    public async Task A_quiet_calibration_reading_of_the_last_closed_session_is_no_alarm()
    {
        (await AlarmAsync(Verdict("Quiet"), Thursday)).ShouldBe(false);
    }

    [Fact]
    public async Task A_firing_calibration_reading_is_an_alarm()
    {
        (await AlarmAsync(Verdict("Fires"), Thursday)).ShouldBe(true);
    }

    [Fact]
    public async Task An_unavailable_calibration_reading_is_unavailable_not_a_pass()
    {
        (await AlarmAsync(Verdict("Unavailable"), Thursday)).ShouldBeNull();
    }

    [Fact]
    public async Task A_row_older_than_the_last_closed_session_is_unavailable()
    {
        (await AlarmAsync(Verdict("Quiet"), new DateOnly(2026, 11, 3))).ShouldBeNull();
    }

    [Fact]
    public async Task A_row_that_cannot_be_read_is_unavailable()
    {
        (await AlarmAsync("not json", Thursday)).ShouldBeNull();
    }

    [Fact]
    public async Task With_no_monitor_row_and_no_scores_everything_is_unavailable_and_the_hold_out_is_always_unavailable()
    {
        var evidence = Evidence();
        _checks.LatestAsync(17, Ct).Returns((StoredMlMonitorCheck?)null);

        var read = await evidence.ReadAsync(Model, Ct);

        read.ShouldBe(MlInfluenceEvidence.None);
    }

    [Fact]
    public async Task The_forward_figures_come_from_the_models_graded_scores()
    {
        var evidence = Evidence();
        _checks.LatestAsync(17, Ct).Returns((StoredMlMonitorCheck?)null);
        _scores.OutcomesAsync(17, Ct).Returns((IReadOnlyList<ScoredOutcome>)[new ScoredOutcome(new DateOnly(2026, 10, 1), "a", 0.8, 1), new ScoredOutcome(new DateOnly(2026, 10, 8), "b", 0.2, -1)]);

        var read = await evidence.ReadAsync(Model, Ct);

        read.Forward!.GradedCandidates.ShouldBe(2);
        read.Forward.Weeks.ShouldBe(1);
        read.Forward.TopMinusBottomR.ShouldBe(2);
        read.HoldOut.ShouldBeNull();
    }

    [Fact]
    public async Task The_rollback_evidence_turns_the_champions_scores_into_calibration_observations_and_none_into_unavailable()
    {
        var evidence = new ShadowRollbackEvidence(_scores);
        _scores.OutcomesAsync(17, Ct).Returns((IReadOnlyList<ScoredOutcome>)[]);
        (await evidence.ReadAsync(Model, Ct)).Calibration.ShouldBeNull();

        _scores.OutcomesAsync(17, Ct).Returns((IReadOnlyList<ScoredOutcome>)[new ScoredOutcome(Thursday, "a", 0.7, 2), new ScoredOutcome(Thursday, "b", 0.4, -1)]);
        var inputs = await evidence.ReadAsync(Model, Ct);

        inputs.Calibration!.Select(o => (o.Predicted, o.Label)).ShouldBe([(0.7, 1), (0.4, 0)]);
        inputs.Cells.ShouldBeNull();
    }
}
