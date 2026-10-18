using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// Criterion 17 of P3-03c at the level of the assembler: the <c>costs=</c> line of the run key, what changes it (the sheet's id or rates, the capital, any profile number, <c>max_cost_R</c>, gross against sheet) and what does not (the spelling of
/// the sheet's text, its label, its source and its note), and the record that carries the <see cref="CostRecord"/>. The labels of a costed run come from the record.
/// </summary>
public sealed class CostRunKeyTests
{
    private static readonly UniverseRecord Universe = FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA"));

    private static InstrumentRun[] Instruments => [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", new DateOnly(2024, 4, 1), new DateOnly(2026, 4, 30))];

    private static SimulationRun Run(CostRecord? costs) => FabricatedRun.Assemble(Universe, Instruments, costs: costs);

    private static CostRecord Record(CostSheet? sheet = null, decimal capital = 1_000_000m, SizingProfile? profile = null, decimal maxCostR = 0.10m) =>
        CostRecord.Of(CostContext.Create(sheet ?? TestCostSheet.Sheet, profile ?? SizingProfile.Standard, capital, maxCostR).Value);

    [Fact]
    public void A_gross_run_has_the_costs_none_line_and_a_costed_one_the_pinned_line_after_the_fills()
    {
        var gross = Run(null);
        var costed = Run(Record());
        var header = new RunHeader(gross.Strategy, "sim-4", gross.From, gross.To, gross.Options, false, gross.Fills, gross.Universe, new XDrishti.Domain.Market.Calendar.TradingCalendar(), Record());

        var grossText = RunAssembler.KeyText(header with { Costs = null }, gross.Instruments, gross.CalendarExceptions);
        var costedText = RunAssembler.KeyText(header, costed.Instruments, costed.CalendarExceptions);

        grossText.ShouldContain("fills=" + gross.Fills + "\ncosts=none\nuniverse=");
        costedText.ShouldContain("\ncosts=test-synthetic-1|" + TestCostSheet.Sheet.RatesSha256 + "|1000000|standard|0.5|25|0.1\nuniverse=");
        gross.Costs.ShouldBeNull();
        costed.Costs.ShouldBe(Record());
        costed.SchemaVersion.ShouldBe(4);
    }

    [Fact]
    public void The_key_changes_with_each_of_the_sheet_id_the_rates_the_capital_each_profile_number_the_limit_and_gross_against_a_sheet()
    {
        var keys = new Dictionary<string, string>
        {
            ["gross"] = Run(null).RunKey,
            ["sheet"] = Run(Record()).RunKey,
            ["id"] = Run(Record(TestCostSheet.Of("test-synthetic-2", TestCostSheet.Sheet.Schedules.ToArray()))).RunKey,
            ["rates"] = Run(Record(TestCostSheet.Of(TestCostSheet.Id, TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.IntradayB, TestCostSheet.DeliveryA), TestCostSheet.Schedule(TestCostSheet.ScheduleB, TestCostSheet.IntradayB, TestCostSheet.DeliveryB)))).RunKey,
            ["capital"] = Run(Record(capital: 1_000_001m)).RunKey,
            ["risk percent"] = Run(Record(profile: SizingProfile.Create("standard", 0.51m, 25m).Value)).RunKey,
            ["max capital percent"] = Run(Record(profile: SizingProfile.Create("standard", 0.5m, 26m).Value)).RunKey,
            ["profile name"] = Run(Record(profile: SizingProfile.Create("other", 0.5m, 25m).Value)).RunKey,
            ["max cost R"] = Run(Record(maxCostR: 0.11m)).RunKey,
        };

        keys.Values.Distinct().Count().ShouldBe(keys.Count, "every change gives a key of its own");
    }

    [Fact]
    public void The_key_is_the_same_across_two_runs_two_spellings_of_the_text_and_a_changed_label_source_or_note()
    {
        var baseline = Run(Record()).RunKey;
        var respelt = CostSheetJson.Parse(TestCostSheet.Json.Replace("0.03,", "0.030,", StringComparison.Ordinal).Replace("\"gst_pct\":18", "\"gst_pct\":18.0", StringComparison.Ordinal)).Value;
        var schedules = TestCostSheet.Sheet.Schedules;

        Run(Record()).RunKey.ShouldBe(baseline);
        Run(Record(respelt)).RunKey.ShouldBe(baseline);
        Run(Record(CostSheet.Create(TestCostSheet.Id, "another label", TestCostSheet.Sheet.Source, null, schedules).Value)).RunKey.ShouldBe(baseline);
        Run(Record(CostSheet.Create(TestCostSheet.Id, TestCostSheet.Sheet.Label, "another source", null, schedules).Value)).RunKey.ShouldBe(baseline);
        Run(Record(CostSheet.Create(TestCostSheet.Id, TestCostSheet.Sheet.Label, TestCostSheet.Sheet.Source, "checked against the bills of 2026-10", schedules).Value)).RunKey.ShouldBe(baseline, "writing the note after a successful check changes no trade and no key");
    }

