using XDrishti.Domain.Research;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests;

/// <summary>CF-05 (ADR 0229): the <c>tier_excluded</c> counter is in the run record only when a conservative run excluded a candidate, so the record of every other run is what it was. The default record is pinned byte for byte by the goldens <c>run-json.golden.txt</c> (<c>SimExportJsonTests</c>) and <c>run-json-costed.golden.txt</c> (<c>CostedExportTests</c>), which this change did not touch; this test checks the same absence on a card run.</summary>
public sealed class TierExcludedExportTests
{
    private static string Json(SimulationRun run) =>
        System.Text.Json.JsonDocument.Parse(Sinks.Render(s => SimExportJson.Write(s, run, RunLabels.Of(run, FillRules.Default), "xd-sim-export-1"))).RootElement.GetRawText();

    [Fact]
    public void The_run_record_omits_the_zero_counter_and_writes_it_when_set()
    {
        var run = FabricatedRun.Standard();
        string Record(SimulationRun r) => Sinks.Render(sink => SimExportJson.WriteRecord(sink, r));

        Record(run).ShouldNotContain("tier_excluded");
        Record(run with { Totals = run.Totals with { Walk = run.Totals.Walk with { TierExcluded = 2 } } }).ShouldContain("\"tier_excluded\":2");
    }

    [Fact]
    public void The_counter_is_left_out_while_it_is_zero_and_written_when_a_candidate_was_excluded()
    {
        var (run, _) = CardFixtures.Card(CardFixtures.BuyAtTarget().Trade, CardFixtures.BuyAtTarget().Bars);

        Json(run).ShouldNotContain("tier_excluded");

        var excluded = run with { Totals = run.Totals with { Walk = run.Totals.Walk with { TierExcluded = 3 }, Fates = run.Totals.Fates with { TierExcluded = 3 } } };
        Json(excluded).Split("\"tier_excluded\":3").Length.ShouldBe(3, "once in the walk counters and once in the fates");
    }
}
