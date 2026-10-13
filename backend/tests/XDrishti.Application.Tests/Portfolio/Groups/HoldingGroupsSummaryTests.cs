using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Groups;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.Groups;

/// <summary>
/// The groups against the summary on holdings imported through the real handlers (two accounts, buckets, a holding without a price, sectors from a fake master): for the additive groupings the groups add up to the
/// summary of the same scope (ADR 0021 §2). Clock: Saturday 2026-10-10, India.
/// </summary>
public sealed class HoldingGroupsSummaryTests
{
    private static readonly DateOnly TwoYearsAgo = new(2024, 10, 10);

    private readonly PortfolioFixture _fx = new();

    private async Task ImportedAsync()
    {
        var one = await _fx.ConnectedAsync("Example One", "1000000001");
        _fx.Dhan.Rows.Clear();
        _fx.Dhan.Rows.AddRange([Row("AAA", 100, 50m, 60m, "1", "INE000A00001"), Row("BBB", 10, 200m, 180m, "2", "INE000B00002")]);
        MapToMaster("AAA", "1", "INE000A00001", "Banks");
        MapToMaster("BBB", "2", "INE000B00002", "IT");
        await _fx.ImportAllAsync(one.Id, TwoYearsAgo);

        var two = await _fx.ConnectedAsync("Example Two", "1000000002");
        _fx.Dhan.Rows.Clear();
        _fx.Dhan.Rows.AddRange([Row("CCC", 5, 1000m, 1500m, "3", "INE000C00003"), Row("DDD", 20, 10m, 0m, "4", "INE000D00004"), Row("EEE", 7, 100m, 130m, "5", "INE000E00005")]);
        MapToMaster("CCC", "3", "INE000C00003", "Banks");
        MapToMaster("EEE", "5", "INE000E00005", null);
        await _fx.ImportAllAsync(two.Id, TwoYearsAgo);
    }

    private void MapToMaster(string symbol, string securityId, string isin, string? sector)
    {
        var id = Guid.NewGuid();
        _fx.Instruments.Known[Row(symbol, isin: isin, securityId: securityId).Key] = new InstrumentRef(id, symbol, symbol);
        if (sector is not null)
        {
            _fx.Classifications.Sectors[id] = sector;
        }
    }

    private async Task<HoldingGroups> GroupsAsync(string by, string? status = null, string? q = null) =>
        Ok(await _fx.Groups().HandleAsync(new GetHoldingGroupsQuery(new HoldingGroupsRequest(null, null, status, q, by, null, null)), CancellationToken.None));

    private async Task<HoldingsSummary> SummaryAsync() =>
        Ok(await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(null, null), CancellationToken.None));

    [Theory]
    [InlineData("account")]
    [InlineData("bucket")]
    [InlineData("sector")]
    public async Task The_additive_groups_add_up_to_the_summary_of_the_same_scope(string by)
    {
        await ImportedAsync();

        var groups = await GroupsAsync(by, status: "all");
        var summary = await SummaryAsync();

        groups.Groups.Sum(g => g.Invested).ShouldBe(summary.Returns.Invested);
        groups.Groups.Sum(g => g.Value ?? 0m).ShouldBe(summary.Returns.Value!.Value);
        groups.Groups.Sum(g => g.Pnl ?? 0m).ShouldBe(summary.Returns.Pnl!.Value);
        groups.Groups.Sum(g => g.Realised).ShouldBe(summary.Returns.RealisedPnl);
        groups.Groups.Sum(g => g.Unpriced).ShouldBe(summary.UnpricedCount);
        groups.Groups.Sum(g => g.Holdings).ShouldBe(summary.OpenCount + summary.ClosedCount);
        (summary.Returns.Invested, summary.Returns.Value, summary.UnpricedCount).ShouldBe((12700m, 16210m, 1));
    }

    [Theory]
    [InlineData("account")]
    [InlineData("sector")]
    [InlineData("basket")]
    public async Task The_total_row_is_the_summary_even_when_the_groups_overlap(string by)
    {
        await ImportedAsync();

        var total = (await GroupsAsync(by)).Total;
        var summary = await SummaryAsync();

        (total.Invested, total.Value, total.Pnl, total.PnlPct, total.Xirr, total.XirrState, total.Realised)
            .ShouldBe((summary.Returns.Invested, summary.Returns.Value, summary.Returns.Pnl, summary.Returns.PnlPct, summary.Returns.Xirr, summary.Returns.XirrState, summary.Returns.RealisedPnl));
        (total.DayChange, total.DayChangePct, total.DayChangeExcluded, total.Unpriced).ShouldBe((summary.DayChange, summary.DayChangePct, summary.DayChangeExcluded, summary.UnpricedCount));
    }

    [Fact]
    public async Task A_search_narrows_groups_and_total_together()
    {
        await ImportedAsync();

        var groups = await GroupsAsync("sector", q: "AAA");

        groups.Groups.Select(g => g.Key).ShouldBe(["Banks"]);
        (groups.Total.Holdings, groups.Total.Invested, groups.Groups[0].WeightPct).ShouldBe((1, 5000m, 100m));
    }

    [Fact]
    public async Task Sectors_unknown_to_the_master_and_unmapped_holdings_are_unclassified()
    {
        await ImportedAsync();

        var groups = await GroupsAsync("sector");

        var none = groups.Groups.Single(g => g.Key == "~none");
        (none.Name, none.Holdings, none.Unpriced).ShouldBe(("Unclassified", 2, 1)); // EEE has no sector in the master, DDD is not mapped (and has no price)
        none.Value.ShouldBe(910m); // EEE: 7 x 130
    }
}
