using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-05 criterion 5: the fill-one-minute-later test per signal, by hand.</summary>
public sealed class DelayStressTests
{
    // 100 signals, the base fills all of them for a total of 20 R (mean 0.20); the delayed run fills 90 of them for the given total
    private static List<(double BaseR, double? DelayedR)> Signals(double delayedTotal)
    {
        var list = new List<(double, double?)>();
        for (var i = 0; i < 100; i++)
        {
            list.Add((0.20, i < 90 ? delayedTotal / 90 : null));
        }

        return list;
    }

    [Fact]
    public void Ninety_fills_with_sixteen_r_keep_eighty_percent_per_signal_and_pass()
    {
        var r = DelayStress.Evaluate(Signals(16), 0.70, costsChecked: true);

        r.Signals.ShouldBe(100);
        r.DelayedFilled.ShouldBe(90);
        r.BaseMeanR!.Value.ShouldBe(0.20, 1e-12);
        r.DelayedMeanR!.Value.ShouldBe(0.16, 1e-12);
        r.Retention!.Value.ShouldBe(0.80, 1e-12);
        r.Status.ShouldBe(StressStatus.Pass);
        // per fill it would read 0.178 (89 %), which the report must not use
        (16.0 / 90 / 0.20).ShouldBe(0.8889, 1e-3);
        r.Retention.ShouldNotBe(16.0 / 90 / 0.20);
    }

    [Fact]
    public void A_delayed_total_of_thirteen_r_keeps_sixty_five_percent_and_fails()
    {
        var r = DelayStress.Evaluate(Signals(13), 0.70, true);

        r.Retention!.Value.ShouldBe(0.65, 1e-12);
        r.Status.ShouldBe(StressStatus.Fail);
    }

    [Fact]
    public void Exactly_the_required_share_passes()
    {
        var r = DelayStress.Evaluate(Signals(14), 0.70, true);

        r.Retention!.Value.ShouldBe(0.70, 1e-12);
        r.Status.ShouldBe(StressStatus.Pass);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.10)]
    public void A_base_of_zero_or_less_is_not_evaluable_and_not_a_pass(double baseR)
    {
        var r = DelayStress.Evaluate([(baseR, baseR), (baseR, null)], 0.70, true);

        r.Status.ShouldBe(StressStatus.Unavailable);
        r.Retention.ShouldBeNull();
        r.Reason.ShouldContain("not evaluable");
    }

    [Fact]
    public void No_signal_or_a_number_that_is_not_finite_is_not_evaluable()
    {
        DelayStress.Evaluate([], 0.70, true).Status.ShouldBe(StressStatus.Unavailable);
        DelayStress.Evaluate([(double.NaN, 0.1)], 0.70, true).Status.ShouldBe(StressStatus.Unavailable);
        DelayStress.Evaluate([(0.2, double.PositiveInfinity)], 0.70, true).Status.ShouldBe(StressStatus.Unavailable);
    }

    [Fact]
    public void An_unchecked_sheet_reads_unavailable_with_the_label_and_keeps_the_figures()
    {
        var r = DelayStress.Evaluate(Signals(16), 0.70, costsChecked: false);

        r.Status.ShouldBe(StressStatus.Unavailable);
        r.Reason.ShouldContain("costs not checked against a bill");
        r.Retention!.Value.ShouldBe(0.80, 1e-12);
    }

    [Fact]
    public void A_signal_that_no_longer_fills_counts_as_zero_r_so_a_missed_runner_is_not_hidden()
    {
        // two signals: the one that ran (+3) is missed by the delayed run, the other (+0.5) still fills at +0.5
        var r = DelayStress.Evaluate([(3.0, null), (0.5, 0.5)], 0.70, true);

        r.BaseMeanR!.Value.ShouldBe(1.75, 1e-12);
        r.DelayedMeanR!.Value.ShouldBe(0.25, 1e-12);
        r.Status.ShouldBe(StressStatus.Fail);
    }
}
