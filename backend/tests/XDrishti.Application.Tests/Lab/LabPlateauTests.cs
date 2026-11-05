using XDrishti.Application.Lab;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Tests.Lab;

/// <summary>
/// CF-07 part C (ADR 0210): the plateau gate read from the stored in-sample sweep. The numbers are the spec's (criterion 7); the gate reads no hold-out window, adds no look, and with Enforce off changes nothing but its own member.
/// </summary>
public sealed class LabPlateauTests
{
    private static readonly DateOnly End = new(2026, 4, 30);

    private static SweepPointView Point(string param, double value, double? mean, int trades = 100, string? error = null) => new(param, value, trades, mean, null, null, error);

    private static ParameterSensitivityView Param(string name, params (double Value, double? Mean)[] points) =>
        new(name, [.. points.Select(p => Point(name, p.Value, p.Mean))], "stable", "x");

    private static SensitivityView Sweep(double? baseMean, params ParameterSensitivityView[] parameters) =>
        new("15m", "FIXED_RR{rr=2}", baseMean is null ? null : Point(string.Empty, 0, baseMean), parameters, "in-sample sweep, one parameter at a time");

    private static IReadOnlyList<KeyValuePair<string, double>> Declared(params (string Name, double Value)[] values) => [.. values.Select(v => new KeyValuePair<string, double>(v.Name, v.Value))];

    private static PlateauView Read(SensitivityView sweep, IReadOnlyList<KeyValuePair<string, double>> declared, PlateauOptions? options = null, DateOnly? developmentEnd = null, DateOnly? sweepEnd = null) =>
        new LabPlateau(options ?? new PlateauOptions()).Read(declared, [.. declared.Select(d => d.Key)], sweep, developmentEnd ?? End, sweepEnd ?? End);

    private static ParameterSensitivityView Good(string name) => Param(name, (8, 0.05), (9, 0.10), (11, 0.11), (12, 0.04));

    [Fact]
    public void The_spec_example_passes_and_the_block_carries_value_threshold_status_and_the_centre()
    {
        var view = Read(Sweep(0.12, Good("period")), Declared(("period", 10)));

        view.Status.ShouldBe("pass");
        (view.Retention, view.MinPoints, view.Comparison, view.Mode, view.Enforce, view.Capped).ShouldBe((0.70, 3, ">=", "ReportOnly", false, false));
        var p = view.Parameters.Single();
        (p.Param, p.Declared, p.Status, p.Reading, p.ValidPoints).ShouldBe(("period", 10.0, "pass", "plateau", 5));
        p.LowerRetention!.Value.ShouldBe(0.10 / 0.12, 1e-12);
        p.UpperRetention!.Value.ShouldBe(0.11 / 0.12, 1e-12);
        p.Median!.Value.ShouldBe(0.10, 1e-12);
        (p.CentreValue, p.CentreMeanR!.Value).ShouldBe((10.0, (0.10 + 0.12 + 0.11) / 3));
        p.Points.Select(x => x.Value).ShouldBe([8.0, 9.0, 10.0, 11.0, 12.0]);
        p.Points.Single(x => x.Declared).Value.ShouldBe(10);
        p.Points.Single(x => x.Declared).Retention!.Value.ShouldBe(1, 1e-12);
        view.SettingsHash.Length.ShouldBe(64);
    }

    [Fact]
    public void The_block_says_in_sample_sweep_and_that_the_declared_value_is_never_changed()
    {
        var view = Read(Sweep(0.12, Good("period")), Declared(("period", 10)));

        view.Basis.ShouldContain("in-sample sweep");
        view.Basis.ShouldContain("development window ending 2026-04-30");
        view.Basis.ShouldContain("never changed");
    }

    [Fact]
    public void A_spike_fails_the_gate_and_names_the_parameter()
    {
        var spike = Param("period", (8, 0.01), (9, 0.02), (11, 0.02), (12, 0.01));

        var view = Read(Sweep(0.15, spike), Declared(("period", 10)));

        view.Status.ShouldBe("fail");
        view.Parameters.Single().Reading.ShouldBe("spike");
        view.Reason.ShouldContain("period");
    }

    [Fact]
    public void One_failing_parameter_fails_the_gate_even_when_the_other_passes()
    {
        var spike = Param("length", (8, 0.01), (9, 0.02), (11, 0.02), (12, 0.01));

        var view = Read(Sweep(0.12, Good("period"), spike), Declared(("period", 10), ("length", 10)));

        view.Status.ShouldBe("fail");
        view.Parameters.Select(p => p.Status).ShouldBe(["pass", "fail"]);
    }

    [Fact]
    public void A_parameter_with_too_few_valid_points_reads_unavailable_and_so_does_the_gate()
    {
        var view = Read(Sweep(0.12, Good("period"), Param("length", (9, 0.11))), Declared(("period", 10), ("length", 10)));

        view.Status.ShouldBe("unavailable");
        view.Parameters[^1].Reading.ShouldBe("inconclusive");
        view.Reason.ShouldContain("length");
    }

