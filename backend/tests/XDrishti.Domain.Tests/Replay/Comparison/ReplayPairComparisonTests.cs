using XDrishti.Domain.Replay.Comparison;

namespace XDrishti.Domain.Tests.Replay.Comparison;

public sealed class ReplayPairComparisonTests
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
    public void Learning_that_beats_frozen_every_day_is_learning_better()
    {
        var r = ReplayPairComparison.Compare(Curve(30, Base), Curve(30, i => Base(i) + 0.002m + ((i % 3) * 0.0001m)), 1_000_000m);
        r.IsSuccess.ShouldBeTrue();
        r.Value.Answer.ShouldBe(PairAnswer.LearningBetter);
        r.Value.AnswerCode.ShouldBe("learning_better");
        r.Value.Days.ShouldBe(30);
        r.Value.MeanDailyDifference.ShouldBeGreaterThan(0);
        r.Value.ProbabilityLearningBetter!.Value.ShouldBeGreaterThanOrEqualTo(r.Value.Cut);
    }

    [Fact]
    public void Frozen_that_beats_learning_every_day_is_frozen_better()
    {
        var r = ReplayPairComparison.Compare(Curve(30, i => Base(i) + 0.002m + ((i % 3) * 0.0001m)), Curve(30, Base), 1_000_000m);
        r.Value.Answer.ShouldBe(PairAnswer.FrozenBetter);
        r.Value.MeanDailyDifference.ShouldBeLessThan(0);
    }

    [Fact]
    public void Alternating_differences_that_cancel_are_no_measurable_difference()
    {
        var r = ReplayPairComparison.Compare(Curve(30, Base), Curve(30, i => Base(i) + (i % 2 == 0 ? 0.002m : -0.002m)), 1_000_000m);
        r.Value.Answer.ShouldBe(PairAnswer.NoMeasurableDifference);
        r.Value.ProbabilityLearningBetter.ShouldNotBeNull();
    }

    [Fact]
    public void Fewer_days_than_the_bootstrap_minimum_is_too_little_history_but_still_shows_n()
    {
        var r = ReplayPairComparison.Compare(Curve(10, Base), Curve(10, i => Base(i) + 0.002m), 1_000_000m);
        r.Value.Answer.ShouldBe(PairAnswer.TooLittleHistory);
        r.Value.Days.ShouldBe(10);
        r.Value.MinDays.ShouldBe(20);
        r.Value.ProbabilityLearningBetter.ShouldBeNull();
    }

    [Fact]
    public void Unequal_or_misaligned_sessions_are_refused_without_an_exception()
    {
        var a = Curve(25, Base);
        ReplayPairComparison.Compare(a, Curve(24, Base), 1_000_000m).Error.Code.ShouldBe(ReplayPairComparison.MisalignedCode);
        var shifted = a.Select(p => p with { Session = p.Session.AddDays(1) }).ToList();
        ReplayPairComparison.Compare(a, shifted, 1_000_000m).Error.Code.ShouldBe(ReplayPairComparison.MisalignedCode);
        ReplayPairComparison.Compare([], [], 1_000_000m).IsFailure.ShouldBeTrue();
        ReplayPairComparison.Compare(a, a, 0m).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_same_inputs_give_the_same_answer()
    {
        var f = Curve(40, Base);
        var l = Curve(40, i => Base(i) + (i % 2 == 0 ? 0.0015m : -0.001m));
        ReplayPairComparison.Compare(f, l, 1_000_000m).Value.ShouldBe(ReplayPairComparison.Compare(f, l, 1_000_000m).Value);
    }
}
