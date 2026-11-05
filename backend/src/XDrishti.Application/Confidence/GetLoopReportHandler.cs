using System.Globalization;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Confidence;

/// <summary>
/// "Is the learning loop helping?" (spec CF-12 D4 to D7, part 12a, ADR 0218). It asks the one evidence port for the inputs and hands each present section to its pure calculator (<see cref="ThreeArmLoopTest"/>, <see cref="LearningCurve"/>, <see cref="ReliabilitySlope"/>,
/// <see cref="DecisionLift"/>); a section with no input says <c>unavailable</c> and why. Derived on every request, nothing stored, nothing changed, and nothing that chooses what to approve may use it (an architecture test).
/// </summary>
internal sealed class GetLoopReportHandler(ILoopEvidence evidence, LoopOptions options) : IQueryHandler<GetLoopReportQuery, Result<LoopReportView>>
{
    public async ValueTask<Result<LoopReportView>> HandleAsync(GetLoopReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var read = await evidence.ReadAsync(cancellationToken);
        var settings = new LoopSettingsView(options.MinDays, options.Draws, options.Level, options.HelpsAt, options.MinSlopePer50Rows, options.TargetGain, LoopOptions.SlopeWindow, options.SlopeLow, options.SlopeHigh, options.SlopeSe, options.SnapshotHash(), options.KeyText());
        var view = new LoopReportView(settings, Arms(read), Curve(read), Slope(read), Lift(read), Statements(options), string.Empty);
        return Result.Success(view with { Line = Line(view) });
    }

    private LoopArmsView Arms(LoopEvidence read)
    {
        if (read.Arms is not { } input)
        {
            var reason = read.ArmsReason ?? "no input";
            return new LoopArmsView("unavailable", reason, 0, null, null, null, null, null);
        }

        var r = ThreeArmLoopTest.Evaluate(input.Candidates, input.FrozenBaseRate, input.RetrainedBaseRate, options, input.SeedKey);
        return new LoopArmsView("available", null, r.Candidates, r.FrozenMean, r.RetrainedMean, r.NoMlMean, Arm(r.RetrainedVsFrozen), Arm(r.RetrainedVsNoMl));
    }

    private static LoopArmView Arm(ArmComparison a) => new(a.Name, a.Verdict switch
    {
        LoopVerdict.Helps => "helps",
        LoopVerdict.Hurts => "hurts",
        LoopVerdict.NoMeasurableDifference => "no_measurable_difference",
        LoopVerdict.Inconclusive => "inconclusive",
        _ => "unavailable",
    }, a.Candidates, a.Days, a.MeanDifference, a.Low, a.High, a.ProbabilityBetter, a.ProbabilityWorse, a.Text);

    private LoopCurveView Curve(LoopEvidence read)
    {
        if (read.Curve is not { } points)
        {
            var reason = read.CurveReason ?? "no input";
            return new LoopCurveView("unavailable", reason, null, 0, null, null, null, null, "unavailable: " + reason);
        }

        var r = LearningCurve.Evaluate(points, options);
        return new LoopCurveView("available", null, r.Verdict.ToString().ToLowerInvariant(), r.PointsUsed, r.SlopePer50Rows, r.StandardError is { } se ? se * 50 : null, r.ProjectedRows, r.MoreRows, r.Text);
    }

    private LoopSlopeView Slope(LoopEvidence read)
    {
        if (read.Outcomes is not { } outcomes)
        {
            var reason = read.OutcomesReason ?? "no input";
            return new LoopSlopeView("unavailable", reason, "unavailable", 0, null, null, null, "unavailable: " + reason);
        }

        var r = ReliabilitySlope.Evaluate(outcomes, options);
        var verdict = r.State switch
        {
            ReliabilityState.Alarm => "alarm",
            ReliabilityState.Ok => "ok",
            ReliabilityState.ShownOnly => "shown_only",
            _ => "unavailable",
        };
        return new LoopSlopeView("available", null, verdict, r.Outcomes, r.Slope, r.Intercept, r.StandardError, r.Text);
    }

    private static LoopLiftView Lift(LoopEvidence read)
    {
        if (read.Lift is not { } candidates)
        {
            var reason = read.LiftReason ?? "no input";
            return new LoopLiftView("unavailable", reason, 0, 0, null, null, null, 0, 0, null, "unavailable: " + reason);
        }

        var r = DecisionLift.Compute(candidates);
        return new LoopLiftView("available", null, r.TopTrades, r.BottomTrades, r.Lift, r.StandardError, r.LiftZ, r.ApprovedTickets, r.ApprovedInTop, r.ApprovedZ, r.Text);
    }

    private static List<string> Statements(LoopOptions o) =>
    [
        "The three arms pick the same candidates (ML is advisory) and differ only in the probability and the take rule: the frozen champion of the epoch's start (A), the champion now (B) and no model, taking every candidate (C). A candidate an arm skips is worth 0 to that arm.",
        string.Create(CultureInfo.InvariantCulture, $"Differences are summed by trade day and resampled by day ({o.Draws} seeded draws). The first arm helps at P(better) of {o.HelpsAt:0.00##} or more and hurts when P(worse) is {LoopOptions.HurtsBar:0.00##} or more; fewer than {o.MinDays} distinct days is inconclusive. No measurable difference is an answer."),
        string.Create(CultureInfo.InvariantCulture, $"The reliability slope never alarms with fewer than {LoopOptions.SlopeWindow} outcomes. The learning curve needs the refit loop. The lift describes how the owner's approvals line up with the score; it feeds nothing."),
        "Report only: this changes no ranking, size, ticket, state or setting and places no order. All thresholds are proposals of the spec. A section whose input has no source yet says unavailable and why; unavailable is never read as fine.",
    ];

    private static string Line(LoopReportView v)
    {
        string Arm(LoopArmView? a) => a is null ? "unavailable" : a.Verdict;
        return string.Create(CultureInfo.InvariantCulture, $"learning loop (report only): retrained vs frozen {Arm(v.Arms.RetrainedVsFrozen)}, retrained vs no model {Arm(v.Arms.RetrainedVsNoMl)}; learning curve {v.Curve.Verdict ?? "unavailable"}; reliability slope {v.Slope.Verdict ?? "unavailable"}; lift {v.Lift.State}; settings {v.Settings.SettingsHash[..12]}");
    }
}
