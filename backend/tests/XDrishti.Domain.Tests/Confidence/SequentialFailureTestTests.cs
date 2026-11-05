using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-09 (ADR 0213), criteria 1 to 9 and 12: the step values, the boundaries, the hand-worked series, the reflection, the design effect, the sigma floor, order independence of a good series, never a pass, and determinism.</summary>
public sealed class SequentialFailureTestTests
{
    private static readonly SequentialOptions Defaults = new();

    private static SequentialTrade[] Repeat(double r, int count, int m = 1) => [.. Enumerable.Repeat(new SequentialTrade(r, m), count)];

    private static SequentialResult Run(IEnumerable<SequentialTrade> series, double rho = 0.3) => SequentialFailureTest.Run([.. series], Defaults, rho);

    [Theory]
    [InlineData(-1, -0.097778)]
    [InlineData(2, 0.168889)]
    [InlineData(0, -0.008889)]
    [InlineData(0.5, 0.035556)]
    public void The_step_values_are_the_specs_to_six_places(double r, double expected)
    {
        SequentialFailureTest.Llr(r, 0.2, 1.5).ShouldBe(expected, 5e-7);
    }

    [Fact]
    public void The_boundaries_are_recomputed_from_the_settings()
    {
        Defaults.CautionBoundary.ShouldBe(-1.558145, 5e-7);
        Defaults.FailureBoundary.ShouldBe(-2.944439, 5e-7);
        Defaults.OnTrackLevel.ShouldBe(2.772589, 5e-7);
        Defaults.CautionOdds.ShouldBe(4.75, 1e-9);
        Defaults.FailureOdds.ShouldBe(19, 1e-9);
    }

    [Fact]
    public void Straight_losses_cross_caution_at_the_16th_and_failure_at_the_31st()
    {
        var at15 = Run(Repeat(-1, 15));
        var at16 = Run(Repeat(-1, 16));
        var at30 = Run(Repeat(-1, 30));
        var at31 = Run(Repeat(-1, 31));

        (at15.Sum, at15.State).ShouldBe((-1.466667, SequentialState.Monitoring), new TupleTolerance());
        at16.State.ShouldBe(SequentialState.Caution);
        at16.Sum.ShouldBe(-1.564444, 5e-7);
        at30.State.ShouldBe(SequentialState.Caution);
        at30.Sum.ShouldBe(-2.933333, 5e-7);
        at31.State.ShouldBe(SequentialState.Failure);
        at31.Sum.ShouldBe(-3.031111, 5e-7);
        (at31.FirstCautionTrade, at31.FirstFailureTrade).ShouldBe((16, 31));
    }

    [Fact]
    public void A_mixed_series_of_7_wins_then_losses_cautions_at_trade_36_and_fails_at_trade_50()
    {
        SequentialTrade[] Series(int losses) => [.. Repeat(2, 7), .. Repeat(-1, losses)];

        Run(Repeat(2, 7)).Sum.ShouldBe(1.182222, 5e-7);
        Run(Series(28)).Sum.ShouldBe(-1.555556, 5e-7);
        Run(Series(28)).State.ShouldBe(SequentialState.Monitoring);
        Run(Series(29)).Sum.ShouldBe(-1.653333, 5e-7);
        Run(Series(29)).FirstCautionTrade.ShouldBe(36);
        Run(Series(42)).Sum.ShouldBe(-2.924444, 5e-7);
        Run(Series(42)).State.ShouldBe(SequentialState.Caution);
        Run(Series(43)).Sum.ShouldBe(-3.022222, 5e-7);
        Run(Series(43)).FirstFailureTrade.ShouldBe(50);
    }

    [Fact]
    public void A_good_series_never_cautions_in_any_order_and_ends_at_the_same_sum()
    {
        // 8 wins of +2 R and 12 losses of -1 R: final +0.177778; the worst order (all losses first) bottoms at -1.173333, above the caution boundary
        var worst = Run([.. Repeat(-1, 12), .. Repeat(2, 8)]);
        worst.LowestSum.ShouldBe(-1.173333, 5e-7);
        worst.State.ShouldBe(SequentialState.Monitoring);

        var random = new Random(20261105);
        for (var round = 0; round < 200; round++)
        {
            var shuffled = Repeat(2, 8).Concat(Repeat(-1, 12)).OrderBy(_ => random.Next()).ToArray();
            var result = Run(shuffled);

            result.Sum.ShouldBe(0.177778, 5e-7);
            result.FirstCautionTrade.ShouldBeNull();
            result.LowestSum.ShouldBeGreaterThanOrEqualTo(-1.173333 - 5e-7);
        }
    }

    [Fact]
    public void Four_hundred_wins_stick_at_the_on_track_level_and_the_state_text_makes_no_claim_of_a_pass()
    {
        var result = Run(Repeat(2, 400));

        result.Sum.ShouldBe(Defaults.OnTrackLevel, 1e-12);
        result.State.ShouldBe(SequentialState.OnTrack);
        foreach (var state in Enum.GetValues<SequentialState>())
        {
            SequentialWording.StateText(state).ShouldNotContain("pass", Case.Insensitive);
            SequentialWording.StateText(state).ShouldNotContain("proven", Case.Insensitive);
            SequentialWording.StateText(state).ShouldNotContain("ready", Case.Insensitive);
        }
    }

