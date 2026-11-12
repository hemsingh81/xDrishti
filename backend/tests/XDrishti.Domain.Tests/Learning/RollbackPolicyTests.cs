using System.Globalization;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>
/// CF-10 part 10d-1, D8 and AC11 (ADR 0220): the rollback triggers ARE the P6-09 detectors. The spec's hand numbers are run through the real <see cref="CalibrationDriftDetector"/> and <see cref="RollingBandDetector"/> (a gap of 12 pp fires on a window of 200 and does not on a window of 100;
/// three checks in a row; 20-trade means below the frozen band at three checks), and the policy only reads "in alarm now". An unavailable series is neither a pass nor a trigger.
/// </summary>
public sealed class RollbackPolicyTests
{
    private static string Key(string prefix, int i) => prefix + i.ToString("D5", CultureInfo.InvariantCulture);

    /// <summary>Predicted 0.58 against an observed 0.46 in every contiguous block of 100: a gap of 12 pp in any window that is a multiple of 100.</summary>
    private static List<CalibrationObservation> Gap12(int outcomes) =>
        [.. Enumerable.Range(0, outcomes).Select(i => new CalibrationObservation(Key("g", i), i, 0.58, i % 100 < 46 ? 1 : 0))];

    [Theory]
    [InlineData(200, 3, true)] // gap 12 pp, 3 x 200 outcomes, standard error 3.5 pp, 2.5 SE = 8.7 pp: three breaches in a row fire
    [InlineData(100, 3, false)] // 2.5 SE of 100 outcomes = 12.3 pp: a gap of 12 pp is no breach, whatever the number of checks
    [InlineData(200, 2, false)] // only two checks in a row so far
    public void A_gap_of_12_pp_fires_on_a_window_of_200_three_checks_in_a_row_and_never_on_a_window_of_100(int window, int checks, bool fires)
    {
        var options = new DriftOptions(CalibrationWindow: window, CalibrationCheckEvery: window, CalibrationBreachChecks: 3);

        var decision = RollbackPolicy.Evaluate(new RollbackInputs(Gap12(window * checks), null), options);

        var calibration = decision.Triggers.Single(t => t.Kind == RollbackPolicy.Calibration);
        (calibration.State == RollbackTriggerState.Fires).ShouldBe(fires);
        decision.ShouldRollback.ShouldBe(fires);
        if (fires)
        {
            calibration.Value!.Value.ShouldBe(12, 1e-9);
            calibration.Limit!.Value.ShouldBeLessThan(12);
            calibration.Text.ShouldContain("3 checks in a row");
        }
        else
        {
            calibration.State.ShouldBe(RollbackTriggerState.Quiet);
        }
    }

    [Fact]
    public void The_trigger_is_the_detector_itself_its_value_limit_and_words_are_the_detectors_event()
    {
        var series = Gap12(600);
        var options = new DriftOptions(CalibrationWindow: 200, CalibrationCheckEvery: 200, CalibrationBreachChecks: 3);
        var events = CalibrationDriftDetector.Run("champion", series, options).Value;

        var trigger = RollbackPolicy.Evaluate(new RollbackInputs(series, null), options).Triggers[0];

        events.Count.ShouldBe(1);
        (trigger.Value, trigger.Limit, trigger.Text).ShouldBe((events[0].Value, events[0].Limit, events[0].Evidence));
    }

    [Fact]
    public void On_the_defaults_a_big_gap_fires_at_the_third_check_of_a_window_of_100_every_50_outcomes()
    {
        var series = Enumerable.Range(0, 200).Select(i => new CalibrationObservation(Key("d", i), i, 0.7, i % 2 == 0 ? 1 : 0)).ToList();

        var at199 = RollbackPolicy.Evaluate(new RollbackInputs(series.Take(199).ToList(), null), new DriftOptions());
        var at200 = RollbackPolicy.Evaluate(new RollbackInputs(series, null), new DriftOptions());

        at199.Triggers[0].State.ShouldBe(RollbackTriggerState.Quiet, "two checks so far");
        at200.Triggers[0].State.ShouldBe(RollbackTriggerState.Fires);
    }