    [Fact]
    public void A_costed_trade_with_no_cost_record_or_a_gross_trade_with_one_or_another_sheet_is_refused_by_the_assembler()
    {
        var costed = FabricatedRun.CostedStandard();
        var costedTrades = costed.Trades.Select(t => t.Trade).ToList();
        var gross = FabricatedRun.Standard().Trades.Select(t => t.Trade).ToList();
        var instruments = costed.Instruments;

        Should.Throw<ArgumentException>(() => FabricatedRun.Assemble(costed.Universe, instruments, costedTrades, costs: null));
        Should.Throw<ArgumentException>(() => FabricatedRun.Assemble(costed.Universe, instruments, gross, costs: costed.Costs));
        Should.Throw<ArgumentException>(() => FabricatedRun.Assemble(costed.Universe, instruments, costedTrades, costs: costed.Costs! with { SheetId = "other-sheet" }));
        FabricatedRun.Assemble(costed.Universe, instruments, costedTrades, costs: costed.Costs).Trades.Count.ShouldBe(costedTrades.Count);
        FabricatedRun.Assemble(costed.Universe, instruments, gross, costs: null).Trades.Count.ShouldBe(gross.Count);
    }

    [Fact]
    public void A_cost_record_whose_id_or_hash_cannot_be_written_into_the_key_is_refused()
    {
        var good = Record();

        Should.Throw<ArgumentException>(() => Run(good with { SheetId = "a|b\nc" }));
        Should.Throw<ArgumentException>(() => Run(good with { RatesSha256 = "abc" }));
        Should.Throw<ArgumentException>(() => Run(good with { RatesSha256 = new string('G', 64) }));
        Should.Throw<ArgumentException>(() => Run(good with { Profile = null! }));
    }

    [Fact]
    public void The_labels_of_a_costed_run_name_the_sheet_add_the_sizing_line_and_list_the_limits()
    {
        var run = Run(Record());

        var labels = RunLabels.Of(run, FillRules.Default);

        labels.Lines[3].ShouldBe("costs: sheet test-synthetic-1 (TEST sheet: synthetic rates, not the broker's) (not checked against bills)");
        labels.Lines[4].ShouldBe(CostDescription.SizingLabel(run.Costs!));
        labels.Lines[5].ShouldBe("portfolio limits: not simulated");
        labels.Lines[6].ShouldBe("sample: in-sample");
        labels.Lines.Count.ShouldBe(12);
        labels.NotModelled.ShouldBe([.. FillLimits.NotModelled, .. CostLimits.NotModelled]);
    }

    [Fact]
    public void The_labels_of_a_gross_run_are_the_eleven_of_before_and_the_fill_limits_alone()
    {
        var labels = RunLabels.Of(Run(null), FillRules.Default);

        labels.Lines.Count.ShouldBe(11);
        labels.Lines[3].ShouldBe("costs: none");
        labels.NotModelled.ShouldBe(FillLimits.NotModelled);
    }

    [Fact]
    public void The_costed_standard_run_carries_a_costed_trade_for_every_trade_and_a_record_that_can_be_recomputed()
    {
        var run = FabricatedRun.CostedStandard();

        run.Trades.ShouldAllBe(t => t.Trade.Costs == CostBasis.Charged && t.Trade.Costing != null);
        run.OpenAtEnd.ShouldAllBe(t => t.Trade.Costs == CostBasis.Charged && t.Trade.Costing != null);
        run.Costs.ShouldNotBeNull();
        CostSheetJson.Parse(run.Costs.SheetJson).Value.RatesSha256.ShouldBe(run.Costs.RatesSha256);
        run.Trades.Concat(run.OpenAtEnd).ShouldAllBe(t => t.Trade.Costing!.TotalCost == t.Trade.Costing.EntryLeg.Total + t.Trade.Costing.ExitLeg.Total);
    }
}
