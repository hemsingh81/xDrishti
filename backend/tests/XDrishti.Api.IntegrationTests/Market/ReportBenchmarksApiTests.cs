using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>P8-07 on a real database: the benchmarks endpoint over real baskets, its refusals, the evidence block and the session guard.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReportBenchmarksApiTests(ApiFactory factory) : BasketsTestBase(factory)
{
    private const string Window = "from=2026-01-05&to=2026-03-31&membersAsOf=2099-01-01";

    [Fact]
    public async Task A_basket_whose_members_have_no_bars_names_them_and_gives_no_number_with_the_evidence()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var reliance = await FindAsync("NSE:RELIANCE");
        var basket = await CreateBasketAsync("Bench");
        await AddAsync(basket.Id, tcs.Id, reliance.Id);

        using var response = await Client.GetAsync(new Uri($"/api/reports/benchmarks?basket={basket.Id}&{Window}", UriKind.Relative), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        var one = body.GetProperty("baskets")[0];
        one.GetProperty("members").GetInt32().ShouldBe(2);
        one.GetProperty("buyAndHold").GetProperty("state").GetString().ShouldBe("empty");
        one.GetProperty("excluded").EnumerateArray().Select(e => e.GetProperty("reason").GetString()).ShouldAllBe(r => r == "no_bars");
        body.GetProperty("nonAdditive").GetBoolean().ShouldBeTrue();
        body.GetProperty("window").GetProperty("source").GetString().ShouldBe("query");
        body.GetProperty("index").GetProperty("return").GetProperty("state").GetString().ShouldBe("unavailable");
        body.GetProperty("evidence").GetProperty("basis").GetString().ShouldBe("price return, dividends not included");
        body.GetProperty("evidence").GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Stored_daily_bars_give_the_hand_worked_hold_a_removed_member_still_counted_and_the_index_return()
    {
        await ImportAsync();
        var ids = await SharesAsync(4);
        ids.Length.ShouldBeGreaterThanOrEqualTo(4);
        var (a, b, c, d) = (ids[0], ids[1], ids[2], ids[3]);
        var start = new DateOnly(2026, 9, 1);
        var end = new DateOnly(2026, 10, 9);
        await StoreDailyAsync(a, (start, 100), (end, 110));
        await StoreDailyAsync(b, (start, 50), (end, 45));
        await StoreDailyAsync(c, (start, 200), (end, 250));
        await StoreDailyAsync(d, (start, 10), (end, 12));
        var nifty = (await FindAsync("NSE:NIFTY")).Id;
        await StoreDailyAsync(nifty, (start, 20000), (end, 21000));
        var three = await CreateBasketAsync("Three");
        await AddAsync(three.Id, a, b, c);
        var four = await CreateBasketAsync("Four");
        await AddAsync(four.Id, a, b, c, d);
        Factory.Clock.Pin(Now.AddDays(10));
        await ResignInAsync();
        await RemoveAsync(four.Id, d);
        var path = $"/api/reports/benchmarks?basket={three.Id},{four.Id}&from=2026-09-01&to=2026-10-09&membersAsOf=2026-10-12";

        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        var baskets = body.GetProperty("baskets");
        baskets[0].GetProperty("buyAndHold").GetProperty("number").GetDouble().ShouldBe(25.0 / 3, 1e-6);
        baskets[1].GetProperty("members").GetInt32().ShouldBe(4);
        baskets[1].GetProperty("buyAndHold").GetProperty("number").GetDouble().ShouldBe(11.25, 1e-6);
        body.GetProperty("index").GetProperty("return").GetProperty("number").GetDouble().ShouldBe(5, 1e-6);
        body.GetProperty("evidence").GetProperty("n").GetInt32().ShouldBe(4);
    }

    private async Task StoreDailyAsync(Guid instrumentId, params (DateOnly Date, double Close)[] closes)
    {
        await using var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        foreach (var (date, close) in closes)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete) VALUES (@id, '1D', @ts, @c, @c, @c, @c, 1000, 375, true) ON CONFLICT DO NOTHING",
                connection);
            command.Parameters.AddWithValue("id", instrumentId);
            command.Parameters.AddWithValue("ts", new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330)).ToUniversalTime());
            command.Parameters.AddWithValue("c", close);
            await command.ExecuteNonQueryAsync(Ct);
        }
    }

    [Fact]
    public async Task Refusals_carry_their_status_and_code()
    {
        var missing = await GetProblemAsync($"/api/reports/benchmarks?basket={Guid.NewGuid()}&{Window}");
        var invalid = await GetProblemAsync($"/api/reports/benchmarks?basket=nope&{Window}");

        missing.ShouldBe((HttpStatusCode.NotFound, "reports.not_found"));
        invalid.ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.invalid"));
    }

    [Fact]
    public async Task The_endpoint_needs_a_signed_in_owner()
    {
        using var anonymous = Factory.CreateClient();

        using var response = await anonymous.GetAsync(new Uri("/api/reports/benchmarks", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<(HttpStatusCode Status, string Code)> GetProblemAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (response.StatusCode, body.GetProperty("code").GetString()!);
    }
}
