using XDrishti.Domain.LiveTrading;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-06: each criterion at its boundary, the overall combinations, the malformed-window guard, caveats and the recommendation sentence.</summary>
public sealed class LivePhaseCheckTests
{
    private static readonly DateOnly Start = new(2026, 1, 5);
    private static readonly Dictionary<string, int> NoOutcomes = [];

    private static List<LiveSessionFact> Sessions(int n, double scale = 0.25, double factor = 0.25, string capture = CaptureHealth.Healthy)
    {
        var list = new List<LiveSessionFact>();
        for (var d = Start; list.Count < n; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            list.Add(new LiveSessionFact(d, 1, 1, capture, [scale], factor, 1, NoOutcomes, 1, 0, 0, 0));
        }

        return list;
    }

    private static LivePhaseFacts Good(IReadOnlyList<LiveSessionFact>? sessions = null)
    {
        var s = sessions ?? Sessions(40);
        var slip = Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? 0.01 : -0.01).ToList();
        return new LivePhaseFacts
        {
            Through = Start.AddDays(56),
            WindowStart = Start,
            Sessions = s,
            GradedRoundTrips = 40,
            Pairs = new LivePairFacts(30, NonInferiority.Within, 0.01, -0.02, 0.04, 0.035, 0, 0, true),
            SlippageR = slip,
            SlippageKind = ReferenceKind.TicketLevel,
            WeeksWithEstimates = 4,
            CapacityState = "value",
            Compliance = [.. ComplianceKinds.All.Select(k => new LiveComplianceFact(k, Start, Start.AddDays(300)))],
            CaptureRan = true,
            AllowList = new LiveAllowListFact(true, "pinned"),
        };
    }

    private static string Verdict(LivePhaseReport r, string code) => r.Criteria.Single(c => c.Code == code).Verdict;

    [Fact]
    public void A_full_good_window_is_met_with_stacked_caveats_and_the_recommendation()
    {
        var r = LivePhaseCheck.Evaluate(Good());
        r.ExitVerdict.ShouldBe("MET");
        r.ExitCriterion.ShouldContain("one-sided bound 0.035 R from 30 pairs");
        r.ExitCriterion.ShouldContain("all live trades at S1");
        r.Recommendation.ShouldContain("step S3");
    }

    [Theory]
    [InlineData(39, "NOT EVALUABLE")]
    [InlineData(40, "MET")]
    public void Sessions_floor(int sessions, string expected) =>
        Verdict(LivePhaseCheck.Evaluate(Good(Sessions(sessions))), "C1").ShouldBe(expected);

    [Fact]
    public void Days_floor_and_coverage()
    {
        Verdict(LivePhaseCheck.Evaluate(Good() with { Through = Start.AddDays(54) }), "C1").ShouldBe("NOT EVALUABLE");
        var s = Sessions(40);
        for (var i = 0; i < 5; i++)
        {
            s[i] = s[i] with { Capture = CaptureHealth.Gap };
        }

        Verdict(LivePhaseCheck.Evaluate(Good(s)), "C1").ShouldBe("NOT MET"); // 35/40 = 0.875
        s[4] = s[4] with { Capture = CaptureHealth.Healthy };
        Verdict(LivePhaseCheck.Evaluate(Good(s)), "C1").ShouldBe("MET"); // 36/40 = 0.90
    }

    [Fact]
    public void Risk_band_rows()
    {
        Verdict(LivePhaseCheck.Evaluate(Good(Sessions(40, 0.5, 0.5))), "C2").ShouldBe("MET");
        Verdict(LivePhaseCheck.Evaluate(Good(Sessions(40, 0.51, 1.0))), "C2").ShouldBe("NOT MET");
        Verdict(LivePhaseCheck.Evaluate(Good(Sessions(40, 0.125, 0.25))), "C2").ShouldBe("MET");
        var few = Sessions(40).Select((s, i) => i < 19 ? s : s with { Scales = [] }).ToList();
        Verdict(LivePhaseCheck.Evaluate(Good(few)), "C2").ShouldBe("NOT EVALUABLE");
        var twenty = Sessions(40).Select((s, i) => i < 20 ? s : s with { Scales = [] }).ToList();
        Verdict(LivePhaseCheck.Evaluate(Good(twenty)), "C2").ShouldBe("MET");
        twenty[3] = twenty[3] with { Scales = [0.3] };
        Verdict(LivePhaseCheck.Evaluate(Good(twenty)), "C2").ShouldBe("NOT MET");
    }

    [Fact]
    public void Volume_and_within_follow_the_floors_and_the_verdict()
    {
        Verdict(LivePhaseCheck.Evaluate(Good() with { GradedRoundTrips = 39 }), "C3").ShouldBe("NOT EVALUABLE");
        Verdict(LivePhaseCheck.Evaluate(Good() with { Pairs = Good().Pairs! with { Pairs = 29 } }), "C3").ShouldBe("NOT EVALUABLE");
        Verdict(LivePhaseCheck.Evaluate(Good() with { Pairs = null }), "C4").ShouldBe("NOT EVALUABLE");
        Verdict(LivePhaseCheck.Evaluate(Good() with { Pairs = Good().Pairs! with { Verdict = NonInferiority.NotWithin } }), "C4").ShouldBe("NOT MET");
        Verdict(LivePhaseCheck.Evaluate(Good() with { Pairs = Good().Pairs! with { Verdict = NonInferiority.Inconclusive } }), "C4").ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public void Slippage_needs_twenty_and_judges_the_bounds()
    {
        Verdict(LivePhaseCheck.Evaluate(Good() with { SlippageR = Good().SlippageR.Take(19).ToList() }), "C5").ShouldBe("NOT EVALUABLE");
        Verdict(LivePhaseCheck.Evaluate(Good() with { SlippageR = [.. Enumerable.Range(0, 20).Select(i => 0.5 + (i % 2 * 0.01))] }), "C5").ShouldBe("NOT MET");
        Verdict(LivePhaseCheck.Evaluate(Good() with { SlippageR = [.. Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? 0.3 : -0.2)] }), "C5").ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public void Discipline_hold_step_down_and_evidence()
    {
        Verdict(LivePhaseCheck.Evaluate(Good() with { HoldActive = true }), "C6").ShouldBe("NOT MET");
        Verdict(LivePhaseCheck.Evaluate(Good() with { StepDownSessionsAgo = [19] }), "C6").ShouldBe("NOT MET");
        Verdict(LivePhaseCheck.Evaluate(Good() with { StepDownSessionsAgo = [21] }), "C6").ShouldBe("MET");
        Verdict(LivePhaseCheck.Evaluate(Good() with { AfterHaltUncleared = 1 }), "C6").ShouldBe("NOT MET");
        Verdict(LivePhaseCheck.Evaluate(Good() with { IntradayEntries = 10, OrderEvidenceUnavailable = 3 }), "C6").ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public void Feedback_and_compliance_and_integrity()
    {
        Verdict(LivePhaseCheck.Evaluate(Good() with { WeeksWithEstimates = 3 }), "C7").ShouldBe("NOT EVALUABLE");
        Verdict(LivePhaseCheck.Evaluate(Good() with { CapacityState = "empty" }), "C7").ShouldBe("NOT EVALUABLE");
        Verdict(LivePhaseCheck.Evaluate(Good() with { Compliance = [] }), "C8").ShouldBe("NOT MET");
        Verdict(LivePhaseCheck.Evaluate(Good() with { Compliance = [], Through = Start.AddDays(54) }), "C8").ShouldBe("NOT EVALUABLE");
        Verdict(LivePhaseCheck.Evaluate(Good() with { Disagreements = 1 }), "I").ShouldBe("NOT MET");
        Verdict(LivePhaseCheck.Evaluate(Good() with { AllowList = null }), "I").ShouldBe("NOT EVALUABLE");
        Verdict(LivePhaseCheck.Evaluate(Good() with { CaptureRan = false }), "I").ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public void Overall_not_met_dominates_and_a_malformed_window_is_never_met()
    {
        var notMet = LivePhaseCheck.Evaluate(Good() with { HoldActive = true, Pairs = null });
        notMet.ExitVerdict.ShouldBe("NOT MET");
        notMet.Recommendation.ShouldNotContain("S3");
        var malformed = LivePhaseCheck.Evaluate(Good() with { OneLiveAccount = false });
        malformed.ExitVerdict.ShouldBe("NOT EVALUABLE");
        malformed.ExitCriterion.ShouldContain("malformed window");
        LivePhaseCheck.Evaluate(Good() with { Pairs = Good().Pairs! with { PairsDistinct = false } }).ExitVerdict.ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public void An_empty_window_is_not_evaluable_and_text_is_deterministic()
    {
        var r = LivePhaseCheck.Evaluate(new LivePhaseFacts { Through = Start });
        r.ExitVerdict.ShouldBe("NOT EVALUABLE");
        LivePhaseText.Render(r).ShouldBe(LivePhaseText.Render(r));
    }
}
