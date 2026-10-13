using System.Net;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Portfolio.Returns;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>
/// The portfolio value history over HTTP on the real database (P2-07, ADR 0020): the series from lots and stored day aggregates with a confirmed split and an edited lot date, equal to the holdings summary at the last point,
/// one statement for the bars per request, the cache and what invalidates it, scopes, resolutions and the wire shape. Clock: Monday 2026-10-12 10:00 India time.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PortfolioHistoryApiTests(ApiFactory factory) : PortfolioHistoryTestBase(factory)
{
    [Fact]
    public async Task The_history_follows_a_confirmed_split_and_an_edited_lot_date()
    {
        var (_, alpha, _) = await TwoHoldingsAsync();
        await AddSplitAsync(alpha, "2026-10-07", 1, 2);
        await SetLotDateAsync("BETA", "2026-10-07");

        var history = await HistoryAsync();

        history.Points.Select(p => (p.Date.Day, p.Value, p.Invested)).ShouldBe(
        [
            (5, 10000m, 5000m), // ALPHA only: 100 x (200 x 0.5)
            (6, 11000m, 5000m), // 100 x (220 x 0.5): continuous across the ex-date of the 7th
            (7, 17200m, 7000m), // BETA's lot, moved to the 7th, starts here: 100 x 120 + 10 x 520
            (8, 13800m, 7000m),
            (9, 15000m, 7000m),
            (12, 18200m, 7000m), // today at the last prices: 100 x 130 + 10 x 520
        ]);
        history.Resolution.ShouldBe("day");
        history.FirstDate.ShouldBe(Oct(5));
        history.LastPriceAt.ShouldNotBeNull();
        history.Coverage.ShouldBe(new HistoryCoverage(2, 2, 0, 0));
        history.Partial.ShouldBeFalse();
    }

    [Fact]
    public async Task A_split_confirmed_after_a_first_read_changes_the_next_one_at_once()
    {
        var (_, alpha, _) = await TwoHoldingsAsync();
        var before = await HistoryAsync();
        before.Points[0].Value.ShouldBe(20000m + 5000m); // raw close 200 x 100 + BETA 500 x 10, nothing adjusted yet

        await AddSplitAsync(alpha, "2026-10-07", 1, 2);
        var after = await HistoryAsync();

        after.Points[0].Value.ShouldBe(10000m + 5000m);
    }

    [Fact]
    public async Task Editing_a_lot_date_changes_the_next_read_at_once_through_the_cache()
    {
        var (_, alpha, _) = await TwoHoldingsAsync();
        await AddSplitAsync(alpha, "2026-10-07", 1, 2);
        var cached = await HistoryAsync();
        (await HistoryAsync()).Points.Count.ShouldBe(cached.Points.Count);

        await SetLotDateAsync("BETA", "2026-10-08");
        var edited = await HistoryAsync();

        cached.Points.Select(p => p.Invested).ShouldBe([7000m, 7000m, 7000m, 7000m, 7000m, 7000m]); // both lots dated the 5th
        edited.Points.Select(p => p.Invested).ShouldBe([5000m, 5000m, 5000m, 7000m, 7000m, 7000m]);
    }

    [Fact]
    public async Task The_last_point_equals_the_summary_for_every_scope_when_all_holdings_are_covered_and_priced()
    {
        var (one, alpha, _) = await TwoHoldingsAsync();
        await AddSplitAsync(alpha, "2026-10-07", 1, 2);
        var gamma = await ShareAsync("GAMMA", "1003", "INE000G01013");
        var two = await ConnectedAccountAsync("Example Two", "1000000002");
        Dhan.Holdings.Clear();
        Dhan.Holdings.Add(Holding("GAMMA", 8, 25m, 31m, "1003", "INE000G01013"));
        await ImportAllAsync(two.Id, "2026-10-06");
        await StoreDailyAsync(gamma, (Oct(6), 26), (Oct(7), 27), (Oct(8), 28), (Oct(9), 30));
        var bucketTwo = (await (await Client.GetAsync("/api/portfolio-buckets", Ct)).ReadAsync<List<BucketView>>(Ct)).Single(b => b.Name == "Example Two · Portfolio");

        foreach (var scope in new[] { "", $"accountId={one.Id}", $"accountId={two.Id}", $"bucketId={bucketTwo.Id}" })
        {
            var history = await HistoryAsync(scope.Length == 0 ? "" : "?" + scope);
            var summary = await Client.SummaryAsync(scope.Length == 0 ? "" : "?" + scope, Ct);

            history.Coverage.Covered.ShouldBe(history.Coverage.Holdings, scope);
            history.Points[^1].Date.ShouldBe(Today, scope);
            history.Points[^1].Value.ShouldBe(summary.Returns.Value.ShouldNotBeNull(), scope);
            history.Points[^1].Invested.ShouldBe(summary.Returns.Invested, scope);
        }
    }

    [Fact]
    public async Task One_statement_reads_the_bars_of_all_instruments_per_request()
    {
        var (_, alpha, _) = await TwoHoldingsAsync();
        await AddSplitAsync(alpha, "2026-10-07", 1, 2);
        await IndexAsync("NSE:NIFTY", "Nifty 50", (Oct(5), 1000), (Oct(9), 1100));

        var uncached = await BarReadsDuringAsync(async () => await HistoryAsync());
        var cached = await BarReadsDuringAsync(async () => await HistoryAsync());
        var other = await BarReadsDuringAsync(async () => await HistoryAsync("?resolution=week"));

        uncached.ShouldBe(1);
        cached.ShouldBe(0); // served from the 60-second cache
        other.ShouldBe(1);
    }

    [Fact]
    public async Task Holdings_without_bars_are_counted_and_the_history_is_partial()
    {
        await TwoHoldingsAsync(bseOnly: true);

        var history = await HistoryAsync();

        history.Coverage.ShouldBe(new HistoryCoverage(3, 2, 1, 0)); // BSEONLY is not in the master
        history.Partial.ShouldBeTrue();
    }

    [Fact]
    public async Task A_holding_whose_bars_are_missing_is_counted_not_valued_at_zero()
    {
        var (_, _, beta) = await TwoHoldingsAsync();
        await ExecuteAsync("DELETE FROM market.bars_agg WHERE instrument_id = @id", ("id", beta));
        InvalidateHistory();

        var history = await HistoryAsync();

        history.Coverage.ShouldBe(new HistoryCoverage(2, 1, 1, 0));
        history.Points[0].Value.ShouldBe(20000m); // ALPHA alone, raw closes (no split here)
    }

    [Fact]
    public async Task The_range_and_the_resolution_are_applied_and_bad_values_are_400()
    {
        var (_, alpha, _) = await TwoHoldingsAsync();
        await AddSplitAsync(alpha, "2026-10-07", 1, 2);

        var range = await HistoryAsync("?from=2026-10-07&to=2026-10-09");
        var weekly = await HistoryAsync("?resolution=week");
        var monthly = await HistoryAsync("?resolution=month");

        range.Points.Select(p => p.Date.Day).ShouldBe([7, 8, 9]);
        weekly.Points.Select(p => p.Date.Day).ShouldBe([9, 12]);
        monthly.Points.Select(p => p.Date.Day).ShouldBe([12]);
        foreach (var query in new[] { "?from=2026-13-40", "?to=tomorrow", "?from=2026-10-09&to=2026-10-05", "?resolution=year", "?accountId=not-a-guid" })
        {
            using var response = await Client.GetAsync("/api/portfolio/history" + query, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
        }

        using var invalid = await Client.GetAsync("/api/portfolio/history?resolution=year", Ct);
        (await invalid.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.invalid");
    }

    [Fact]
    public async Task An_unknown_account_or_bucket_gives_an_empty_history_like_the_holdings_list()
    {
        await TwoHoldingsAsync();

        var unknown = await HistoryAsync($"?accountId={Guid.NewGuid()}");
        var bucket = await HistoryAsync($"?bucketId={Guid.NewGuid()}");

        unknown.Points.ShouldBeEmpty();
        bucket.Points.ShouldBeEmpty();
        (unknown.Coverage.Holdings, unknown.FirstDate, unknown.MaxDrawdown).ShouldBe((0, null, null));
    }

    [Fact]
    public async Task The_json_uses_the_documented_names()
    {
        await TwoHoldingsAsync();
        await IndexAsync("NSE:NIFTY", "Nifty 50", (Oct(5), 1000), (Oct(9), 1100));

        using var response = await Client.GetAsync("/api/portfolio/history", Ct);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

        foreach (var name in new[] { "points", "coverage", "partial", "benchmark", "maxDrawdown", "benchmarkMaxDrawdown", "firstDate", "lastPriceAt", "resolution" })
        {
            json.TryGetProperty(name, out _).ShouldBeTrue(name);
        }

        foreach (var name in new[] { "date", "value", "invested", "benchmarkValue", "nav", "benchmarkNav" })
        {
            json.GetProperty("points")[0].TryGetProperty(name, out _).ShouldBeTrue(name);
        }

        foreach (var name in new[] { "holdings", "covered", "unpriced", "lateStart" })
        {
            json.GetProperty("coverage").TryGetProperty(name, out _).ShouldBeTrue(name);
        }

        foreach (var name in new[] { "state", "instrumentId", "symbol", "name", "tracked", "hasBars", "reason" })
        {
            json.GetProperty("benchmark").TryGetProperty(name, out _).ShouldBeTrue(name);
        }

        foreach (var name in new[] { "pct", "from", "to" })
        {
            json.GetProperty("maxDrawdown").TryGetProperty(name, out _).ShouldBeTrue(name);
        }

        json.GetProperty("points")[0].GetProperty("date").GetString().ShouldBe("2026-10-05");
        json.GetProperty("benchmark").GetProperty("state").GetString().ShouldBe("ok");
    }

    [Fact]
    public async Task A_computed_history_is_logged_with_sizes_and_no_figures()
    {
        await TwoHoldingsAsync();

        await HistoryAsync();

        var line = Factory.Logs.All.Last(l => l.Contains("Portfolio history computed", StringComparison.Ordinal));
        line.ShouldContain("2 holdings");
        line.ShouldContain("points in");
        AssertNoSecret(line);
    }
}
