using XDrishti.Domain.Research;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Tests.Support;
using XDrishti.SimExport.Text;

namespace XDrishti.SimExport.Tests;

/// <summary>CF-04 C (ADR 0271): run.json and README.txt carry the depth and survivorship lines only for a run with an instrument of 3 years or more; the standard run (about 2.1 years) is what it was.</summary>
public sealed class HistoryDepthExportTests
{
    private static string RunJson(SimulationRun run) => Sinks.Render(s => SimExportJson.Write(s, run, RunLabels.Of(run, FillRules.Default), "xd-sim-export-1"));

    private static SimulationRun Deep() =>
        FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", new DateOnly(2021, 5, 3), new DateOnly(2026, 4, 30))]);

    [Fact]
    public void A_run_under_three_years_has_no_depth_or_survivorship_text_in_run_json_or_readme()
    {
        var run = FabricatedRun.Standard();

        RunJson(run).ShouldNotContain("depth:");
        RunJson(run).ShouldNotContain("survivorship:");
        RunReadme.Text(RunLabels.Of(run, FillRules.Default), "r").ShouldNotContain("depth:");
    }

    [Fact]
    public void A_run_over_three_years_has_both_lines_in_run_json_and_readme_and_the_record_text_keeps_its_fields()
    {
        var run = Deep();
        var json = RunJson(run);

        json.ShouldContain("depth: history from 2021-05-03");
        json.ShouldContain("survivorship: members as of today");
        RunReadme.Text(RunLabels.Of(run, FillRules.Default), "r").ShouldContain("survivorship: members as of today");
        Sinks.Render(s => SimExportJson.WriteRecord(s, run)).ShouldNotContain("depth");
    }
}
