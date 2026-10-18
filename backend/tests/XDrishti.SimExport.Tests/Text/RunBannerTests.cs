using System.Text;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Tests.Support;
using XDrishti.SimExport.Text;

namespace XDrishti.SimExport.Tests.Text;

/// <summary>Criteria 5 and 6 of P3-04b1: the banner and the readme of a run, in the cases the spec names.</summary>
public sealed class RunBannerTests
{
    private static readonly DateOnly Apr2024 = new(2024, 4, 1);

    private static readonly DateOnly Dec2024 = new(2024, 12, 18);

    private static readonly DateOnly End = new(2026, 4, 30);

    private static (string Name, SimulationRun Run, FillRules Rules)[] Cases()
    {
        var none = FillRules.Create(0.1, SlippageConfig.None).Value;
        var ran = FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", Apr2024, End);
        var late = FabricatedRun.Ran(FabricatedRun.IdB, "NSE:BBB", Dec2024, End);
        return
        [
            ("default fills, list run", FabricatedRun.Standard(), FillRules.Default),
            ("no slippage", FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [ran], fills: none.ToCanonicalJson()), none),
            ("basket run", FabricatedRun.Assemble(FabricatedRun.BasketOf((FabricatedRun.IdA, "NSE:AAA")), [ran]), FillRules.Default),
            ("nothing ran", FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Skipped(FabricatedRun.IdA, "NSE:AAA", SkipReason.NoBars)]), FillRules.Default),
            ("starts in April and December 2024", FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA"), (FabricatedRun.IdB, "NSE:BBB")), [ran, late]), FillRules.Default),
        ];
    }

    [Fact]
    public void The_banners_of_the_cases_equal_the_golden_file_golden()
    {
        var text = new StringBuilder();
        foreach (var (name, run, rules) in Cases())
        {
            text.Append("== ").Append(name).Append('\n').Append(RunBanner.Text(RunLabels.Of(run, rules)));
        }

        Golden.Check("banner-cases", text.ToString());
    }

    [Fact]
    public void The_readme_equals_the_golden_file_golden()
    {
        var run = FabricatedRun.Standard();

        Golden.Check("readme", RunReadme.Text(RunLabels.Of(run, FillRules.Default), "orb-20240401-20260430-31f088780475"));
    }

    [Fact]
    public void The_visible_part_and_the_limits_make_the_whole_banner()
    {
        foreach (var (_, run, rules) in Cases())
        {
            var labels = RunLabels.Of(run, rules);

            (RunBanner.Visible(labels) + RunBanner.Limits(labels)).ShouldBe(RunBanner.Text(labels));
            RunBanner.Visible(labels).Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldBe(labels.Lines);
            RunBanner.Limits(labels).Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldBe(labels.NotModelled.Select(l => "not modelled: " + l.Code + ": " + l.Text));
            RunBanner.Text(labels).ShouldEndWith("\n");
        }
    }

    [Fact]
    public void The_banner_has_no_html_character_and_no_carriage_return()
    {
        foreach (var (_, run, rules) in Cases())
        {
            var text = RunBanner.Text(RunLabels.Of(run, rules));

            text.IndexOfAny(['<', '>', '&', '"', '\r']).ShouldBe(-1);
        }
    }

    [Fact]
    public void No_banner_contains_a_word_of_a_statistic_or_a_figure_in_R()
    {
        foreach (var (name, run, rules) in Cases())
        {
            ForbiddenWords.In(RunBanner.Text(RunLabels.Of(run, rules))).ShouldBeEmpty(name);
        }
    }

    [Fact]
    public void The_first_two_lines_of_the_default_banner_say_exploration_and_modelled()
    {
        var labels = RunLabels.Of(FabricatedRun.Standard(), FillRules.Default);

        RunBanner.Visible(labels).ShouldStartWith("exploration: signal-level mechanics on past data, not evidence of an edge\nfills: modelled\n");
    }

    [Fact]
    public void The_data_line_of_the_last_case_says_about_one_point_four_to_about_two_point_one_years()
    {
        var (_, run, rules) = Cases()[4];

        RunLabels.Of(run, rules).Lines[7].ShouldContain("length from about 1.4 to about 2.1 years");
        RunLabels.Of(run, rules).Lines[7].ShouldContain("first bars 2024-04-01 to 2024-12-18, last bars 2026-04-30 to 2026-04-30");
    }

    [Fact]
    public void The_readme_starts_with_the_banner_names_the_files_and_holds_no_time_and_no_path()
    {
        var labels = RunLabels.Of(FabricatedRun.Standard(), FillRules.Default);
        var readme = RunReadme.Text(labels, "orb-x");

        readme.ShouldStartWith(RunBanner.Text(labels));
        readme.ShouldContain("trades.csv");
        readme.ShouldContain("open_at_end.csv");
        readme.ShouldContain("run.json");
        readme.ShouldContain("manifest.json");
        readme.ShouldContain("Nothing in this folder is evidence of an edge");
        readme.ShouldContain("Do not pass trades.csv or open_at_end.csv on without this notice");
        readme.ShouldNotContain("/Users/");
        readme.ShouldNotContain("\r");
        ForbiddenWords.In(readme).ShouldBeEmpty();
    }

    [Fact]
    public void The_arguments_are_checked()
    {
        var labels = RunLabels.Of(FabricatedRun.Standard(), FillRules.Default);

        Should.Throw<ArgumentNullException>(() => RunBanner.Text(null!));
        Should.Throw<ArgumentNullException>(() => RunBanner.Visible(null!));
        Should.Throw<ArgumentNullException>(() => RunBanner.Limits(null!));
        Should.Throw<ArgumentNullException>(() => RunReadme.Text(null!, "x"));
        Should.Throw<ArgumentException>(() => RunReadme.Text(labels, " "));
    }
}
