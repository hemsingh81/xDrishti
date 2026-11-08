using System.Text.Json;
using XDrishti.Application.Lab;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Tests.Lab;

/// <summary>CF-03 part B (ADR 0228), criteria 14 to 16: the figure and the series from the variants, the unavailable cases, the size of the document, and the Gate that only caps a PASS.</summary>
public sealed class LabPboTests
{
    private static readonly DateOnly Start = new(2025, 5, 1);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static PboVariant Variant(string label, int days, ulong seed, double drift = 0)
    {
        var random = new XDrishti.Domain.Lab.SeededRandom(seed);
        return new PboVariant(label, [.. Enumerable.Range(0, days).Select(i => (Start.AddDays(i), Math.Round(random.NextDouble() - 0.5 + drift, 6)))]);
    }

    private static List<PboVariant> Variants(int count, int days = 64) => [.. Enumerable.Range(0, count).Select(i => Variant(i == 0 ? "base" : $"p={i}", days, (ulong)(i + 1)))];

    private static LabVerdictView Verdict(string outcome, PboView? pbo) => new(
        outcome, 4, 1.5, new EvidenceView(100, 0.4, 1.8, 12, 9, 4, 2, "walk_forward"), [], ["a limit"], [], [], [],
        new RobustnessView(new SensitivityView("15m", "x", null, [], "l"), null, [], null, "n", [], Pbo: pbo));

    private static PboView Figure(double? pbo, string mode = "Gate") => new(mode, 12, 8, 70, pbo, 0.2, 0.5, "Mean", false, mode + "/8/10/0.5/Mean", pbo is null ? "unavailable: x" : "note");

    [Fact]
    public void Off_gives_no_figure_and_no_series()
    {
        var (view, series) = LabPbo.Compute(new PboOptions(ConfidenceMode.Off), Variants(12), 0);

        view.ShouldBeNull();
        series.ShouldBeNull();
    }

    [Fact]
    public void Twelve_variants_give_the_figure_with_its_counts_settings_and_the_sweep_limit_and_the_series_beside_it()
    {
        var (view, series) = LabPbo.Compute(new PboOptions(), Variants(12), 0);

        view!.Mode.ShouldBe("ReportOnly");
        view.Variants.ShouldBe(12);
        view.Blocks.ShouldBe(8);
        view.Splits.ShouldBe(70);
        view.Pbo!.Value.ShouldBeInRange(0.0, 1.0);
        view.MedianLogit.ShouldNotBeNull();
        view.MaxPbo.ShouldBe(0.5);
        view.Metric.ShouldBe("Mean");
        view.Capped.ShouldBeFalse();
        view.Settings.ShouldBe("ReportOnly/8/10/0.5/Mean");
        view.Note.ShouldContain("neighbours");
        view.Note.ShouldContain("not whether the strategy family has an edge");
        view.Note.ShouldContain("informational");
        series!.Variants.Count.ShouldBe(12);
        series.Variants[0].Label.ShouldBe("base");
        series.Variants.ShouldAllBe(v => v.Values.Count == series.Dates.Count);
    }

    [Fact]
    public void The_figure_is_computed_from_exactly_the_stored_series()
    {
        var (view, series) = LabPbo.Compute(new PboOptions(), Variants(12), 0);

        var again = Cscv.Pbo([.. series!.Variants.Select(v => v.Values.ToArray())], 8);

        again.Pbo.ShouldBe(view!.Pbo);
        again.MedianLogit.ShouldBe(view.MedianLogit);
    }

    [Fact]
    public void Nine_variants_read_unavailable_with_the_reason_and_no_number_but_keep_the_series()
    {
        var (view, series) = LabPbo.Compute(new PboOptions(), Variants(9), 0);

        view!.Note.ShouldStartWith("unavailable: needs at least 10 variants (have 9)");
        view.Pbo.ShouldBeNull();
        view.MedianLogit.ShouldBeNull();
        view.Splits.ShouldBe(0);
        series!.Variants.Count.ShouldBe(9);
    }

    [Fact]
    public void Ten_variants_are_enough_and_the_minimum_is_the_setting()
    {
        LabPbo.Compute(new PboOptions(), Variants(10), 0).View!.Pbo.ShouldNotBeNull();
        LabPbo.Compute(new PboOptions(MinVariants: 12), Variants(11), 0).View!.Pbo.ShouldBeNull();
        LabPbo.Compute(new PboOptions(MinVariants: 6), Variants(6), 0).View!.Pbo.ShouldNotBeNull();
    }

    [Fact]
    public void Runs_that_were_left_out_are_counted_in_the_note()
    {
        var (view, _) = LabPbo.Compute(new PboOptions(), Variants(12), 3);

        view!.Note.ShouldContain("3 run(s) of the base and the sweep ran no trade or were refused");
    }

    [Fact]
    public void No_variant_at_all_is_unavailable_with_no_series()
    {
        var (view, series) = LabPbo.Compute(new PboOptions(), [], 5);

        view!.Note.ShouldStartWith("unavailable: no variant ran a trade");
        view.Pbo.ShouldBeNull();
        series.ShouldBeNull();
    }

