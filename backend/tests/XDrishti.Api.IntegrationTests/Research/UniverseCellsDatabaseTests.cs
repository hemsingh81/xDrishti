using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// BK-11 on a real database (ADR 0261): the tier tagger's read of the daily bars (tracked live shares, close x volume, nothing from the decision day itself), and the stored tier rows: the plain readers see the all-shares cells only,
/// the tier endpoint shows the tier cells, a tier key is readable as one cell, the links say which cell a reader takes, and every six-part row reads back as before with universe <c>*</c>.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class UniverseCellsDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Day = new(2026, 3, 10);

    private static readonly CellStatsOptions Options = new(RecencyHalfLifeDays: 30, PriorStrength: 20, MinTrades: 30);

    private static readonly CellId Orb = new("ORB", "15m", "BUY", "intraday", "FIXED_RR");

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.cell_stats");
    }

    private static Task Bar(NpgsqlConnection connection, Guid id, DateOnly day, double close, long volume) =>
        ExecuteAsync(
            connection,
            "INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete) VALUES (@id, '1D', @ts, @c, @c, @c, @c, @v, 375, true)",
            ("id", id), ("ts", new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330)).ToUniversalTime()), ("c", close), ("v", volume));

    [Fact]
    public async Task The_series_has_close_times_volume_of_tracked_live_shares_before_the_end_day_and_nothing_else()
    {
        var tracked = await SeedAsync("UNIVA");
        var untracked = await SeedAsync("UNIVB");
        var retired = await SeedAsync("UNIVC", retired: true);
        var index = await SeedAsync("UNIVIDX", type: "index");
        await using (var connection = await OpenAsync())
        {
            foreach (var id in new[] { tracked, untracked, retired, index })
            {
                foreach (var offset in Enumerable.Range(0, 5))
                {
                    await Bar(connection, id, Day.AddDays(-offset), 100, 10 + offset);
                }
            }

            foreach (var id in new[] { tracked, retired, index })
            {
                await ExecuteAsync(connection, "INSERT INTO market.tracked_instruments (instrument_id, active, active_since, tracked_by) VALUES (@i, true, now(), 'probe')", ("i", id));
            }
        }

        using var scope = Factory.Services.CreateScope();
        var series = await new UniverseSeriesSource(scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>()).ReadAsync(Day.AddDays(-10), Day, Ct);

        series.Keys.ShouldBe([tracked]);
        series[tracked].Select(s => (s.Date, s.Value)).ShouldBe([(Day.AddDays(-4), 1400m), (Day.AddDays(-3), 1300m), (Day.AddDays(-2), 1200m), (Day.AddDays(-1), 1100m)]);   // the bar of the day itself (offset 0) is not read
    }

    private async Task StoreAsync(int childWins, int childLosses, int parentWins = 100, int parentTotal = 200)
    {
        CellTrade T(bool win) => new(Day, win ? 1 : -1, 0, 1);
        var child = Enumerable.Repeat(T(true), childWins).Concat(Enumerable.Repeat(T(false), childLosses)).ToList();
        var rest = Enumerable.Repeat(T(true), parentWins - childWins).Concat(Enumerable.Repeat(T(false), parentTotal - parentWins - childLosses)).ToList();
        var rows = CellStatsCalculator.Compute([new CellInput(Orb, false, [.. child, .. rest])], Day, Options, universeCells: [new CellInput(Orb with { Universe = "liq_high" }, false, child)]);
        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ICellStatsStore>().StoreAsync(new CellSnapshotToStore(new string('d', 64), Day, Options, "run:intraday:FIXED_RR", rows, "test", DateTimeOffset.UnixEpoch), Ct)).ShouldBeTrue();
    }

    private async Task<JsonElement> GetAsync(string url)
    {
        using var response = await Client.GetAsync(new Uri(url, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task Stored_tier_rows_are_seen_by_the_tier_readers_only_and_the_plain_cells_read_as_before()
    {
        await StoreAsync(22, 8);

        var star = await GetAsync("/api/cells");
        var tiers = await GetAsync("/api/cells/universe");
        var one = await GetAsync("/api/cells/" + Uri.EscapeDataString("ORB|15m|BUY|intraday|FIXED_RR|*|liq_high"));

        star.GetArrayLength().ShouldBe(3);
        star.EnumerateArray().ShouldAllBe(r => r.GetProperty("universe").GetString() == "*" && r.GetProperty("cellKey").GetString() == "ORB|15m|BUY|intraday|FIXED_RR|*");
        tiers.GetArrayLength().ShouldBe(3);
        tiers.EnumerateArray().ShouldAllBe(r => r.GetProperty("universe").GetString() == "liq_high" && r.GetProperty("regime").GetString() == "*");
        one.GetArrayLength().ShouldBe(3);
        one.EnumerateArray().Single(r => r.GetProperty("window").GetString() == "all").GetProperty("winRatePosterior").GetDouble().ShouldBe(0.64, 1e-9);
    }

    [Fact]
    public async Task The_decision_readers_get_only_six_part_rows_from_a_snapshot_that_holds_tier_rows()
    {
        await StoreAsync(22, 8);
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICellStatsStore>();

        var before = await store.ListLatestBeforeAsync(Day.AddDays(1), null, null, Ct);
        var cash = await store.ListLatestCashAsync(null, null, Ct);
        var shown = XDrishti.Application.Learning.CashCells.Only(await store.ListUniverseLatestAsync(null, null, Ct), "test", null);

        before.Count.ShouldBe(3);
        before.ShouldAllBe(r => r.Row.Cell.Universe == "*" && r.Row.Cell.Key == "ORB|15m|BUY|intraday|FIXED_RR|*");
        cash.ShouldAllBe(r => r.Row.Cell.Universe == "*");
        shown.ShouldAllBe(r => r.Row.Cell.Universe == "*", "even handed tier rows, the decision filter drops them");
    }

    [Fact]
    public async Task The_links_say_which_cell_a_reader_takes_and_never_apply_anything()
    {
        await StoreAsync(22, 8);

        var links = await GetAsync("/api/cells/universe-links");

        var all = links.EnumerateArray().Single(l => l.GetProperty("window").GetString() == "all");
        all.GetProperty("reason").GetString().ShouldBe("child_better");
        all.GetProperty("readKey").GetString().ShouldBe("ORB|15m|BUY|intraday|FIXED_RR|*|liq_high");
        all.GetProperty("childN").GetInt32().ShouldBe(30);
        all.GetProperty("parentN").GetInt32().ShouldBe(200);
        all.GetProperty("observation").GetBoolean().ShouldBeFalse("30 trades are under the link minimum of 100");
        all.GetProperty("holdOut").GetString().ShouldBe("insufficient", "the tier trades all exit on one day: no two halves");
    }

    [Fact]
    public async Task Before_any_tier_snapshot_the_tier_endpoints_are_empty_and_a_bad_window_is_refused()
    {
        (await GetAsync("/api/cells/universe")).GetArrayLength().ShouldBe(0);
        (await GetAsync("/api/cells/universe-links")).GetArrayLength().ShouldBe(0);
        using var response = await Client.GetAsync(new Uri("/api/cells/universe?window=nope", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
