#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Confidence;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>CF-12 part 12a (ADR 0218) over a fake evidence port: each present section reaches its calculator, each missing one says unavailable and why, and the shipped source says nothing is available.</summary>
public sealed class GetLoopReportHandlerTests
{
    private static readonly DateOnly Start = new(2026, 3, 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LoopEvidence None => new(null, "no arms", null, "no curve", null, "no outcomes", null, "no lift");

    private static async Task<LoopReportView> RunAsync(LoopEvidence evidence, LoopOptions? options = null)
    {
        var port = Substitute.For<ILoopEvidence>();
        port.ReadAsync(Ct).Returns(ValueTask.FromResult(evidence));
        var result = await new GetLoopReportHandler(port, options ?? new LoopOptions()).HandleAsync(new GetLoopReportQuery(), Ct);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    [Fact]
    public async Task With_no_evidence_every_section_is_unavailable_with_its_reason()
    {
        var view = await RunAsync(None);

        view.Arms.State.ShouldBe("unavailable");
        view.Arms.Reason.ShouldBe("no arms");
        view.Curve.State.ShouldBe("unavailable");
        view.Curve.Text.ShouldBe("unavailable: no curve");
        view.Slope.Verdict.ShouldBe("unavailable");
        view.Lift.State.ShouldBe("unavailable");
        view.Lift.Text.ShouldContain("no lift");
        view.Statements.ShouldContain(s => s.Contains("unavailable is never read as fine", StringComparison.Ordinal));
        view.Line.ShouldContain("report only");
        view.Settings.SettingsHash.Length.ShouldBe(64);
    }

    [Fact]
    public async Task The_shipped_source_says_nothing_is_available_and_invents_nothing()
    {
        var evidence = await new NoLoopEvidence().ReadAsync(Ct);

        (evidence.Arms, evidence.Curve, evidence.Outcomes, evidence.Lift).ShouldBe((null, null, null, null));
        new[] { evidence.ArmsReason, evidence.CurveReason, evidence.OutcomesReason, evidence.LiftReason }.ShouldAllBe(r => !string.IsNullOrWhiteSpace(r));
    }

    [Fact]
    public async Task Present_arms_reach_the_three_arm_test_with_the_settings()
    {
        var candidates = Enumerable.Range(0, 30).SelectMany(d => new[]
        {
            new LoopCandidate($"w{d}", Start.AddDays(d), 1, 0.9, 0.8), new LoopCandidate($"l{d}", Start.AddDays(d), -1, 0.9, 0.2),
        }).ToList();

        var view = await RunAsync(None with { Arms = new LoopArmsInput(candidates, 0.5, 0.5, "s") });

        view.Arms.State.ShouldBe("available");
        view.Arms.Candidates.ShouldBe(60);
        view.Arms.RetrainedVsNoMl!.Verdict.ShouldBe("helps");
        view.Arms.RetrainedVsFrozen!.Verdict.ShouldBe("helps");
        view.Arms.RetrainedMean!.Value.ShouldBe(0.5, 1e-12);
        view.Line.ShouldContain("retrained vs frozen helps");
    }

    [Fact]
    public async Task Few_days_read_inconclusive_through_the_handler()
    {
        var candidates = Enumerable.Range(0, 5).Select(d => new LoopCandidate("c" + d, Start.AddDays(d), 1, 0.9, 0.9)).ToList();

        var view = await RunAsync(None with { Arms = new LoopArmsInput(candidates, 0.5, 0.5, "s") });

        view.Arms.RetrainedVsNoMl!.Verdict.ShouldBe("inconclusive");
    }

    [Fact]
    public async Task A_present_curve_reaches_the_learning_curve()
    {
        var view = await RunAsync(None with { Curve = [new LearningCurvePoint(50, -0.004), new LearningCurvePoint(100, 0.001), new LearningCurvePoint(150, 0.005)] });

        view.Curve.State.ShouldBe("available");
        view.Curve.Verdict.ShouldBe("unclear");
        view.Curve.SlopePer50Rows!.Value.ShouldBe(0.0045, 1e-12);
        view.Curve.MoreRows!.Value.ShouldBe(53.7, 0.05);
    }

    [Fact]
    public async Task Present_outcomes_reach_the_reliability_slope()
    {
        var outcomes = new List<CalibrationObservation>();
        for (var i = 0; i < 100; i++)
        {
            outcomes.Add(new CalibrationObservation("a" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), i, 0.2, i < 35 ? 1 : 0));
            outcomes.Add(new CalibrationObservation("b" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), 1000 + i, 0.8, i < 65 ? 1 : 0));
        }

        var view = await RunAsync(None with { Outcomes = outcomes });

        view.Slope.Verdict.ShouldBe("alarm");
        view.Slope.Slope!.Value.ShouldBe(0.4465, 1e-4);
        view.Slope.Outcomes.ShouldBe(200);
    }

    [Fact]
    public async Task Present_lift_candidates_reach_the_decision_lift()
    {
        var candidates = new[]
        {
            new LiftCandidate("a", Start, 0.9, 1.1, true), new LiftCandidate("b", Start, 0.8, 0.9, true), new LiftCandidate("c", Start, 0.2, -0.9, false), new LiftCandidate("d", Start, 0.1, -1.1, false),
            new LiftCandidate("e", Start.AddDays(1), 0.9, 1.0, true), new LiftCandidate("f", Start.AddDays(1), 0.1, -1.0, false),
        };

        var view = await RunAsync(None with { Lift = candidates });

        view.Lift.State.ShouldBe("available");
        (view.Lift.TopTrades, view.Lift.BottomTrades).ShouldBe((3, 3));
        view.Lift.Lift!.Value.ShouldBeGreaterThan(1.9);
        (view.Lift.ApprovedTickets, view.Lift.ApprovedInTop).ShouldBe((3, 3));
    }
}
