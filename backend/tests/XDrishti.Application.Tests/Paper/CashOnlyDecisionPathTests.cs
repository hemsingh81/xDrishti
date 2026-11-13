#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Paper;
using XDrishti.Application.Planning;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Paper;

/// <summary>P14-06 criterion 6 (ADR 0193): the paper configuration and the ticket replay read cash cells only; an option cell moved to Paper or Active, with a statistics row that beats cash, changes nothing.</summary>
public sealed class CashOnlyDecisionPathTests
{
    private const string Cash = "ORB|15m|BUY|intraday|FIXED_RR|*";
    private const string Option = Cash + "|*|option_buy";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ChangelogEntry Move(long id, string cell, string from, string to) =>
        new(id, new NewChangelogEntry("transition", "cell", cell, from, to, "gates", "owner", [], null, null, null, null, DateTimeOffset.UnixEpoch));

    private static IEnumerable<ChangelogEntry> Chain(string cell, long first) => [Move(first, cell, "research", "candidate"), Move(first + 1, cell, "candidate", "paper"), Move(first + 2, cell, "paper", "active")];

    private static StoredCellStats Stats(Expression expression, string snapshot, double posterior)
    {
        var cell = new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR", Expression: expression);
        var row = new CellStatsRow(cell, CellWindow.All, new DateOnly(2026, 10, 23), true, 40, 20, 40, 0.5, 0.5, posterior, 0.5, 0.6, 2, -1, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);
        return new StoredCellStats(snapshot, DateTimeOffset.UnixEpoch, new CellStatsOptions(), "run1:intraday:FIXED_RR", row);
    }

    private static StoredRunHeader Header() =>
        new(1, "run1", "sim-1", 1, 1, "ORB", "def1", new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 1), "index", "NIFTY 50", 50, 50, 10, 10, 10, 0, false, "next_open", null, null, 1, "r", "t", DateTimeOffset.UnixEpoch, "owner");

    private static async Task<List<string>> ComponentsAsync(IEnumerable<ChangelogEntry> log, params StoredCellStats[] stats)
    {
        var changelog = Substitute.For<ILearningChangelog>();
        changelog.ListAsync("cell", null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ChangelogEntry>>([.. log]));
        var cellStats = Substitute.For<ICellStatsStore>();
        cellStats.ListLatestCashAsync(null, null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<StoredCellStats>>(stats));
        var strategies = Substitute.For<IStrategyStore>();
        var runs = Substitute.For<IRunStore>();
        runs.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredRunHeader?>(Header()));
        var source = new ConfigSnapshotSource(
            Substitute.For<IPlanningSettingsReader>(), Substitute.For<IAutonomySettingsStore>(), Substitute.For<IAccountStore>(), Substitute.For<IRiskProfileStore>(), changelog, strategies, cellStats, runs,
            Substitute.For<IModelRegistry>(), Substitute.For<ISimulationEngine>(), Substitute.For<IGradeSettings>(), Substitute.For<ICostSheetStore>());

        var reading = await source.ReadAsync(Ct);

        return [.. reading.Components.Where(c => c.Key.StartsWith("cells.", StringComparison.Ordinal)).Select(c => c.Key + "=" + c.Value)];
    }

    [Fact]
    public async Task An_option_cell_in_paper_or_active_with_better_statistics_leaves_the_paper_configuration_unchanged()
    {
        var without = await ComponentsAsync(Chain(Cash, 1), Stats(Expression.Cash, "snap1", 0.55));

        var with = await ComponentsAsync([.. Chain(Cash, 1), .. Chain(Option, 10)], Stats(Expression.Cash, "snap1", 0.55), Stats(Expression.OptionBuy, "snap2", 0.95));

        with.ShouldBe(without);
        without.ShouldContain(c => c.StartsWith("cells.eligible/" + Cash, StringComparison.Ordinal), "the cash cell is in: the comparison is not vacuous");
        with.ShouldNotContain(c => c.Contains("option_buy", StringComparison.Ordinal));
        with.ShouldNotContain(c => c.Contains("snap2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_ticket_with_an_eight_part_cell_key_is_not_gradable_and_reads_no_strategy_bars_or_simulation()
    {
        var strategies = Substitute.For<IStrategyStore>();
        var baskets = Substitute.For<IBasketQueries>();
        var simulation = Substitute.For<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
        var item = new NewPlanItem(1, TicketGroup.Primary, "NSE:AAA", "BUY", "ORB", new string('a', 64), "t", "s", "g", "v", [], new PlanEvidence(Option, 30, "r", "b", null, null, [], null), []);

        var replay = await new TicketReplayer(strategies, baskets, simulation).ReplayAsync(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 3), new TradingCalendar(), item, CostChoice.Gross, Ct);

        replay.Grade.ShouldNotBeNull().Status.ShouldBe(GradeStatus.NotGradable);
        replay.Trade.ShouldBeNull();
        strategies.ReceivedCalls().ShouldBeEmpty();
        baskets.ReceivedCalls().ShouldBeEmpty();
        simulation.ReceivedCalls().ShouldBeEmpty();
    }
}