    [Fact]
    public void A_cleared_alarm_is_no_longer_a_trigger()
    {
        // 200 outcomes with a 20 pp gap fire; then 300 well calibrated ones (gap 0) clear it after three checks within half the limit
        var bad = Enumerable.Range(0, 200).Select(i => new CalibrationObservation(Key("b", i), i, 0.7, i % 2 == 0 ? 1 : 0));
        var good = Enumerable.Range(200, 300).Select(i => new CalibrationObservation(Key("b", i), i, 0.5, i % 2 == 0 ? 1 : 0));
        var series = bad.Concat(good).ToList();

        var decision = RollbackPolicy.Evaluate(new RollbackInputs(series, null), new DriftOptions());

        CalibrationDriftDetector.Run("m", series, new DriftOptions()).Value.Select(e => e.Fired).ShouldBe([true, false]);
        decision.Triggers[0].State.ShouldBe(RollbackTriggerState.Quiet);
        decision.ShouldRollback.ShouldBeFalse();
    }

    private static (List<DriftObservation> Trades, long MonitorFrom) Band(double reference, double monitored)
    {
        // 40 reference trades alternating around the reference mean (sd about 1), then 60 monitored trades around the monitored mean: three 20-trade checks
        var trades = new List<DriftObservation>();
        for (var i = 0; i < 40; i++)
        {
            trades.Add(new DriftObservation(Key("r", i), i, reference + (i % 2 == 0 ? 1 : -1)));
        }

        for (var i = 0; i < 60; i++)
        {
            trades.Add(new DriftObservation(Key("m", i), 100 + i, monitored + (i % 2 == 0 ? 1 : -1)));
        }

        return (trades, 100);
    }

    [Fact]
    public void Twenty_trade_means_below_the_frozen_band_for_three_checks_fire_the_band_trigger_and_name_the_cells()
    {
        var (trades, from) = Band(0.5, -1.5);
        var inputs = new RollbackInputs(null, [new RollbackCellSeries("A|15m|BUY|intraday|FIXED_RR|*", trades, from)]);

        var decision = RollbackPolicy.Evaluate(inputs, new DriftOptions());

        var band = decision.Triggers.Single(t => t.Kind == RollbackPolicy.Band);
        band.State.ShouldBe(RollbackTriggerState.Fires);
        band.Cells.ShouldBe(["A|15m|BUY|intraday|FIXED_RR|*"]);
        band.Text.ShouldContain("3 checks in a row");
        decision.ShouldRollback.ShouldBeTrue();
        RollingBandDetector.Run("c", trades, from, RollingBandDetector.MakeBand(trades.Where(t => t.KnownAtEpochSeconds < from).ToList(), new DriftOptions()).Value, new DriftOptions())[0].Value.ShouldBe(band.Value!.Value, 1e-12);
    }

    [Fact]
    public void Means_inside_the_band_are_quiet_and_two_checks_below_are_not_enough()
    {
        var (steady, from) = Band(0.5, 0.5);
        var (shortRun, _) = Band(0.5, -1.5);
        var tooShort = shortRun.Take(40 + 40).ToList(); // two monitored checks only

        RollbackPolicy.Evaluate(new RollbackInputs(null, [new RollbackCellSeries("a", steady, from)]), new DriftOptions()).Triggers[1].State.ShouldBe(RollbackTriggerState.Quiet);
        RollbackPolicy.Evaluate(new RollbackInputs(null, [new RollbackCellSeries("a", tooShort, from)]), new DriftOptions()).Triggers[1].State.ShouldBe(RollbackTriggerState.Quiet);
    }

    [Fact]
    public void A_cell_with_too_few_reference_trades_is_not_monitored_and_a_set_with_no_band_is_unavailable_not_quiet()
    {
        var thin = Enumerable.Range(0, 10).Select(i => new DriftObservation(Key("t", i), i, 1.0)).ToList();
        var (bad, from) = Band(0.5, -1.5);

        var none = RollbackPolicy.Evaluate(new RollbackInputs(null, [new RollbackCellSeries("thin", thin, 5)]), new DriftOptions());
        var mixed = RollbackPolicy.Evaluate(new RollbackInputs(null, [new RollbackCellSeries("thin", thin, 5), new RollbackCellSeries("bad", bad, from)]), new DriftOptions());

        none.Triggers[1].State.ShouldBe(RollbackTriggerState.Unavailable);
        mixed.Triggers[1].State.ShouldBe(RollbackTriggerState.Fires);
        mixed.Triggers[1].Cells.ShouldBe(["bad"]);
    }

