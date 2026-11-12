using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>CF-04d decisions 9 and 10 (acceptance criteria 5 and 6): the key gains a <c>seam=</c> line only for an admitted instrument, and a run without one has the key and the labels it always had.</summary>
public sealed class PreSeamKeyAndLabelTests
{
    private static readonly string Basis = new('a', 64);

    private static RunHeader Header(UniverseRecord universe) => new(
        FabricatedRun.Strategy(), "sim-9", FabricatedRun.Monday, FabricatedRun.Monday.AddDays(4), ReplayOptions.Default, false, FabricatedRun.DefaultFills, universe, new XDrishti.Domain.Market.Calendar.TradingCalendar([]));

    private static InstrumentRun Ran(Guid id, string symbol, PreSeamRecord? preSeam) =>
        FabricatedRun.Ran(id, symbol, new DateOnly(2022, 12, 1), new DateOnly(2026, 4, 30)) with { PreSeam = preSeam };

    private static (RunHeader Header, InstrumentRun[] Instruments) World(PreSeamRecord? a, PreSeamRecord? b)
    {
        var universe = FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA"), (FabricatedRun.IdB, "NSE:BBB"));
        return (Header(universe), [Ran(FabricatedRun.IdA, "NSE:AAA", a), Ran(FabricatedRun.IdB, "NSE:BBB", b)]);
    }

    private static string Text(PreSeamRecord? a, PreSeamRecord? b)
    {
        var (header, instruments) = World(a, b);
        return RunAssembler.KeyText(header, instruments, []);
    }

    [Fact]
    public void A_run_with_no_admitted_instrument_has_no_seam_line_so_its_key_is_the_one_of_part_b()
    {
        Text(null, null).ShouldNotContain("seam=");
    }

    [Fact]
    public void An_admitted_instrument_adds_exactly_one_seam_line_after_the_instrument_lines_in_run_order()
    {
        var text = Text(new PreSeamRecord(new DateOnly(2024, 4, 2), Basis), null);

        text.ShouldContain("seam=" + FabricatedRun.IdA.ToString("D") + "|2024-04-02|" + Basis + "\n");
        text.Split('\n').Count(l => l.StartsWith("seam=", StringComparison.Ordinal)).ShouldBe(1);
        var lines = text.Split('\n').ToList();
        lines.FindIndex(l => l.StartsWith("seam=", StringComparison.Ordinal)).ShouldBe(lines.FindLastIndex(l => l.StartsWith("i=", StringComparison.Ordinal)) + 1);
    }

    [Fact]
    public void Two_admitted_instruments_are_listed_in_run_order_and_identical_runs_give_identical_keys()
    {
        var a = new PreSeamRecord(new DateOnly(2024, 4, 2), Basis);
        var b = new PreSeamRecord(new DateOnly(2024, 5, 2), new string('b', 64));

        var text = Text(a, b);
        var seams = text.Split('\n').Where(l => l.StartsWith("seam=", StringComparison.Ordinal)).ToList();

        seams.Count.ShouldBe(2);
        seams[0].ShouldContain(FabricatedRun.IdA.ToString("D"));
        seams[1].ShouldContain(FabricatedRun.IdB.ToString("D"));
        var (header, instruments) = World(a, b);
        RunAssembler.Assemble(header, [.. instruments.Select(i => new InstrumentReplay(i, [], []))]).RunKey
            .ShouldBe(RunAssembler.Assemble(header, [.. instruments.Select(i => new InstrumentReplay(i, [], []))]).RunKey);
        Text(a, null).ShouldNotBe(Text(null, null));
    }

    [Fact]
    public void The_label_line_names_the_admitted_count_and_the_reasons_in_ordinal_order_and_a_run_without_the_record_has_none()
    {
        var run = FabricatedRun.Standard();
        var withRecord = run with { BackfillAdmission = new BackfillAdmissionRecord(1, [new BackfillExclusion("seam_unresolved", 1), new BackfillExclusion("not_probed", 1), new BackfillExclusion("seam_jump", 1)]) };

        RunLabels.Of(run, FillRules.Default).Lines.ShouldNotContain(l => l.StartsWith("backfill:", StringComparison.Ordinal));
        RunLabels.Of(withRecord, FillRules.Default).Lines.ShouldContain("backfill: pre-seam bars admitted for 1 instruments (source-aware); excluded for 3 (not_probed 1, seam_jump 1, seam_unresolved 1)");
        new BackfillAdmissionRecord(2, []).Label().ShouldBe("backfill: pre-seam bars admitted for 2 instruments (source-aware); excluded for 0");
    }
}
