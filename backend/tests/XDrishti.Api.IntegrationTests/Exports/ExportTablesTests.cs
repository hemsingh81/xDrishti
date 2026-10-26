using XDrishti.Api.Exports;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Reporting;
using XDrishti.SimExport.Csv;

namespace XDrishti.Api.IntegrationTests.Exports;

/// <summary>Spec P8-20 criteria 4 and 10 without a database: a figure that is not a value never becomes a digit, the mapper and the columns agree, and the slot lets one export run at a time and six in a minute.</summary>
public sealed class ExportTablesTests
{
    private static readonly string[] States = ["value", "provisional", "insufficient", "unavailable", "not_evaluable", "empty"];

    private static GroupView Group(Func<string, MetricView> metric) =>
        new("strategy=ORB", [new GroupPartView("strategy", "ORB")], 40, 20, metric("a"), metric("b"), metric("c"), metric("d"), metric("e"), metric("f"), metric("g"), metric("h"), metric("i"), metric("j"), metric("k"), metric("l"), metric("m"),
            new GroupEvidenceView(40, null, null, false, []));

    [Fact]
    public void A_figure_is_a_number_only_for_a_value_or_a_provisional_and_every_other_state_is_a_word_with_an_empty_number_cell()
    {
        foreach (var state in States)
        {
            foreach (var number in new double?[] { null, 0, 0.5, -1.25, double.NaN, double.PositiveInfinity })
            {
                var cells = new List<ExportCell>();
                ExportTables.Metric(cells, new MetricView(state, number, state is "value" ? null : "why"));

                cells.Count.ShouldBe(3);
                var hasNumber = state is "value" or "provisional" && number is { } n && double.IsFinite(n);
                (cells[0].Kind == ExportCellKind.Real).ShouldBe(hasNumber, $"{state} {number}");
                (hasNumber ? ExportCellKind.Real : ExportCellKind.Empty).ShouldBe(cells[0].Kind);
                cells[1].Text.ShouldBe(state is "value" or "provisional" && !hasNumber ? "unavailable" : state, "a value with no usable number is said to be unavailable, never shown as zero");
            }
        }
    }

    [Fact]
    public void A_group_row_has_as_many_cells_as_the_header_and_the_columns_are_the_same_for_the_ranked_cell()
    {
        var cells = new List<ExportCell>();
        ExportTables.Group(cells, Group(_ => new MetricView("insufficient", null, "n 9 of at least 15")));

        cells.Count.ShouldBe(ExportTables.GroupsColumns.Count);
        cells.Skip(3).Where((_, i) => i % 3 == 0).Take(13).ShouldAllBe(c => c.Kind == ExportCellKind.Empty);

        var ranked = new List<ExportCell>();
        ExportTables.WhatWorksRow(ranked, 1, Group(_ => new MetricView("value", 0.1, null)), true, null);
        ranked.Count.ShouldBe(ExportTables.WhatWorksColumns.Count);
        var unranked = new List<ExportCell>();
        ExportTables.WhatWorksRow(unranked, null, Group(_ => new MetricView("empty", null, "no trade")), null, "too few trades");
        unranked.Count.ShouldBe(ExportTables.WhatWorksColumns.Count);
        unranked[0].Kind.ShouldBe(ExportCellKind.Empty);
    }

    [Fact]
    public void The_evidence_block_names_the_basis_the_confirmation_the_as_of_every_run_every_limit_and_every_unavailable_input()
    {
        var evidence = new ReportEvidenceView("net", 12, new DateOnly(2026, 1, 5), new DateOnly(2026, 4, 30), false, ["k1"], [new RunUsedView("k1", "ORB", "basket", new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30), true, "s1", "intraday", "FIXED", 12, 3)], false, ["L1", "L2"], ["no regime"], 0, 3, "charge sheet 's1' confirmed");

        var lines = ExportTables.Evidence("report-groups", evidence, ["N1"]);

        lines.ShouldContain(l => l.StartsWith("basis: net R after charges; charge sheet confirmed by the owner: charge sheet 's1' confirmed", StringComparison.Ordinal));
        lines.ShouldContain("as-of: 2026-04-30; the last exit date in the data (the date of the data; not the time of the export)");
        lines.ShouldContain(l => l.StartsWith("run: k1 strategy ORB universe basket window 2026-01-01 to 2026-04-30 costed", StringComparison.Ordinal));
        lines.Where(l => l.StartsWith("limit: ", StringComparison.Ordinal)).ShouldBe(["limit: L1", "limit: L2"]);
        lines.ShouldContain("unavailable input: no regime");
        lines.ShouldContain("note: N1");
        lines.ShouldContain(l => l.Contains("population: n=12; runs=1", StringComparison.Ordinal));
    }

    [Fact]
    public void One_export_runs_at_a_time_six_start_in_a_minute_and_the_minute_slides()
    {
        var clock = new AdjustableTimeProvider();
        var slot = new ExportSlot(clock);

        slot.TryEnter().ShouldBeTrue();
        slot.TryEnter().ShouldBeFalse("one at a time");
        slot.Leave();
        for (var i = 1; i < ExportSlot.PerMinute; i++)
        {
            slot.TryEnter().ShouldBeTrue();
            slot.Leave();
        }

        slot.TryEnter().ShouldBeFalse("the seventh in a minute");
        clock.Advance(TimeSpan.FromSeconds(61));
        slot.TryEnter().ShouldBeTrue("a minute later");
        slot.Leave();
        slot.Reset();
        slot.TryEnter().ShouldBeTrue();
    }
}