    [Fact]
    public void A_scheduled_parameter_whose_every_step_was_dropped_reads_inconclusive()
    {
        var view = Read(Sweep(0.12, new ParameterSensitivityView("period", [], "inconclusive", "no points")), Declared(("period", 10)));

        view.Status.ShouldBe("unavailable");
        view.Parameters.Single().ValidPoints.ShouldBe(1);
    }

    [Fact]
    public void A_strategy_with_no_parameter_reads_unavailable_and_never_a_pass()
    {
        var view = new LabPlateau(new PlateauOptions()).Read([], [], Sweep(0.12), End, End);

        view.Status.ShouldBe("unavailable");
        view.Parameters.ShouldBeEmpty();
        view.Reason.ShouldContain("no parameter");
    }

    [Fact]
    public void A_missing_base_run_makes_every_parameter_unavailable()
    {
        var view = Read(Sweep(null, Good("period")), Declared(("period", 10)));

        view.Status.ShouldBe("unavailable");
        view.Parameters.Single().BaseMeanR.ShouldBeNull();
    }

    [Fact]
    public void A_sweep_window_that_reaches_the_hold_out_block_is_unavailable_and_no_figure_of_it_is_shown()
    {
        var view = Read(Sweep(0.12, Good("period")), Declared(("period", 10)), developmentEnd: new DateOnly(2026, 4, 19), sweepEnd: End);

        view.Status.ShouldBe("unavailable");
        view.Reason.ShouldContain("hold-out");
        view.Parameters.ShouldBeEmpty("a figure that depends on a hold-out session is not shown");
        view.Basis.ShouldContain("inside the hold-out block");
    }

    [Fact]
    public void A_sweep_that_ends_exactly_on_the_development_end_is_read()
    {
        Read(Sweep(0.12, Good("period")), Declared(("period", 10)), developmentEnd: End, sweepEnd: End).Status.ShouldBe("pass");
    }

    [Fact]
    public void The_settings_that_made_the_figure_travel_with_it()
    {
        var view = Read(Sweep(0.12, Good("period")), Declared(("period", 10)), new PlateauOptions(Retention: 0.9));

        view.Settings.ShouldBe("ReportOnly/enforce=off/retention=0.9/minPoints=3");
        view.Status.ShouldBe("fail", "the neighbours keep 83.3 % and 91.7 %, below 90 % on one side");
    }

    private static LabVerdictView Verdict(string outcome, PlateauView? plateau) => new(
        outcome, 1, 1,
        new EvidenceView(60, 0.20, 1.5, 8, 6, 3, 0, "walk_forward"),
        [new GateView("oos_trades", 50, 50, 60, true, "ok")],
        ["a limit"], [], [], ["a label"],
        Nulls: plateau is null ? null : new NullsView("ReportOnly", false, "s", "h", new RandomEntryView(40, 40, 1000, 1000, 0.1, 0, 0, 0, 0, 0, 95, 0, 0, 0.001, 0.05, "<=", "pass", "ok", 0, 0, "1"), false, "basis", [], Plateau: plateau));

    private static PlateauView Failing(string status = "fail") => Read(Sweep(0.15, Param("period", (8, 0.01), (9, 0.02), (11, 0.02), (12, 0.01))), Declared(("period", 10))) with { Status = status };

    [Fact]
    public void Report_only_leaves_every_outcome_and_limit_as_it_was()
    {
        var plateau = new LabPlateau(new PlateauOptions());
        var verdict = Verdict("PASS", Failing());

        plateau.Apply(verdict).ShouldBeSameAs(verdict);
    }

    [Fact]
    public void Enforce_caps_a_pass_at_marginal_when_the_gate_fails_and_says_so()
    {
        var plateau = new LabPlateau(new PlateauOptions(Enforce: true));

        var capped = plateau.Apply(Verdict("PASS", Failing()));

        capped.Outcome.ShouldBe("MARGINAL");
        capped.Limits.ShouldContain("capped at MARGINAL by the plateau gate (fail) (enforced)");
        capped.Nulls!.Plateau!.Capped.ShouldBeTrue();
        capped.Gates.ShouldBe(Verdict("PASS", null).Gates);
        capped.Evidence.ShouldBe(Verdict("PASS", null).Evidence);
    }

    [Fact]
    public void Enforce_treats_an_unavailable_gate_as_a_miss()
    {
        new LabPlateau(new PlateauOptions(Enforce: true)).Apply(Verdict("PASS", Failing("unavailable"))).Outcome.ShouldBe("MARGINAL");
    }

    [Theory]
    [InlineData("MARGINAL")]
    [InlineData("FAIL")]
    public void Enforce_never_raises_or_changes_an_outcome_that_is_not_a_pass(string outcome)
    {
        var verdict = Verdict(outcome, Failing());

        new LabPlateau(new PlateauOptions(Enforce: true)).Apply(verdict).ShouldBeSameAs(verdict);
    }

    [Fact]
    public void Enforce_leaves_a_passing_gate_and_a_verdict_without_the_member_alone()
    {
        var plateau = new LabPlateau(new PlateauOptions(Enforce: true));
        var passing = Verdict("PASS", Failing("pass"));
        var none = Verdict("PASS", null);

        plateau.Apply(passing).ShouldBeSameAs(passing);
        plateau.Apply(none).ShouldBeSameAs(none);
    }
}
