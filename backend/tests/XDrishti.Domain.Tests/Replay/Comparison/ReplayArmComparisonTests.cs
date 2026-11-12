using XDrishti.Domain.Replay.Comparison;

namespace XDrishti.Domain.Tests.Replay.Comparison;

/// <summary>SR-02, AC 3: the selection run against the no-selection baseline gives the four answers of the one paired day bootstrap.</summary>
public sealed class ReplayArmComparisonTests
{
    private static readonly DateOnly Start = new(2026, 10, 5);

    private static List<ReplayDayEquity> Curve(int n, Func<int, decimal> dailyReturn)
    {
        var list = new List<ReplayDayEquity>();
        var equity = 1_000_000m;
        for (var i = 0; i < n; i++)
        {
            equity *= 1 + dailyReturn(i);
            list.Add(new ReplayDayEquity(Start.AddDays(i), equity));
        }

        return list;
    }

    private static decimal Base(int i) => ((i % 5) - 2) * 0.001m;

    [Fact]
    public void A_selection_that_beats_the_baseline_every_day_is_selection_better()
    {
        var r = ReplayPairComparison.CompareArms(Curve(30, i => Base(i) + 0.002m + ((i % 3) * 0.0001m)), Curve(30, Base), 1_000_000m);

        r.IsSuccess.ShouldBeTrue();
        r.Value.Answer.ShouldBe(ArmAnswer.SelectionBetter);
        r.Value.AnswerCode.ShouldBe("selection_better");
        r.Value.MeanDailyDifference.ShouldBeGreaterThan(0);
        r.Value.ProbabilitySelectionBetter!.Value.ShouldBeGreaterThanOrEqualTo(r.Value.Cut);
    }

    [Fact]
    public void A_baseline_that_beats_the_selection_every_day_is_baseline_better()
    {
        var r = ReplayPairComparison.CompareArms(Curve(30, Base), Curve(30, i => Base(i) + 0.002m + ((i % 3) * 0.0001m)), 1_000_000m);

        r.Value.Answer.ShouldBe(ArmAnswer.BaselineBetter);
        r.Value.AnswerCode.ShouldBe("baseline_better");
        r.Value.MeanDailyDifference.ShouldBeLessThan(0);
    }

    [Fact]
    public void Differences_that_cancel_are_no_measurable_difference_with_the_probabilities_shown()
    {
        var r = ReplayPairComparison.CompareArms(Curve(30, i => Base(i) + (i % 2 == 0 ? 0.002m : -0.002m)), Curve(30, Base), 1_000_000m);

        r.Value.Answer.ShouldBe(ArmAnswer.NoMeasurableDifference);
        r.Value.AnswerCode.ShouldBe("no_measurable_difference");
        r.Value.ProbabilitySelectionBetter.ShouldNotBeNull();
    }

    [Fact]
    public void Fifteen_informative_days_are_too_little_history_with_n_shown_and_no_probability()
    {
        var r = ReplayPairComparison.CompareArms(Curve(40, i => i < 15 ? 0.01m : 0m), Curve(40, _ => 0m), 1_000_000m);

        r.Value.Answer.ShouldBe(ArmAnswer.TooLittleHistory);
        (r.Value.Days, r.Value.InformativeDays).ShouldBe((40, 15));
        r.Value.ProbabilitySelectionBetter.ShouldBeNull();
        r.Value.AnswerCode.ShouldBe("too_little_history");
    }

    [Fact]
    public void Misaligned_series_are_refused_without_an_exception()
    {
        ReplayPairComparison.CompareArms(Curve(25, Base), Curve(24, Base), 1_000_000m).Error.Code.ShouldBe(ReplayPairComparison.MisalignedCode);
    }
}
