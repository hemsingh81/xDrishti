#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Paper;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Paper;

/// <summary>
/// Spec P10-04 over fake ports: the counted set, the point-in-time reference, honest states, the cost gate (a stub fact stands in for the owner's confirmation; the production default is not confirmed) and the sensitivity beside. The hand
/// fixtures (a) and (b) run end to end.
/// </summary>
public sealed class GetBacktestVsPaperHandlerTests
{
    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";
    private const string Run = "run-a";
    private static readonly DateOnly Start = new(2026, 11, 3);
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 4, 0, 0, TimeSpan.Zero);
    private static readonly EpochRow Epoch = new(1, Start, T0, "first", "the default epoch", "system");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IPaperLedgerData _ledger = Substitute.For<IPaperLedgerData>();
    private readonly IBacktestPaperData _data = Substitute.For<IBacktestPaperData>();
    private readonly ICellStatsStore _stats = Substitute.For<ICellStatsStore>();
    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly IGradeReader _grades = Substitute.For<IGradeReader>();
    private readonly ICostConfirmationFact _costs = Substitute.For<ICostConfirmationFact>();

    public GetBacktestVsPaperHandlerTests()
    {
        _ledger.EpochAsync(Arg.Any<long?>(), Ct).Returns(c => ValueTask.FromResult<EpochRow?>(c.Arg<long?>() is { } id && id != 1 ? null : Epoch));
        _costs.ConfirmedOnAsync(Arg.Any<string>(), Ct).Returns(ValueTask.FromResult<DateOnly?>(new DateOnly(2026, 10, 30)));
        _grades.ReportItemsAsync(Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<int>(), Ct).Returns(ValueTask.FromResult(new GradedPage([], false)));
        _data.ReadAsync(1, Ct).Returns(ValueTask.FromResult(new PaperCompareData([], 0, null)));
    }

    private GetBacktestVsPaperHandler Handler(ICostConfirmationFact? costs = null) =>
        new(_ledger, _data, new BacktestReferenceReader(_stats, _runs), _grades, costs ?? _costs, new FakeTimeProvider(), NullLogger<GetBacktestVsPaperHandler>.Instance);

    private static TradeCosting Costing(double rNet) =>
        new(CostSegment.IntradayEquity, "flat-10", 100, 1000m, 10000m, 10000m, LegCharges.Zero, LegCharges.Zero, 100m, 0.1, rNet);

    private static StoredRunHeader Header(string key, string? sheet) =>
        new(1, key, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", sheet, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    /// <summary>A stored snapshot built from a run of <paramref name="before"/> trades with an exit before the start (all R = <paramref name="r"/>) and <paramref name="after"/> trades inside the paper period (R = -9).</summary>
    private void Reference(int before, double r, bool costed = true, int after = 0)
    {
        var trades = new List<SimTrade>();
        for (var i = 0; i < before; i++)
        {
            trades.Add(FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Start.AddDays(-1 - i), rFilled: r, rExact: r, costing: costed ? Costing(r) : null));
        }

        for (var i = 0; i < after; i++)
        {
            trades.Add(FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Start.AddDays(i), rFilled: -9, rExact: -9, costing: costed ? Costing(-9) : null));
        }

        _runs.FindAsync(Run, Ct).Returns(ValueTask.FromResult<StoredRunHeader?>(Header(Run, costed ? "flat-10" : null)));
        _runs.StreamTradesAsync(Run, false, Ct).Returns(_ => Stream([.. trades]));
        var row = new CellStatsRow(new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR"), CellWindow.All, Start.AddDays(-2), costed, before, 0, before, null, 0.5, 0.5, 0, 1, null, null, r, null, 0, null, null, null, null, true);
        _stats.ListLatestBeforeAsync(Start, null, null, Ct).Returns(ValueTask.FromResult<IReadOnlyList<StoredCellStats>>([new StoredCellStats("snapkey1234567890", DateTimeOffset.UnixEpoch, new CellStatsOptions(), $"{Run}:intraday:FIXED_RR", row)]));
    }

    private void Paper(int n, double r, string basis = "net", string? sheet = "flat-10", string cell = Cell, int firstId = 1)
    {
        var rows = Rows(n, r, basis, sheet, cell, firstId);
        _data.ReadAsync(1, Ct).Returns(ValueTask.FromResult(new PaperCompareData(rows, 0, null)));
    }

    private static List<PaperCompareRow> Rows(int n, double r, string basis = "net", string? sheet = "flat-10", string cell = Cell, int firstId = 1) =>
        [.. Enumerable.Range(firstId, n).Select(i => new PaperCompareRow(i, i, "primary", "measured", cell, $"k{i}", T0.AddMinutes(i), T0.AddMinutes(1000 + i), r, basis, sheet))];

    private async Task<BacktestVsPaperView> View(int? draws = 2_000, ICostConfirmationFact? costs = null)
    {
        var result = await Handler(costs).HandleAsync(new GetBacktestVsPaperQuery(null, draws), Ct);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Fixture_a_end_to_end_is_met_on_both_statistics_with_the_numbers()
    {
        Reference(30, 0.5);
        Paper(60, 0.5);

        var view = await View();

        view.N.ShouldBe(60);
        view.Basis.ShouldBe("net");
        view.Paper.Mean.Number.ShouldBe(0.5);
        view.Band.P05.Number.ShouldBe(0.5);
        view.Band.Position.ShouldBe("within");
        view.Drawdown.Position.ShouldBe("within");
        view.Criteria.Expectancy.Verdict.ShouldBe("met");
        view.Criteria.Drawdown.Verdict.ShouldBe("met");
        view.EarlyWarning.ShouldBeNull();
        view.ChargeSheet.ShouldBe(new CompareChargeSheetView("flat-10", new DateOnly(2026, 10, 30)));
        view.Fan.Count.ShouldBe(60);
        view.Evidence.SnapshotKey.ShouldBe("snapkey1234567890");
        view.Cells.Single().State.ShouldBe("ok");
        view.Band.Seed.ShouldBe(BacktestBand.SeedOf(1, "snapkey1234567890", 60).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Fixture_b_end_to_end_is_not_met_below_the_band_and_warns()
    {
        Reference(30, 0.5);
        Paper(60, -0.2);

        var view = await View();

        view.Band.Position.ShouldBe("below");
        view.Criteria.Expectancy.Verdict.ShouldBe("not_met");
        view.EarlyWarning.ShouldBe(PaperExitStatistics.EarlyWarningText);
    }

    [Fact]
    public async Task The_production_default_is_not_confirmed_so_both_statistics_are_not_evaluable_and_a_confirmed_sheet_is_absent()
    {
        Reference(30, 0.5);
        Paper(60, 0.5);

        var view = await View(costs: new NotConfirmed());

        view.Criteria.Expectancy.Verdict.ShouldBe("not_evaluable");
        view.Criteria.Expectancy.Label.ShouldContain("costs not confirmed");
        view.Criteria.Drawdown.Verdict.ShouldBe("not_evaluable");
        view.ChargeSheet.ConfirmedOn.ShouldBeNull();
        view.Band.P05.State.ShouldBe("value");
    }

    [Fact]
    public async Task Fewer_than_thirty_counted_trades_show_only_the_count_and_read_no_reference()
    {
        Reference(30, 0.5);
        Paper(29, 0.5);

        var view = await View();

        view.N.ShouldBe(29);
        view.Paper.Mean.State.ShouldBe("insufficient");
        view.Paper.Mean.Number.ShouldBeNull();
        view.Band.P05.State.ShouldBe("insufficient");
        view.Band.P05.Number.ShouldBeNull();
        view.Band.Position.ShouldBeNull();
        view.Drawdown.P90.Number.ShouldBeNull();
        view.Fan.ShouldBeEmpty();
        view.Criteria.Expectancy.Verdict.ShouldBe("not_evaluable");
        _runs.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Thirty_to_fifty_nine_trades_are_provisional_and_the_statistics_are_not_evaluable()
    {
        Reference(30, 0.5);
        Paper(45, 0.5);

        var view = await View();

        view.Paper.Mean.State.ShouldBe("provisional");
        view.Paper.Mean.Reason!.ShouldContain("interim");
        view.Band.P05.State.ShouldBe("provisional");
        view.Band.P05.Number.ShouldBe(0.5);
        view.Criteria.Expectancy.Verdict.ShouldBe("not_evaluable");
    }

    [Fact]
    public async Task No_snapshot_before_the_start_makes_the_band_unavailable_but_keeps_the_paper_mean()
    {
        _stats.ListLatestBeforeAsync(Start, null, null, Ct).Returns(ValueTask.FromResult<IReadOnlyList<StoredCellStats>>([]));
        Paper(60, 0.5);

        var view = await View();

        view.Band.P05.State.ShouldBe("unavailable");
        view.Band.P05.Number.ShouldBeNull();
        view.Paper.Mean.Number.ShouldBe(0.5);
        view.Criteria.Expectancy.Verdict.ShouldBe("not_evaluable");
        view.Evidence.SnapshotKey.ShouldBeNull();
    }

    [Fact]
    public async Task A_cell_with_a_thin_or_missing_reference_is_listed_and_makes_the_band_insufficient()
    {
        Reference(10, 0.5);
        Paper(40, 0.5);
        _data.ReadAsync(1, Ct).Returns(ValueTask.FromResult(new PaperCompareData([.. Rows(40, 0.5), .. Rows(20, 0.5, cell: "ORB|15m|SELL|intraday|FIXED_RR|*", firstId: 100)], 0, null)));

        var view = await View();

        view.Band.P05.State.ShouldBe("insufficient");
        view.Band.P05.Reason!.ShouldContain(Cell);
        view.Band.P05.Reason!.ShouldContain("ORB|15m|SELL|intraday|FIXED_RR|*");
        view.Cells.Select(c => c.State).Order().ShouldBe(["no_reference", "thin_reference"]);
        view.Cells.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_paper_basis_or_sheet_that_differs_from_the_cell_is_not_evaluable()
    {
        Reference(30, 0.5);
        Paper(60, 0.5, basis: "gross", sheet: null);

        var gross = await View();
        gross.Band.P05.State.ShouldBe("not_evaluable");
        gross.Band.P05.Reason!.ShouldContain("basis differs");
        gross.Cells.Single().State.ShouldBe("basis_differs");

        Paper(60, 0.5, sheet: "other-sheet");
        var other = await View();
        other.Band.P05.State.ShouldBe("not_evaluable");
        other.Cells.Single().Reason!.ShouldContain("flat-10");
    }

    [Fact]
    public async Task Backtest_trades_inside_the_paper_period_never_enter_the_reference()
    {
        Reference(30, 0.5, after: 10);
        Paper(60, 0.5);

        var view = await View();

        view.Cells.Single().ReferenceN.ShouldBe(30);
        view.Band.P05.Number.ShouldBe(0.5);
    }

    [Fact]
    public async Task Reserve_straddle_open_and_other_epoch_entries_never_count_and_are_reported_apart()
    {
        Reference(30, 0.5);
        PaperCompareRow Odd(long id, string key, string group = "primary", string status = "measured", bool open = false) =>
            new(id, id, group, status, Cell, key, T0, open ? null : T0.AddHours(3), open ? null : -5, open ? null : "net", open ? null : "flat-10");
        _data.ReadAsync(1, Ct).Returns(ValueTask.FromResult(new PaperCompareData([.. Rows(60, 0.5), Odd(900, "r", group: "reserve"), Odd(901, "s", status: "straddle"), Odd(902, "o", open: true)], 12, null)));

        var view = await View();

        view.Counts.ShouldBe(new CompareCountsView(60, 1, 1, 1, 12));
        view.Paper.Mean.Number.ShouldBe(0.5);
    }

    [Fact]
    public async Task The_sensitivity_is_the_mean_of_graded_primary_tickets_beside_and_never_used_by_the_statistics()
    {
        Reference(30, 0.5);
        Paper(60, 0.5);
        GradedItem Item(TicketGroup group, double r) => new(1, Start, 1, group, TicketState.Proposed, new TicketGrade(GradeStatus.Graded, r, 0, 0, "stop", 1, 1, "gross, no charges", "x"));
        var items = Enumerable.Range(0, 30).Select(i => Item(TicketGroup.Primary, i % 2 == 0 ? 1.0 : -0.5)).Append(Item(TicketGroup.Reserve, 9)).ToList();
        _grades.ReportItemsAsync(Start, null, Arg.Any<int>(), Ct).Returns(ValueTask.FromResult(new GradedPage(items, false)));

        var view = await View();

        view.SensitivityAllProposals.Label.ShouldBe("simulated on plan prices, all proposals");
        view.SensitivityAllProposals.N.ShouldBe(30);
        view.SensitivityAllProposals.Mean.Number!.Value.ShouldBe(0.25, 1e-12);
        view.Criteria.Expectancy.Verdict.ShouldBe("met");
    }

    [Fact]
    public async Task The_same_inputs_give_the_same_view()
    {
        Reference(30, 0.5);
        Paper(60, 0.5);

        var a = await View();
        var b = await View();

        a.ShouldBeEquivalentTo(b);
    }

    [Fact]
    public async Task An_unknown_epoch_is_not_found_and_a_bad_draw_count_or_epoch_is_invalid()
    {
        (await Handler().HandleAsync(new GetBacktestVsPaperQuery(9, null), Ct)).Error.Code.ShouldBe(PaperErrors.NotFoundCode);
        (await Handler().HandleAsync(new GetBacktestVsPaperQuery(null, 999), Ct)).Error.Code.ShouldBe(PaperErrors.InvalidCode);
        (await Handler().HandleAsync(new GetBacktestVsPaperQuery(null, 100_001), Ct)).Error.Code.ShouldBe(PaperErrors.InvalidCode);
        (await Handler().HandleAsync(new GetBacktestVsPaperQuery(0, null), Ct)).Error.Code.ShouldBe(PaperErrors.InvalidCode);
    }

    [Fact]
    public async Task Before_the_first_epoch_the_view_is_empty_with_no_number()
    {
        _ledger.EpochAsync(null, Ct).Returns(ValueTask.FromResult<EpochRow?>(null));

        var view = await View();

        view.EpochId.ShouldBeNull();
        view.N.ShouldBe(0);
        view.Paper.Mean.Number.ShouldBeNull();
        view.Criteria.Expectancy.Verdict.ShouldBe("not_evaluable");
    }

    [Theory]
    [InlineData(30, "provisional", "not_evaluable")]
    [InlineData(59, "provisional", "not_evaluable")]
    [InlineData(60, "value", "met")]
    public async Task The_states_and_verdicts_change_exactly_at_thirty_and_sixty_trades(int n, string state, string verdict)
    {
        Reference(30, 0.5);
        Paper(n, 0.5);

        var view = await View();

        view.Paper.Mean.State.ShouldBe(state);
        view.Band.P05.State.ShouldBe(state);
        view.Criteria.Expectancy.Verdict.ShouldBe(verdict);
    }

    [Fact]
    public async Task A_basis_mismatch_together_with_a_thin_cell_reports_the_mismatch_and_lists_both_cells()
    {
        Reference(30, 0.5);
        const string Thin = "ORB|15m|SELL|intraday|FIXED_RR|*";
        _data.ReadAsync(1, Ct).Returns(ValueTask.FromResult(new PaperCompareData([.. Rows(40, 0.5, basis: "gross", sheet: null), .. Rows(20, 0.5, cell: Thin, firstId: 100)], 0, null)));

        var view = await View();

        view.Band.P05.State.ShouldBe("not_evaluable");
        view.Band.P05.Reason!.ShouldContain(Cell);
        view.Cells.Select(c => c.State).Order().ShouldBe(["basis_differs", "no_reference"]);
    }

    [Fact]
    public async Task Cells_say_not_read_when_the_reference_was_left_unread_and_unavailable_when_it_is_missing()
    {
        Paper(10, 0.5);
        var few = await View();
        few.Cells.Single().State.ShouldBe("not_read");
        few.Cells.Single().ReferenceN.ShouldBe(0);

        _stats.ListLatestBeforeAsync(Start, null, null, Ct).Returns(ValueTask.FromResult<IReadOnlyList<StoredCellStats>>([]));
        Paper(60, 0.5);
        var missing = await View();
        missing.Cells.Single().State.ShouldBe("unavailable");
        missing.Cells.Single().Reason!.ShouldContain("no cell statistics snapshot");
    }

    [Fact]
    public async Task A_truncated_grade_page_makes_the_sensitivity_provisional_and_says_how_many()
    {
        Reference(30, 0.5);
        Paper(60, 0.5);
        GradedItem Item(double r) => new(1, Start, 1, TicketGroup.Primary, TicketState.Proposed, new TicketGrade(GradeStatus.Graded, r, 0, 0, "stop", 1, 1, "gross, no charges", "x"));
        _grades.ReportItemsAsync(Start, null, Arg.Any<int>(), Ct).Returns(ValueTask.FromResult(new GradedPage([.. Enumerable.Range(0, 40).Select(_ => Item(0.5))], true)));

        var view = await View();

        view.SensitivityAllProposals.Mean.State.ShouldBe("provisional");
        view.SensitivityAllProposals.Mean.Reason!.ShouldContain("first 5000 tickets");
    }

    [Fact]
    public async Task A_cancelled_read_stops_the_draw()
    {
        Reference(30, 0.5);
        Paper(60, 0.5);
        var rows = Rows(60, 0.5);
        var snapshot = await _stats.ListLatestBeforeAsync(Start, null, null, Ct);
        var header = await _runs.FindAsync(Run, Ct);
        var trades = new List<SimTrade>();
        await foreach (var t in _runs.StreamTradesAsync(Run, false, Ct))
        {
            trades.Add(t.Trade);
        }

        _ledger.EpochAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<EpochRow?>(Epoch));
        _data.ReadAsync(1, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new PaperCompareData(rows, 0, null)));
        _stats.ListLatestBeforeAsync(Start, null, null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(snapshot));
        _runs.FindAsync(Run, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(header));
        _runs.StreamTradesAsync(Run, false, Arg.Any<CancellationToken>()).Returns(_ => Stream([.. trades]));
        _costs.ConfirmedOnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<DateOnly?>(new DateOnly(2026, 10, 30)));
        _grades.ReportItemsAsync(Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new GradedPage([], false)));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await Handler().HandleAsync(new GetBacktestVsPaperQuery(null, 2_000), cts.Token));
    }

    [Fact]
    public void An_exit_label_with_a_comma_or_colon_survives_the_stored_sources_text()
    {
        var parsed = CellSource.TryParseList("aaa:intraday:ATR{a,b},bbb:swing:X:Y,ccc:intraday:FIXED_RR");

        parsed.ShouldNotBeNull();
        parsed.Select(s => (s.RunKey, s.Style, s.Exit)).ShouldBe([("aaa", "intraday", "ATR{a,b}"), ("bbb", "swing", "X:Y"), ("ccc", "intraday", "FIXED_RR")]);
        CellSource.TryParseList("").ShouldBeNull();
        CellSource.TryParseList("nonsense").ShouldBeNull();
        CellSource.TryParse("k:swing:E,1:2")!.Exit.ShouldBe("E,1:2");
    }

    [Fact]
    public void The_handler_is_read_only_and_has_no_path_to_the_charge_sheet_confirmation_source()
    {
        var parameters = typeof(GetBacktestVsPaperHandler).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        parameters.ShouldNotContain(typeof(XDrishti.Application.Abstractions.Reporting.IChargeSheetConfirmationSource));
        parameters.ShouldNotContain(typeof(IGradeStore));
        parameters.ShouldNotContain(typeof(IPaperStore));
        typeof(ICostConfirmationFact).GetMethods().Select(m => m.Name).ShouldBe(["ConfirmedOnAsync"]);
        typeof(IBacktestPaperData).GetMethods().Select(m => m.Name).ShouldBe(["ReadAsync"]);
        typeof(IGradeReader).GetMethods().Select(m => m.Name).ShouldBe(["ReportItemsAsync"]);
    }
}
