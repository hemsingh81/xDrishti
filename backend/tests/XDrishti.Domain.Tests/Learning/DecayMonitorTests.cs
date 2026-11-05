using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-12 part 12a, AC1 and AC2 (ADR 0218, spec D1): the threshold by hand, non-overlapping windows, twice in a row, the P6-09 CUSUM reused, the by-quarter table.</summary>
public sealed class DecayMonitorTests
{
    private static readonly ExpectancyBand Reference = new(320, 0.12, 1.0, 0, 0);
    private static readonly DecayOptions Options = new();
    private static readonly DriftOptions Drift = new();

    private static List<DecayTrade> Trades(int count, Func<int, double> r, int dayCandidates = 1, DateOnly? start = null) =>
        [.. Enumerable.Range(0, count).Select(i => new DecayTrade("t" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture), i, (start ?? new DateOnly(2026, 1, 5)).AddDays(i), r(i), dayCandidates))];

    [Fact]
    public void The_threshold_with_a_design_effect_of_one_is_minus_0_0910()
    {
        DecayMonitor.Threshold(0.12, 1.0, 320, 60, 1, 1.5).ShouldBe(-0.0910, 5e-5);
        DecayMonitor.Threshold(0.12, 1.0, 320, 60, 1, 1.5).ShouldBe(0.12 - (1.5 * Math.Sqrt((1.0 / 60) + (1.0 / 320))), 1e-12);
    }

    [Fact]
    public void Ignoring_the_error_of_the_reference_it_would_be_minus_0_0736()
    {
        DecayMonitor.Threshold(0.12, 1.0, int.MaxValue, 60, 1, 1.5).ShouldBe(0.12 - (1.5 / Math.Sqrt(60)), 1e-6);
        DecayMonitor.Threshold(0.12, 1.0, int.MaxValue, 60, 1, 1.5).ShouldBe(-0.0736, 1e-4);
    }

    [Fact]
    public void The_threshold_with_a_design_effect_of_2_5_is_minus_0_1975()
    {
        DecayMonitor.Threshold(0.12, 1.0, 320, 60, 2.5, 1.5).ShouldBe(-0.1975, 5e-5);
    }

    [Fact]
    public void A_window_mean_of_minus_0_10_alerts_at_a_design_effect_of_one_and_not_at_2_5()
    {
        var one = DecayMonitor.Evaluate(Trades(60, _ => -0.10, 1), Reference, Options, 0.3, Drift);
        var six = DecayMonitor.Evaluate(Trades(60, _ => -0.10, 6), Reference, Options, 0.3, Drift); // 6 candidates a day, rho 0.3: 1 + 5 * 0.3 = 2.5

        one.Windows.ShouldHaveSingleItem().Below.ShouldBeTrue();
        one.Windows[0].DesignEffect.ShouldBe(1, 1e-12);
        six.Windows.ShouldHaveSingleItem().Below.ShouldBeFalse();
        six.Windows[0].DesignEffect.ShouldBe(2.5, 1e-12);
        six.Windows[0].Threshold.ShouldBe(-0.1975, 5e-5);
    }

    [Fact]
    public void A_window_just_above_the_threshold_does_not_alert_and_just_below_does()
    {
        var threshold = DecayMonitor.Threshold(0.12, 1.0, 320, 60, 1, 1.5);

        DecayMonitor.Evaluate(Trades(60, _ => threshold + 0.001), Reference, Options, 0.3, Drift).Windows[0].Below.ShouldBeFalse();
        DecayMonitor.Evaluate(Trades(60, _ => threshold - 0.001), Reference, Options, 0.3, Drift).Windows[0].Below.ShouldBeTrue();
    }

    [Fact]
    public void A_hundred_and_thirty_trades_make_two_windows_and_ten_pending()
    {
        var report = DecayMonitor.Evaluate(Trades(130, _ => 0.1), Reference, Options, 0.3, Drift);

        report.Windows.Count.ShouldBe(2);
        report.PendingTrades.ShouldBe(10);
        report.Trades.ShouldBe(130);
        report.Windows.ShouldAllBe(w => w.Trades == 60);
        report.Windows[0].Index.ShouldBe(1);
        report.Windows[1].FirstDay.ShouldBe(new DateOnly(2026, 1, 5).AddDays(60));
    }

    [Fact]
    public void Fewer_trades_than_a_window_make_no_window()
    {
        var report = DecayMonitor.Evaluate(Trades(59, _ => -5), Reference, Options, 0.3, Drift);

        report.Windows.ShouldBeEmpty();
        report.PendingTrades.ShouldBe(59);
        report.TwiceInARow.ShouldBeFalse();
    }

    // each window's value: bad = -1.0 (below), good = +0.5 (above)
    private static DecayReport Windows(params bool[] bad) =>
        DecayMonitor.Evaluate(Trades(60 * bad.Length, i => bad[i / 60] ? -1.0 : 0.5), Reference, Options, 0.3, Drift);

    [Fact]
    public void Two_completed_windows_below_in_a_row_are_a_twice_in_a_row()
    {
        var report = Windows(false, true, true);

        report.Streak.ShouldBe(2);
        report.TwiceInARow.ShouldBeTrue();
        report.FirstTwiceAtWindow.ShouldBe(3);
    }

    [Fact]
    public void A_window_above_in_between_resets_the_streak()
    {
        var report = Windows(true, false, true);

        report.Streak.ShouldBe(1);
        report.TwiceInARow.ShouldBeFalse();
        report.FirstTwiceAtWindow.ShouldBeNull();
    }

    [Fact]
    public void A_streak_that_ended_is_remembered_by_the_first_time_but_is_not_the_present()
    {
        var report = Windows(true, true, false);

        report.TwiceInARow.ShouldBeFalse();
        report.Streak.ShouldBe(0);
        report.FirstTwiceAtWindow.ShouldBe(2);
    }

    [Fact]
    public void The_cusum_is_the_existing_p6_09_detector_run_on_the_same_series()
    {
        var series = Trades(120, _ => -1.0);

        var report = DecayMonitor.Evaluate(series, Reference, Options, 0.3, Drift);
        var direct = CusumDetector.Run("decay", [.. series.Select(t => new DriftObservation(t.Key, t.KnownAtEpochSeconds, t.R))], 0, Reference, Drift);

        direct.ShouldNotBeEmpty();
        report.CusumEvents.ShouldBe(direct);
        report.CusumAlarm.ShouldBeTrue();
    }

    [Fact]
    public void A_series_at_the_reference_has_no_cusum_alarm()
    {
        DecayMonitor.Evaluate(Trades(120, i => i % 2 == 0 ? 1.12 : -0.88), Reference, Options, 0.3, Drift).CusumAlarm.ShouldBeFalse();
    }

    [Fact]
    public void The_quarter_table_has_trades_mean_and_the_share_of_weak_quarters()
    {
        // 2026 Q1: 10 trades at +0.10; Q2: 10 trades at -0.10 (weak, floor -0.05); Q3: 10 trades at -0.04 (not weak)
        var series = new List<DecayTrade>();
        var n = 0;
        foreach (var (month, r) in new[] { (2, 0.10), (5, -0.10), (8, -0.04) })
        {
            for (var i = 0; i < 10; i++)
            {
                series.Add(new DecayTrade("k" + n, n, new DateOnly(2026, month, 1 + i), r, 1));
                n++;
            }
        }

        var report = DecayMonitor.Evaluate(series, Reference, Options, 0.3, Drift);

        report.Quarters.Select(q => (q.Year, q.Quarter, q.Trades, q.Weak)).ShouldBe([(2026, 1, 10, false), (2026, 2, 10, true), (2026, 3, 10, false)]);
        report.Quarters[1].MeanR.ShouldBe(-0.10, 1e-12);
        report.WeakQuarterShare!.Value.ShouldBe(1.0 / 3, 1e-12);
    }

    [Fact]
    public void The_input_order_does_not_change_the_report()
    {
        var series = Trades(130, i => (i % 7) - 3.0);

        var forward = DecayMonitor.Evaluate(series, Reference, Options, 0.3, Drift);
        var shuffled = DecayMonitor.Evaluate([.. series.OrderByDescending(t => t.Key, StringComparer.Ordinal)], Reference, Options, 0.3, Drift);

        shuffled.Windows.ShouldBe(forward.Windows);
        shuffled.Quarters.ShouldBe(forward.Quarters);
        shuffled.CusumEvents.ShouldBe(forward.CusumEvents);
    }

    [Fact]
    public void Bad_input_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DecayMonitor.Evaluate([new DecayTrade("a", 1, new DateOnly(2026, 1, 5), double.NaN, 1)], Reference, Options, 0.3, Drift));
        Should.Throw<ArgumentOutOfRangeException>(() => DecayMonitor.Evaluate([new DecayTrade("a", 1, new DateOnly(2026, 1, 5), 0.1, 0)], Reference, Options, 0.3, Drift));
        Should.Throw<ArgumentOutOfRangeException>(() => DecayMonitor.Evaluate([], Reference, Options, 1.5, Drift));
        Should.Throw<ArgumentException>(() => DecayMonitor.Evaluate([], Reference, new DecayOptions(WindowTrades: 61), 0.3, Drift));
    }
}
