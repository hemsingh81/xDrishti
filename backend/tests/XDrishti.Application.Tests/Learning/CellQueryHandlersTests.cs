using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The two read handlers of the cell statistics (spec P6-01): the view carries the basis, the knobs and the limits; refusals are located.</summary>
public sealed class CellQueryHandlersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StoredCellStats Stored(bool costed, bool enough) =>
        new(new string('a', 64), DateTimeOffset.UnixEpoch, new CellStatsOptions(),
            "src", new CellStatsRow(new CellId("ORB", "15m", "BUY", "intraday", "X"), CellWindow.Days60, new DateOnly(2026, 3, 10), costed, 5, 3, 4.5, 0.6, 0.5, 0.55, 0.3, 0.8, 1, -1, 0.2, 1.5, 2, 0.5, 1, 1, 2, enough));

    [Fact]
    public async Task The_list_shows_basis_evidence_knobs_and_limits_and_passes_the_filters()
    {
        var store = Substitute.For<ICellStatsStore>();
        store.ListLatestAsync("ORB", "60d", Ct).Returns([Stored(true, false)]);

        var result = await new ListCellsHandler(store).HandleAsync(new ListCellsQuery("ORB", "60d"), Ct);

        var view = result.Value.Single();
        view.CellKey.ShouldBe("ORB|15m|BUY|intraday|X|*");
        view.Window.ShouldBe("60d");
        view.Basis.ShouldContain("net R");
        view.Evidence.ShouldContain("not enough evidence");
        view.RecencyHalfLifeDays.ShouldBe(180);
        view.Limits.ShouldContain("modelled fills");
    }

    [Fact]
    public async Task A_gross_cell_says_gross_and_a_blank_strategy_is_no_filter()
    {
        var store = Substitute.For<ICellStatsStore>();
        store.ListLatestAsync(null, null, Ct).Returns([Stored(false, true)]);

        var result = await new ListCellsHandler(store).HandleAsync(new ListCellsQuery("  ", null), Ct);

        result.Value.Single().Basis.ShouldContain("gross R");
        result.Value.Single().Evidence.ShouldBe("enough");
    }

    [Fact]
    public async Task An_unknown_window_is_refused_without_asking_the_store()
    {
        var store = Substitute.For<ICellStatsStore>();

        var result = await new ListCellsHandler(store).HandleAsync(new ListCellsQuery(null, "7d"), Ct);

        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cell_is_found_or_not_found()
    {
        var store = Substitute.For<ICellStatsStore>();
        store.FindLatestAsync("a|b", Ct).Returns([Stored(true, true)]);
        store.FindLatestAsync("nope", Ct).Returns([]);
        var handler = new GetCellHandler(store);

        var found = await handler.HandleAsync(new GetCellQuery("a|b"), Ct);
        var missing = await handler.HandleAsync(new GetCellQuery("nope"), Ct);
        var blank = await handler.HandleAsync(new GetCellQuery(" "), Ct);

        found.IsSuccess.ShouldBeTrue();
        missing.Error.Code.ShouldBe(LearningErrors.CellNotFoundCode);
        blank.Error.Code.ShouldBe(LearningErrors.CellNotFoundCode);
    }
}