    [Fact]
    public void A_daily_value_that_is_not_finite_is_unavailable_and_no_series_is_stored()
    {
        var variants = Variants(12);
        variants[4] = new PboVariant("p=4", [(Start, double.NaN), (Start.AddDays(1), 1)]);

        var (view, series) = LabPbo.Compute(new PboOptions(), variants, 0);

        view!.Note.ShouldStartWith("unavailable: a daily value is not a finite number");
        view.Pbo.ShouldBeNull();
        series.ShouldBeNull();
    }

    [Fact]
    public void Variants_that_all_look_alike_are_refused_not_given_a_number()
    {
        var one = Variant("base", 64, 1);
        List<PboVariant> same = [.. Enumerable.Range(0, 10).Select(i => one with { Label = $"p={i}" })];

        var (view, series) = LabPbo.Compute(new PboOptions(), same, 0);

        view!.Note.ShouldStartWith("unavailable: all variants are identical");
        view.Pbo.ShouldBeNull();
        series.ShouldNotBeNull("the series themselves are fine and stay");
    }

    [Fact]
    public void Too_few_days_for_the_blocks_is_unavailable()
    {
        var (view, _) = LabPbo.Compute(new PboOptions(Blocks: 16), Variants(10, days: 12), 0);

        view!.Note.ShouldStartWith("unavailable: fewer days than blocks");
    }

    [Fact]
    public void Seventeen_variants_over_five_hundred_and_twenty_sessions_stay_under_a_hundred_kilobytes_in_the_document()
    {
        var variants = Enumerable.Range(0, 17).Select(i => Variant(i == 0 ? "base" : $"param_{i}=1.{i:00}", 520, (ulong)(i + 100), drift: 0.05)).ToList();

        var (view, series) = LabPbo.Compute(new PboOptions(), variants, 0);

        view!.Pbo.ShouldNotBeNull();
        series!.Dates.Count.ShouldBe(520);
        JsonSerializer.Serialize(series, Web).Length.ShouldBeLessThan(100_000);
    }

    [Fact]
    public void The_same_variants_give_the_same_figure_and_a_different_metric_a_recorded_one()
    {
        LabPbo.Compute(new PboOptions(), Variants(12), 0).View.ShouldBe(LabPbo.Compute(new PboOptions(), Variants(12), 0).View);

        var sharpe = LabPbo.Compute(new PboOptions(Metric: PboMetric.Sharpe), Variants(12), 0).View!;
        sharpe.Metric.ShouldBe("Sharpe");
        sharpe.Settings.ShouldBe("ReportOnly/8/10/0.5/Sharpe");
    }

    [Fact]
    public void In_gate_mode_a_pass_above_the_limit_becomes_marginal_with_the_reason_and_the_figure_says_capped()
    {
        var capped = LabPbo.Apply(new PboOptions(ConfidenceMode.Gate), Verdict("PASS", Figure(4.0 / 6.0)));

        capped.Outcome.ShouldBe("MARGINAL");
        capped.Limits.Count.ShouldBe(2);
        capped.Limits[0].ShouldBe("a limit");
        capped.Limits[1].ShouldContain("probability of backtest overfitting 0.6667");
        capped.Robustness!.Pbo!.Capped.ShouldBeTrue();
        capped.Gates.ShouldBeEmpty("the six gates are untouched");
    }

    [Theory]
    [InlineData("FAIL", 0.9)]
    [InlineData("MARGINAL", 0.9)]
    [InlineData("PASS", 0.5)]
    [InlineData("PASS", 0.2)]
    [InlineData("PASS", null)]
    public void A_fail_a_marginal_a_figure_at_or_below_the_limit_or_an_unavailable_one_change_nothing_in_gate_mode(string outcome, double? pbo)
    {
        var verdict = Verdict(outcome, Figure(pbo));

        LabPbo.Apply(new PboOptions(ConfidenceMode.Gate), verdict).ShouldBeSameAs(verdict);
    }

    [Theory]
    [InlineData(ConfidenceMode.Off)]
    [InlineData(ConfidenceMode.ReportOnly)]
    public void Outside_gate_mode_the_verdict_is_returned_as_it_came(ConfidenceMode mode)
    {
        var verdict = Verdict("PASS", Figure(0.99, mode.ToString()));

        LabPbo.Apply(new PboOptions(mode), verdict).ShouldBeSameAs(verdict);
    }

    [Fact]
    public void A_verdict_without_a_robustness_block_or_figure_is_returned_as_it_came_in_gate_mode()
    {
        var plain = Verdict("PASS", null);

        LabPbo.Apply(new PboOptions(ConfidenceMode.Gate), plain).ShouldBeSameAs(plain);
        var none = plain with { Robustness = null };
        LabPbo.Apply(new PboOptions(ConfidenceMode.Gate), none).ShouldBeSameAs(none);
    }

    [Fact]
    public void The_limit_in_the_options_is_the_one_applied()
    {
        var tight = new PboOptions(ConfidenceMode.Gate, MaxPbo: 0.1);

        LabPbo.Apply(tight, Verdict("PASS", Figure(0.12))).Outcome.ShouldBe("MARGINAL");
        LabPbo.Apply(tight, Verdict("PASS", Figure(0.1))).Outcome.ShouldBe("PASS");
    }
}
