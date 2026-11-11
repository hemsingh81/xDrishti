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

    [Fact]
    public void BK_09_the_feature_unknown_counter_and_the_relative_strength_record_are_absent_for_a_run_that_does_not_read_the_features_and_written_when_it_does()
    {
        var run = FabricatedRun.Standard();
        string Record(SimulationRun r) => Sinks.Render(sink => SimExportJson.WriteRecord(sink, r));

        Record(run).ShouldNotContain("feature_unknown");
        Record(run).ShouldNotContain("relative_strength");
        Json(run).ShouldNotContain("feature_unknown");
        Json(run).ShouldNotContain("relative_strength");

        var read = run with
        {
            Instruments = [.. run.Instruments.Select(i => i with { Scans = [.. i.Scans.Select(s => s with { FeatureUnknown = 3 })] })],
            RelativeStrength = new RelativeStrengthRecord(new string('a', 64), 5, new DateOnly(2026, 3, 4), [new RelativeStrengthBasis(Guid.Empty, "Nifty 100", null, "dated")], "rsf-1", 3),
        };
        Record(read).ShouldContain("\"relative_strength\"");
        Record(read).ShouldContain("\"feature_unknown\":3");
    }

    [Fact]
    public void BK_11b_the_universe_basis_and_the_not_member_counter_are_absent_for_a_run_that_did_not_name_a_basis_and_written_when_it_did()
    {
        var run = FabricatedRun.Standard();
        string Record(SimulationRun r) => Sinks.Render(sink => SimExportJson.WriteRecord(sink, r));

        Record(run).ShouldNotContain("universe_basis");
        Record(run).ShouldNotContain("not_member");
        Json(run).ShouldNotContain("universe_basis");

        var dated = run with
        {
            UniverseBasis = new UniverseBasisRecord(Guid.Parse("11111111-1111-1111-1111-111111111111"), null, null, UniverseBasisRecord.Dated, new string('c', 64)),
            Totals = run.Totals with { Fates = run.Totals.Fates with { NotMember = 2 }, Walk = run.Totals.Walk with { NotMember = 2 } },
        };
        Record(dated).ShouldContain("\"universe_basis\":{\"universe_basket_id\":\"11111111-1111-1111-1111-111111111111\",\"universe_params_hash\":null,\"members_as_of\":null,\"universe_basis\":\"dated\",\"member_days_hash\":\"" + new string('c', 64) + "\"}");
        Record(dated).ShouldContain("\"not_member\":2");
    }

    [Fact]
    public void The_turnover_blocked_counter_is_left_out_while_it_is_zero_and_written_when_set_ADR_0230()
    {
        var run = FabricatedRun.Standard();
        string Record(SimulationRun r) => Sinks.Render(sink => SimExportJson.WriteRecord(sink, r));

        Record(run).ShouldNotContain("turnover_blocked");
        Record(run with { Totals = run.Totals with { Walk = run.Totals.Walk with { TurnoverBlocked = 4 } } }).ShouldContain("\"turnover_blocked\":4");
    }
}
