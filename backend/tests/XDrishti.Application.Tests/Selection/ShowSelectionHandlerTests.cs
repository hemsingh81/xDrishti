using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Selection;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Selection;

/// <summary>The cell-level selection view (spec P7-01): states come from the changelog, instrument-level inputs are unavailable so a cell is held, a research cell is rejected, nothing is written.</summary>
public sealed class ShowSelectionHandlerTests
{
    private static readonly DateOnly Date = new(2026, 10, 26);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ICellStatsStore _stats = Substitute.For<ICellStatsStore>();

    private readonly ILearningChangelog _log = Substitute.For<ILearningChangelog>();

    private static StoredCellStats Row(string strategy, DateOnly asOf)
    {
        var cell = new CellId(strategy, "15m", "BUY", "intraday", "fixed");
        var row = new CellStatsRow(cell, CellWindow.All, asOf, true, 40, 20, 40, 0.5, 0.5, 0.55, 0.5, 0.6, 2, -1, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);
        return new StoredCellStats("snap", DateTimeOffset.UnixEpoch, new CellStatsOptions(), "sources", row);
    }

    private static ChangelogEntry ToCandidate(string cell) =>
        new(1, new NewChangelogEntry("transition", "cell", cell, "research", "candidate", "gates", "owner", [], null, null, null, null, DateTimeOffset.UnixEpoch));

    private ShowSelectionHandler Handler() => new(_stats, _log, NullLogger<ShowSelectionHandler>.Instance);

    [Fact]
    public async Task A_candidate_cell_is_held_for_want_of_instrument_inputs_and_a_research_cell_is_rejected()
    {
        var a = Row("A", new DateOnly(2026, 10, 23));
        var b = Row("B", new DateOnly(2026, 10, 23));
        _stats.ListLatestAsync(null, "all", Ct).Returns([a, b]);
        _log.HistoryAsync(a.Row.Cell.Key, Ct).Returns([ToCandidate(a.Row.Cell.Key)]);
        _log.HistoryAsync(b.Row.Cell.Key, Ct).Returns([]);

        var result = await Handler().HandleAsync(new ShowSelectionQuery(Date, CellState.Candidate), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Eligible.ShouldBe(0);
        result.Value.Held.ShouldBe(1);
        result.Value.Rejected.ShouldBe(1);
        result.Value.Text.ShouldContain("reward_risk unavailable");
        result.Value.Text.ShouldContain("cell_state fail");
        result.Value.Text.ShouldContain("limits:");
        await _stats.DidNotReceiveWithAnyArgs().StoreAsync(default!, Ct);
    }

    [Fact]
    public async Task Statistics_from_the_decision_date_are_not_used()
    {
        var a = Row("A", Date);
        _stats.ListLatestAsync(null, "all", Ct).Returns([a]);
        _log.HistoryAsync(a.Row.Cell.Key, Ct).Returns([ToCandidate(a.Row.Cell.Key)]);

        var result = await Handler().HandleAsync(new ShowSelectionQuery(Date, CellState.Candidate), Ct);

        result.Value.Text.ShouldContain("stats_as_of unavailable");
        result.Value.Text.ShouldContain("win_rate unavailable");
    }

    [Fact]
    public async Task Nothing_stored_prints_no_cells()
    {
        _stats.ListLatestAsync(null, "all", Ct).Returns([]);

        var result = await Handler().HandleAsync(new ShowSelectionQuery(Date, CellState.Candidate), Ct);

        result.Value.Text.ShouldContain("none stored");
        result.Value.Text.ShouldContain("0 cells");
    }

    [Fact]
    public async Task A_minimum_state_below_candidate_is_refused()
    {
        var result = await Handler().HandleAsync(new ShowSelectionQuery(Date, CellState.Research), Ct);

        result.IsFailure.ShouldBeTrue();
    }
}
