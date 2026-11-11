using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>BK-11 criteria 6 and 11: a tier cell is read against its all-shares parent, both rows stay as stored, and an edge is only an observation (never applied, hold-out not evaluated).</summary>
public sealed class UniverseLinksHandlerTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 31);

    private static readonly CellStatsOptions Options = new(RecencyHalfLifeDays: 30, PriorStrength: 20, MinTrades: 30);

    private static readonly CellId Orb = new("ORB", "15m", "BUY", "intraday", "FIXED_RR");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // spread over ten days so a median split has two halves
    private static List<CellTrade> Trades(int wins, int losses) =>
        [.. Enumerable.Range(0, wins).Select(i => new CellTrade(AsOf.AddDays(-(i % 10)), 1, 0, 1)), .. Enumerable.Range(0, losses).Select(i => new CellTrade(AsOf.AddDays(-(i % 10)), -1, 0, 1))];

    private static IReadOnlyList<StoredCellStats> Stored(int childWins, int childLosses, int parentWins, int parentLosses)
    {
        var child = Trades(childWins, childLosses);
        var star = new CellInput(Orb, false, [.. child, .. Trades(parentWins - childWins, parentLosses - childLosses)]);
        var tier = new CellInput(Orb with { Universe = "liq_high" }, false, child);
        return [.. CellStatsCalculator.Compute([star], AsOf, Options, universeCells: [tier]).Select(r => new StoredCellStats(new string('s', 64), DateTimeOffset.UnixEpoch, Options, "x", r))];
    }

    private static async Task<IReadOnlyList<UniverseLinkView>> Read(IReadOnlyList<StoredCellStats> rows, int linkMin = 100)
    {
        var store = Substitute.For<ICellStatsStore>();
        store.ListUniverseLatestAsync(null, null, Ct).Returns(rows);
        var settings = Substitute.For<IUniverseSettingsStore>();
        settings.CurrentAsync(Ct).Returns(new UniverseSettingsState(new UniverseSettings(30, linkMin, UniverseSettings.Defaults.RegisteredBaskets), []));
        var result = await new GetUniverseLinksHandler(store, settings).HandleAsync(new GetUniverseLinksQuery(null), Ct);
        return result.Value;
    }

    [Fact]
    public async Task A_better_tier_with_enough_trades_is_listed_as_an_observation_and_names_both_bounds()
    {
        var views = await Read(Stored(88, 32, 100, 100));   // child 88 of 120 in a parent of 200: the rest is 12 wins of 80

        var all = views.Single(v => v.Window == "all");

        all.Reason.ShouldBe(CellChoice.ChildBetter);
        all.ReadKey.ShouldBe("ORB|15m|BUY|intraday|FIXED_RR|*|liq_high");
        all.ParentKey.ShouldBe("ORB|15m|BUY|intraday|FIXED_RR|*");
        all.Observation.ShouldBeTrue();
        all.HoldOut.ShouldBe("stable");
        (all.HoldOutFirstN + all.HoldOutSecondN).ShouldBe(120);
        all.ChildLower.ShouldBeGreaterThan(all.ParentLower!.Value);
        all.Text.ShouldContain("an observation, nothing is applied");
    }

    [Fact]
    public async Task A_better_tier_below_the_link_minimum_is_read_but_not_an_observation()
    {
        var views = await Read(Stored(22, 8, 100, 100));

        var all = views.Single(v => v.Window == "all");

        all.Reason.ShouldBe(CellChoice.ChildBetter);
        all.Observation.ShouldBeFalse();
        all.HoldOut.ShouldBe("insufficient");   // 30 trades are under 30 per half
    }

    [Fact]
    public async Task A_tier_no_better_than_the_parent_says_so_and_a_thin_one_is_not_enough_evidence()
    {
        var same = (await Read(Stored(16, 14, 100, 100))).Single(v => v.Window == "all");
        var thin = (await Read(Stored(25, 4, 100, 100))).Single(v => v.Window == "all");

        same.Reason.ShouldBe(CellChoice.NoBetterThanParent);
        same.ReadKey.ShouldBe(same.ParentKey);
        same.Text.ShouldContain("no better than all shares");
        thin.Reason.ShouldBe(CellChoice.ChildThin);
        thin.Text.ShouldContain("not enough evidence");
        thin.Observation.ShouldBeFalse();
    }

    [Fact]
    public async Task No_tier_rows_gives_an_empty_list()
    {
        (await Read([])).ShouldBeEmpty();
    }
}
