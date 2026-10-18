using System.Globalization;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>Criteria 5 and 6 of P3-04b1: the labels of a run, built from the record and the fill rules it ran with, and the limit texts that must not read like a statistic.</summary>
public sealed class RunLabelsTests
{
    private static readonly DateOnly Apr2024 = new(2024, 4, 1);

    private static readonly DateOnly Dec2024 = new(2024, 12, 18);

    private static readonly DateOnly End = new(2026, 4, 30);

    private static SimulationRun Run(UniverseRecord universe, params InstrumentRun[] instruments) => FabricatedRun.Assemble(universe, instruments);

    private static SimulationRun TwoRan() => Run(
        FabricatedRun.BasketOf((FabricatedRun.IdA, "NSE:AAA"), (FabricatedRun.IdB, "NSE:BBB")),
        FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", Apr2024, End),
        FabricatedRun.Ran(FabricatedRun.IdB, "NSE:BBB", Dec2024, End));

    [Fact]
    public void The_default_fills_give_the_eleven_lines_in_order()
    {
        var run = TwoRan();

        var labels = RunLabels.Of(run, FillRules.Default);

        labels.Lines.ShouldBe(
        [
            "exploration: signal-level mechanics on past data, not evidence of an edge",
            "fills: modelled",
            "entry limit: +0.1 %",
            "costs: none",
            "portfolio limits: not simulated",
            "sample: in-sample",
            "universe: members as of today; survivorship is bounded and disclosed, not avoided (ISS-22)",
            "data: 2 instruments; first bars 2024-04-01 to 2024-12-18, last bars 2026-04-30 to 2026-04-30; length from about 1.4 to about 2.1 years (days / 365.25, one decimal); no hold-out (ISS-19)",
            "engine: not yet verified (P3-08)",
            "tick: today's master tick; rounding to the grid is approximate",
            "opening bar: the stored 09:15 bar can be widened by the CSV importer and the first 1-minute open is not the auction open (ISS-21, ISS-18)",
        ]);
        labels.NotModelled.ShouldBe(FillLimits.NotModelled);
        RunLabels.EngineVerification.ShouldBe(labels.Lines[8]);
    }

    [Fact]
    public void Without_slippage_the_label_says_modelled_and_that_the_tick_rounding_is_off_too()
    {
        var rules = FillRules.Create(0.1, SlippageConfig.None).Value;
        var run = FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", Apr2024, End)], fills: rules.ToCanonicalJson());

        var lines = RunLabels.Of(run, rules).Lines;

        lines[1].ShouldBe("fills: modelled, no slippage, no tick rounding of fills");
        lines[2].ShouldBe("entry limit: +0.1 %");
    }

    [Fact]
    public void A_list_run_says_instruments_as_listed()
    {
        var run = Run(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", Apr2024, End));

        RunLabels.Of(run, FillRules.Default).Lines[6].ShouldBe("universe: instruments as listed; survivorship is bounded and disclosed, not avoided (ISS-22)");
    }

    [Fact]
    public void A_run_in_which_nothing_ran_says_data_none()
    {
        var run = Run(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), FabricatedRun.Skipped(FabricatedRun.IdA, "NSE:AAA", SkipReason.NoBars));

        RunLabels.Of(run, FillRules.Default).Lines[7].ShouldBe("data: none");
    }

    [Fact]
    public void A_skipped_instrument_that_was_read_is_not_counted_in_the_data_line()
    {
        var read = FabricatedRun.Ran(FabricatedRun.IdB, "NSE:BBB", Dec2024, End) with { Skipped = SkipReason.FeaturesNotReady };
        var run = Run(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA"), (FabricatedRun.IdB, "NSE:BBB")), FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", Apr2024, End), read);

        RunLabels.Of(run, FillRules.Default).Lines[7].ShouldStartWith("data: 1 instruments; first bars 2024-04-01 to 2024-04-01");
    }

    [Fact]
    public void The_years_are_days_over_365_25_to_one_decimal_whatever_the_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            comma.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = comma;

            RunLabels.Of(TwoRan(), FillRules.Default).Lines[7].ShouldContain("length from about 1.4 to about 2.1 years");
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Rules_that_do_not_match_the_run_are_refused()
    {
        var run = TwoRan();
        var other = FillRules.Create(0.25, SlippageConfig.Default).Value;

        Should.Throw<ArgumentException>(() => RunLabels.Of(run, other)).ParamName.ShouldBe("rules");
        Should.Throw<ArgumentNullException>(() => RunLabels.Of(run, null!));
        Should.Throw<ArgumentNullException>(() => RunLabels.Of(null!, FillRules.Default));
    }

    [Fact]
    public void The_three_fixed_labels_are_the_ones_the_header_has_always_had()
    {
        RunLabels.FixedLabels.ShouldBe(["costs: none", "portfolio limits: not simulated", "sample: in-sample"]);
    }

    [Fact]
    public void No_limit_text_is_empty_has_a_figure_in_R_or_a_word_of_a_statistic()
    {
        foreach (var limit in FillLimits.NotModelled)
        {
            limit.Text.ShouldNotBeNullOrWhiteSpace(limit.Code);
            ForbiddenWords.In(limit.Text).ShouldBeEmpty(limit.Code);
        }
    }

    [Fact]
    public void The_word_scan_does_find_a_word_a_figure_and_does_not_trip_on_a_longer_word()
    {
        ForbiddenWords.In("about 0.1 R off every Winner").ShouldBe(["winner", "<digit> R"], ignoreOrder: true);
        ForbiddenWords.In("a gain of 2R, +1R or 0.5 R").ShouldBe(["<digit> R"]);
        ForbiddenWords.In("the window, a summary and TATACONSUM").ShouldBeEmpty();
        ForbiddenWords.List.Count.ShouldBe(25);
    }

    [Fact]
    public void The_touch_is_fill_text_has_no_number()
    {
        FillLimits.NotModelled.Single(l => l.Code == "touch_is_fill").Text.ShouldNotContain("0.1");
    }
}