    [Fact]
    public void After_sitting_at_the_cap_45_losses_reach_caution_and_59_reach_failure()
    {
        var cap = Repeat(2, 100);

        var at44 = Run([.. cap, .. Repeat(-1, 44)]);
        var at45 = Run([.. cap, .. Repeat(-1, 45)]);
        var at58 = Run([.. cap, .. Repeat(-1, 58)]);
        var at59 = Run([.. cap, .. Repeat(-1, 59)]);

        at44.State.ShouldBe(SequentialState.Monitoring);
        at45.State.ShouldBe(SequentialState.Caution);
        at58.State.ShouldBe(SequentialState.Caution);
        at59.State.ShouldBe(SequentialState.Failure);
    }

    [Fact]
    public void Without_the_reflection_a_banked_sum_of_20_would_need_235_losses()
    {
        // the arithmetic of the spec: ceil((20 + 2.944439) / 0.097778) = 235 (the test reflects at 2.772589, so 59 suffice)
        Math.Ceiling((20 + 2.944439) / 0.097778).ShouldBe(235);
        Math.Ceiling((Defaults.OnTrackLevel - Defaults.FailureBoundary) / 0.097778).ShouldBe(59);
    }

    [Fact]
    public void Five_same_day_losses_are_discounted_by_the_design_effect()
    {
        Run(Repeat(-1, 5, m: 5), rho: 0.3).Sum.ShouldBe(-0.222222, 5e-7);
        Run(Repeat(-1, 5, m: 5), rho: 0).Sum.ShouldBe(-0.488889, 5e-7);
        Run(Repeat(-1, 5, m: 1), rho: 0.3).Sum.ShouldBe(-0.488889, 5e-7);
    }

    [Fact]
    public void The_effective_n_is_the_trades_over_the_design_effect_of_their_days()
    {
        var result = Run(Repeat(-1, 6, m: 6), rho: 0.3); // design effect 2.5

        result.EffectiveN.ShouldBe(2.4, 1e-9);
        result.Rho.ShouldBe(0.3);
    }

    [Fact]
    public void The_sigma_is_never_below_the_configured_one_and_a_larger_sample_sd_shrinks_the_step()
    {
        SequentialFailureTest.SigmaUsed(1.5, 1.2, 20, 20).ShouldBe(1.5);
        SequentialFailureTest.SigmaUsed(1.5, 2.0, 20, 20).ShouldBe(2.0);
        SequentialFailureTest.SigmaUsed(1.5, 2.0, 19, 20).ShouldBe(1.5);
        SequentialFailureTest.Llr(1.1, 0.2, 2.0).ShouldBe(0.05 * (1.1 - 0.1), 1e-12);
    }

    [Fact]
    public void A_wide_series_raises_sigma_from_the_earlier_trades_and_a_narrow_one_does_not()
    {
        // alternating +5 and -5: sample sd about 5.1, far above 1.5
        var wide = Run(Enumerable.Range(0, 30).Select(i => new SequentialTrade(i % 2 == 0 ? 5 : -5, 1)));
        var narrow = Run(Repeat(-1, 30));

        wide.SigmaUsed.ShouldBeGreaterThan(5.0);
        narrow.SigmaUsed.ShouldBe(1.5);
    }

    [Fact]
    public void A_series_that_crossed_failure_and_recovered_still_reads_failure()
    {
        var result = Run([.. Repeat(-1, 31), .. Repeat(2, 200)]);

        result.State.ShouldBe(SequentialState.Failure);
        result.FirstFailureTrade.ShouldBe(31);
        result.Sum.ShouldBeGreaterThan(result.FailureBoundary);
    }

    [Fact]
    public void No_trade_is_monitoring_with_an_empty_path()
    {
        var result = Run([]);

        (result.State, result.Sum, result.Trades, result.EffectiveN, result.LowestAtTrade).ShouldBe((SequentialState.Monitoring, 0.0, 0, 0.0, 0));
        result.Path.ShouldBeEmpty();
    }

    [Fact]
    public void The_same_series_gives_the_same_result()
    {
        var series = Enumerable.Range(0, 80).Select(i => new SequentialTrade(((i * 7) % 5) - 2.0, (i % 4) + 1)).ToArray();

        var first = Run(series);
        var second = Run(series);

        second.ShouldBe(first, new ResultComparer());
        second.Path.ShouldBe(first.Path);
    }

    [Fact]
    public void A_bad_input_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Run([new SequentialTrade(double.NaN, 1)]));
        Should.Throw<ArgumentOutOfRangeException>(() => Run([new SequentialTrade(1, 0)]));
        Should.Throw<ArgumentOutOfRangeException>(() => Run([], rho: 1.5));
        Should.Throw<ArgumentException>(() => SequentialFailureTest.Run([], new SequentialOptions(Mu1: -1), 0.3));
    }

    private sealed class TupleTolerance : IEqualityComparer<(double, SequentialState)>
    {
        public bool Equals((double, SequentialState) x, (double, SequentialState) y) => Math.Abs(x.Item1 - y.Item1) < 5e-7 && x.Item2 == y.Item2;

        public int GetHashCode((double, SequentialState) obj) => 0;
    }

    private sealed class ResultComparer : IEqualityComparer<SequentialResult>
    {
        public bool Equals(SequentialResult? x, SequentialResult? y) => x is not null && y is not null && x.State == y.State && x.Sum == y.Sum && x.EffectiveN == y.EffectiveN && x.FirstCautionTrade == y.FirstCautionTrade && x.FirstFailureTrade == y.FirstFailureTrade && x.SigmaUsed == y.SigmaUsed;

        public int GetHashCode(SequentialResult obj) => 0;
    }
}
