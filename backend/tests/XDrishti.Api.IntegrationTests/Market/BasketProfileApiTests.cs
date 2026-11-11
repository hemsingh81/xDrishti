using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>BK-07 on a real database: the profile and the ranking over real baskets and stored daily bars, the evidence block, the refusals and the session guard.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class BasketProfileApiTests(ApiFactory factory) : BasketsTestBase(factory)
{
    private static readonly DateOnly Start = new(2026, 9, 14);

    private static readonly DateOnly[] Sessions = [.. Enumerable.Range(0, 35).Select(i => Start.AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Take(25)];

    private const string Window = "from=2026-09-14&to=2026-10-16";

    [Fact]
    public async Task Stored_daily_bars_give_the_hand_worked_hold_the_index_return_the_basis_and_the_evidence_block()
    {
        await ImportAsync();
        var ids = await SharesAsync(3);
        await StoreDailyAsync(ids[0], i => 100 + i);
        await StoreDailyAsync(ids[1], i => 50 - (0.5 * i));
        await StoreDailyAsync(ids[2], i => 100 + (0.5 * i));
        var nifty = (await FindAsync("NSE:NIFTY")).Id;
        await StoreDailyAsync(nifty, i => 1000 + (i * 100.0 / 24));
        var basket = await CreateBasketAsync("Profile");
        await AddAsync(basket.Id, ids);

        using var response = await Client.GetAsync(new Uri($"/api/reports/baskets/{basket.Id}/profile?{Window}", UriKind.Relative), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        body.GetProperty("hold").GetProperty("number").GetDouble().ShouldBe(4.0, 1e-6);
        body.GetProperty("indexDifference").GetProperty("difference").GetProperty("number").GetDouble().ShouldBe(-6.0, 1e-6);
        body.GetProperty("membershipBasis").GetString().ShouldBe("single_snapshot");
        body.GetProperty("members").GetProperty("counted").GetInt32().ShouldBe(3);
        body.GetProperty("series").GetProperty("dates").GetArrayLength().ShouldBe(25);
        body.GetProperty("breadth").GetProperty("b20").GetProperty("share").GetProperty("state").GetString().ShouldBe("insufficient");
        body.GetProperty("regime").GetProperty("status").GetProperty("state").GetString().ShouldBe("unavailable");
        body.GetProperty("window").GetProperty("source").GetString().ShouldBe("query");
        var evidence = body.GetProperty("evidence");
        evidence.GetProperty("basis").GetString().ShouldBe("equal weight, daily rebalanced, price return, dividends not included");
        evidence.GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);

        using var again = await Client.GetAsync(new Uri($"/api/reports/baskets/{basket.Id}/profile?{Window}", UriKind.Relative), Ct);
        (await again.Content.ReadAsStringAsync(Ct)).ShouldBe(body.GetRawText());
    }

    [Fact]
    public async Task The_ranking_of_two_baskets_carries_ranks_the_overlap_and_the_evidence()
    {
        await ImportAsync();
        var ids = await SharesAsync(4);
        await StoreDailyAsync(ids[0], i => 100 + (3 * i));
        await StoreDailyAsync(ids[1], i => 100 + (3 * i));
        await StoreDailyAsync(ids[2], i => 100 + (3 * i));
        await StoreDailyAsync(ids[3], i => 100 - i);
        var nifty = (await FindAsync("NSE:NIFTY")).Id;
        await StoreDailyAsync(nifty, i => 1000 + (i * 100.0 / 24));
        var up = await CreateBasketAsync("Up");
        await AddAsync(up.Id, ids[0], ids[1], ids[2]);
        var mixed = await CreateBasketAsync("Mixed");
        await AddAsync(mixed.Id, ids[0], ids[1], ids[3]);

        using var response = await Client.GetAsync(new Uri($"/api/reports/baskets/ranking?basket={up.Id},{mixed.Id}&{Window}", UriKind.Relative), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        body.GetProperty("rankWindow").GetInt32().ShouldBe(21);
        body.GetProperty("rows")[0].GetProperty("rsRank").GetProperty("number").GetDouble().ShouldBe(1);
        body.GetProperty("rows")[1].GetProperty("rsRank").GetProperty("number").GetDouble().ShouldBe(2);
        body.GetProperty("overlap")[0].GetProperty("jaccard").GetProperty("number").GetDouble().ShouldBe(0.5, 1e-9);
        body.GetProperty("evidence").GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Refusals_carry_their_status_and_code()
    {
        var basket = await CreateBasketAsync("Short");

        var missing = await GetProblemAsync($"/api/reports/baskets/{Guid.NewGuid()}/profile?{Window}");
        var malformed = await GetProblemAsync($"/api/reports/baskets/nope/profile?{Window}");
        var shortWindow = await GetProblemAsync($"/api/reports/baskets/{basket.Id}/profile?from=2026-10-01&to=2026-10-09");
        var oneBasket = await GetProblemAsync($"/api/reports/baskets/ranking?basket={basket.Id}&{Window}");

        missing.ShouldBe((HttpStatusCode.NotFound, "reports.not_found"));
        malformed.ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.invalid"));
        shortWindow.ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.invalid"));
        oneBasket.ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.invalid"));
    }

    [Fact]
    public async Task Both_endpoints_need_a_signed_in_owner()
    {
        using var anonymous = Factory.CreateClient();

        using var profile = await anonymous.GetAsync(new Uri($"/api/reports/baskets/{Guid.NewGuid()}/profile", UriKind.Relative), Ct);
        using var ranking = await anonymous.GetAsync(new Uri("/api/reports/baskets/ranking", UriKind.Relative), Ct);

        profile.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ranking.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task StoreDailyAsync(Guid instrumentId, Func<int, double> close)
    {
        await using var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        for (var i = 0; i < Sessions.Length; i++)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete) VALUES (@id, '1D', @ts, @c, @c, @c, @c, 1000, 375, true) ON CONFLICT DO NOTHING",
                connection);
            command.Parameters.AddWithValue("id", instrumentId);
            command.Parameters.AddWithValue("ts", new DateTimeOffset(Sessions[i].ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330)).ToUniversalTime());
            command.Parameters.AddWithValue("c", close(i));
            await command.ExecuteNonQueryAsync(Ct);
        }
    }

    private async Task<(HttpStatusCode Status, string Code)> GetProblemAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (response.StatusCode, body.GetProperty("code").GetString()!);
    }
}