    [Fact]
    public void Missing_inputs_are_unavailable_never_a_trigger_and_never_a_pass()
    {
        var decision = RollbackPolicy.Evaluate(RollbackInputs.None, new DriftOptions());

        decision.Triggers.Select(t => t.State).ShouldBe([RollbackTriggerState.Unavailable, RollbackTriggerState.Unavailable]);
        decision.ShouldRollback.ShouldBeFalse();
        decision.Firing.ShouldBeEmpty();
        default(RollbackTriggerState).ShouldBe(RollbackTriggerState.Unavailable);
        RollbackPolicy.Evaluate(new RollbackInputs([new CalibrationObservation("x", 1, 1.5, 1)], null), new DriftOptions()).Triggers[0].State.ShouldBe(RollbackTriggerState.Unavailable);
    }

    [Fact]
    public void Either_trigger_alone_is_enough()
    {
        var (trades, from) = Band(0.5, -1.5);
        var calibration = new RollbackInputs(Enumerable.Range(0, 200).Select(i => new CalibrationObservation(Key("e", i), i, 0.7, i % 2 == 0 ? 1 : 0)).ToList(), null);
        var band = new RollbackInputs(null, [new RollbackCellSeries("a", trades, from)]);

        RollbackPolicy.Evaluate(calibration, new DriftOptions()).Firing.Select(t => t.Kind).ShouldBe([RollbackPolicy.Calibration]);
        RollbackPolicy.Evaluate(band, new DriftOptions()).Firing.Select(t => t.Kind).ShouldBe([RollbackPolicy.Band]);
        RollbackPolicy.Evaluate(new RollbackInputs(calibration.Calibration, band.Cells), new DriftOptions()).Firing.Count.ShouldBe(2);
    }

    [Fact]
    public void The_input_order_does_not_matter()
    {
        var series = Gap12(600);
        var options = new DriftOptions(CalibrationWindow: 200, CalibrationCheckEvery: 200, CalibrationBreachChecks: 3);

        var forward = RollbackPolicy.Evaluate(new RollbackInputs(series, null), options);
        var backward = RollbackPolicy.Evaluate(new RollbackInputs([.. series.AsEnumerable().Reverse()], null), options);

        backward.Triggers.Select(t => (t.Kind, t.State, t.Value, t.Limit, t.Text)).ShouldBe(forward.Triggers.Select(t => (t.Kind, t.State, t.Value, t.Limit, t.Text)).ToList());
    }

    [Fact]
    public void The_key_text_of_the_drift_options_is_culture_independent_and_changes_with_a_value()
    {
        new DriftOptions().KeyText().ShouldBe(new DriftOptions().KeyText());
        new DriftOptions(CalibrationZ: 3).KeyText().ShouldNotBe(new DriftOptions().KeyText());
        new DriftOptions().KeyText().ShouldContain("calZ=2.5");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(99)]
    public void Below_the_calibration_window_the_trigger_is_unavailable_not_quiet(int outcomes)
    {
        // CF-10e review: the detector makes no check below its window, and "no event" must not read as a pass
        var decision = RollbackPolicy.Evaluate(new RollbackInputs(Gap12(outcomes), null), new DriftOptions());

        decision.Triggers[0].State.ShouldBe(RollbackTriggerState.Unavailable);
        decision.ShouldRollback.ShouldBeFalse();
    }

    [Fact]
    public void With_a_full_window_of_well_calibrated_outcomes_the_trigger_is_quiet()
    {
        var calibrated = Enumerable.Range(0, 100).Select(i => new CalibrationObservation(Key("c", i), i, 0.5, i % 2)).ToList();

        RollbackPolicy.Evaluate(new RollbackInputs(calibrated, null), new DriftOptions()).Triggers[0].State.ShouldBe(RollbackTriggerState.Quiet);
    }
}
