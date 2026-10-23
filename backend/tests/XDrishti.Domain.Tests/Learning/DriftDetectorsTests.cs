using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The drift monitors (spec P6-09): constructed series fire and clear, stationary series rarely alarm, a check never sees the future, the input order does not matter.</summary>
public sealed class DriftDetectorsTests
{
    private const long Day = 86_400;
    private static readonly DriftOptions Options = new();

    /// <summary>Trades one a day from day 0; the value of each is given by <paramref name="r"/>.</summary>
    private static List<DriftObservation> Series(int count, Func<int, double> r, int startDay = 0) =>
        [.. Enumerable.Range(startDay, count).Select(i => new DriftObservation("t" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture), i * Day, r(i)))];

    /// <summary>Mean 0.3, sd about 1.</summary>
    private static double Alternating(int i) => i % 2 == 0 ? 1.3 : -0.7;

    private static long Cut(int day) => day * Day;

    private static ExpectancyBand Band() => RollingBandDetector.MakeBand(Series(40, Alternating), Options).Value;

    [Fact]
    public void The_band_is_the_reference_mean_less_standard_errors_of_a_window()
    {
        var band = Band();

        band.Mean.ShouldBe(0.3, 1e-9);
        band.Sd.ShouldBe(1.0127, 1e-3);
        band.Lower.ShouldBe(0.3 - (1.645 * band.Sd / Math.Sqrt(20)), 1e-9);
        band.ClearLevel.ShouldBe((band.Mean + band.Lower) / 2, 1e-9);
    }

    [Fact]
    public void A_reference_that_is_too_short_or_flat_gives_no_band()
    {
        RollingBandDetector.MakeBand(Series(29, Alternating), Options).IsFailure.ShouldBeTrue();
        RollingBandDetector.MakeBand(Series(40, _ => 0.5), Options).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_band_fires_after_three_checks_below_it_and_clears_after_three_back_inside()
    {
        // 40 reference trades, 60 bad ones (checks 1 to 3 below the edge), 60 good ones (checks 4 to 6 above the clear level).
        var all = Series(160, i => i < 40 ? Alternating(i) : i < 100 ? -0.5 : 0.5);

        var events = RollingBandDetector.Run("c", all, Cut(40), Band(), Options);

        events.Count.ShouldBe(2);
        events[0].Fired.ShouldBeTrue();
        events[0].Check.ShouldBe(3);
        events[1].Fired.ShouldBeFalse();
        events[1].Check.ShouldBe(6);
        events[0].Evidence.ShouldContain("below the band edge");
    }

    [Fact]
    public void Two_checks_below_the_band_are_not_enough()
    {
        var all = Series(120, i => i < 40 ? Alternating(i) : i < 80 ? -0.5 : Alternating(i));

        RollingBandDetector.Run("c", all, Cut(40), Band(), Options).ShouldBeEmpty();
    }

    [Fact]
    public void A_recovery_that_stays_between_the_edge_and_the_clear_level_does_not_clear()
    {
        var band = Band();
        var between = (band.Lower + band.ClearLevel) / 2;
        var all = Series(160, i => i < 40 ? Alternating(i) : i < 100 ? -0.5 : between);

        var events = RollingBandDetector.Run("c", all, Cut(40), band, Options);

        events.ShouldHaveSingleItem().Fired.ShouldBeTrue();
    }

    [Fact]
    public void The_cusum_fires_on_a_shift_and_clears_when_it_ends()
    {
        var all = Series(160, i => i < 40 ? Alternating(i) : i < 80 ? -0.5 : 1.5);

        var events = CusumDetector.Run("c", all, Cut(40), Band(), Options);

        events.Count.ShouldBe(2);
        events[0].Fired.ShouldBeTrue();
        events[0].Check.ShouldBeInRange(25, 32);
        events[0].AtEpochSeconds.ShouldBeLessThan(Cut(80));
        events[1].Fired.ShouldBeFalse();
        events[1].AtEpochSeconds.ShouldBeGreaterThan(Cut(80));
    }

    [Fact]
    public void The_cusum_ignores_a_shift_smaller_than_its_allowance()
    {
        // 0.3 mean less 0.3 R is a quarter of a standard deviation: under the allowance of half.
        var all = Series(400, i => i < 40 ? Alternating(i) : 0.0 + (i % 2 == 0 ? 1.0 : -1.0));

        CusumDetector.Run("c", all, Cut(40), Band(), Options).ShouldBeEmpty();
    }

    [Fact]
    public void Calibration_fires_when_observed_falls_away_from_predicted_and_clears_when_it_returns()
    {
        // Predicted 50 % throughout; the first 100 outcomes are balanced, the next 150 all lose, the last 300 are balanced again.
        var all = Outcomes(550, i => i is >= 100 and < 250 ? 0 : i % 2);

        var events = CalibrationDriftDetector.Run("model:1", all, Options).Value;

        events.Count.ShouldBe(2);
        events[0].Fired.ShouldBeTrue();
        events[0].Check.ShouldBe(4);
        events[0].Value.ShouldBe(50, 1e-9);
        events[0].Evidence.ShouldContain("window ECE");
        events[1].Fired.ShouldBeFalse();
        events[1].Check.ShouldBe(8);
    }

    [Fact]
    public void Calibration_noise_within_the_standard_error_is_not_a_breach()
    {
        // 100 outcomes at 50 % predicted with 60 wins is a 10 pp gap: above the 5 pp limit, under 2.5 standard errors (12.5 pp).
        var all = Outcomes(300, i => i % 10 < 6 ? 1 : 0);

        CalibrationDriftDetector.Run("model:1", all, Options).Value.ShouldBeEmpty();
    }

    [Fact]
    public void Calibration_refuses_a_probability_outside_zero_to_one()
    {
        var all = new List<CalibrationObservation> { new("a", 1, 1.2, 1) };

        CalibrationDriftDetector.Run("model:1", all, Options).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_check_never_reads_a_trade_known_after_it()
    {
        var past = Series(160, i => i < 40 ? Alternating(i) : -0.5);
        var withFuture = past.Concat(Series(100, _ => 5.0, 160)).ToList();
        var lastPast = past[^1].KnownAtEpochSeconds;

        var a = Replay(past);
        var b = Replay(withFuture);

        b.Where(e => e.AtEpochSeconds <= lastPast).ShouldBe(a);
    }

    [Fact]
    public void A_check_inside_an_instant_with_several_trades_waits_for_the_whole_instant()
    {
        // Every day holds four trades at the same second; a check can only be made after the fourth.
        var all = Enumerable.Range(0, 400).Select(i => new DriftObservation("k" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), (i / 4) * Day, i / 4 < 10 ? Alternating(i) : -0.5)).ToList();

        var events = RollingBandDetector.Run("c", all, Cut(10), RollingBandDetector.MakeBand(all.Where(o => o.KnownAtEpochSeconds < Cut(10)).ToList(), Options with { MinReferenceTrades = 30 }).Value, Options);

        events.ShouldNotBeEmpty();
        events.ShouldAllBe(e => all.Count(o => o.KnownAtEpochSeconds == e.AtEpochSeconds) == 4 && all.Count(o => o.KnownAtEpochSeconds <= e.AtEpochSeconds) % 4 == 0);
    }

    [Fact]
    public void The_input_order_does_not_change_the_result()
    {
        var all = Series(160, i => i < 40 ? Alternating(i) : i < 100 ? -0.5 : 0.5);
        var shuffled = all.OrderBy(o => o.Key.GetHashCode(StringComparison.Ordinal) ^ 0x5bd1e995).ToList();

        Replay(shuffled).ShouldBe(Replay(all));
    }

    [Fact]
    public void Stationary_series_rarely_alarm_with_the_default_settings()
    {
        // 300 seeded series: 60 reference trades and 120 monitored, all from one distribution (mean 0.2 R, sd 1.2 R, a little skewed by a heavy loss).
        var rare = 0;
        var cusum = 0;
        var bandAlarms = 0;
        for (var seed = 1; seed <= 300; seed++)
        {
            var rng = new Random(seed);
            var all = Series(180, _ => Draw(rng));
            var band = RollingBandDetector.MakeBand(all.Take(60).ToList(), Options).Value;
            if (RollingBandDetector.Run("c", all, Cut(60), band, Options).Any(e => e.Fired))
            {
                bandAlarms++;
            }

            if (CusumDetector.Run("c", all, Cut(60), band, Options).Any(e => e.Fired))
            {
                cusum++;
            }

            rare++;
        }

        (bandAlarms / (double)rare).ShouldBeLessThan(0.03);
        (cusum / (double)rare).ShouldBeLessThan(0.08);
    }

    [Fact]
    public void Stationary_calibrated_outcomes_rarely_alarm()
    {
        var alarms = 0;
        for (var seed = 1; seed <= 200; seed++)
        {
            var rng = new Random(seed);
            var all = new List<CalibrationObservation>();
            for (var i = 0; i < 600; i++)
            {
                var p = 0.3 + (0.4 * rng.NextDouble());
                all.Add(new CalibrationObservation("k" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), i * Day, p, rng.NextDouble() < p ? 1 : 0));
            }

            if (CalibrationDriftDetector.Run("m", all, Options).Value.Any(e => e.Fired))
            {
                alarms++;
            }
        }

        (alarms / 200.0).ShouldBeLessThan(0.03);
    }

    private static double Draw(Random rng)
    {
        var u1 = 1 - rng.NextDouble();
        var z = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * rng.NextDouble());
        return 0.2 + (1.2 * z);
    }

    private static List<DriftEvent> Replay(List<DriftObservation> all)
    {
        var band = RollingBandDetector.MakeBand(all.Where(o => o.KnownAtEpochSeconds < Cut(40)).ToList(), Options).Value;
        return [.. RollingBandDetector.Run("c", all, Cut(40), band, Options).Concat(CusumDetector.Run("c", all, Cut(40), band, Options)).OrderBy(e => e.AtEpochSeconds).ThenBy(e => e.Detector)];
    }

    private static List<CalibrationObservation> Outcomes(int count, Func<int, int> label) =>
        [.. Enumerable.Range(0, count).Select(i => new CalibrationObservation("k" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), i * Day, 0.5, label(i)))];
}
