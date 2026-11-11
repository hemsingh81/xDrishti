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

        using var response = await Client.GetAsync(new Uri($"/api/reports/baskets/{basket.Id}/profile?{Window}&index={nifty}", UriKind.Relative), Ct);
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

        using var again = await Client.GetAsync(new Uri($"/api/reports/baskets/{basket.Id}/profile?{Window}&index={nifty}", UriKind.Relative), Ct);
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

        using var response = await Client.GetAsync(new Uri($"/api/reports/baskets/ranking?basket={up.Id},{mixed.Id}&{Window}&index={nifty}", UriKind.Relative), Ct);
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

    [Fact]
    public async Task The_default_index_is_nifty_500_and_the_portfolio_benchmark_stays_nifty_50()
    {
        await ImportAsync();
        var basket = await SeededBasketAsync();
        var nifty500 = await InsertIndexAsync("NSE:NIFTY_500", "Nifty 500");
        await StoreDailyAsync(nifty500, i => 1000 + (i * 200.0 / 24));

        var body = await ProfileAsync(basket);

        var index = body.GetProperty("index");
        (index.GetProperty("id").GetGuid(), index.GetProperty("isDefault").GetBoolean(), index.GetProperty("name").GetString()).ShouldBe((nifty500, true, "Nifty 500"));
        body.GetProperty("indexDifference").GetProperty("index").GetProperty("number").GetDouble().ShouldBe(20.0, 1e-6);
        using var portfolio = await Client.GetAsync("/api/portfolio/benchmark", Ct);
        (await portfolio.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("symbol").GetString().ShouldBe("NSE:NIFTY");
    }

    [Fact]
    public async Task The_owner_can_choose_another_index_with_an_audit_row_and_the_cached_answer_of_the_old_one_is_not_served()
    {
        await ImportAsync();
        var basket = await SeededBasketAsync();
        var nifty500 = await InsertIndexAsync("NSE:NIFTY_500", "Nifty 500");
        await StoreDailyAsync(nifty500, i => 1000 + (i * 200.0 / 24));
        var nifty50 = (await FindAsync("NSE:NIFTY")).Id;
        await StoreDailyAsync(nifty50, i => 1000 + (i * 100.0 / 24));
        var before = await ProfileAsync(basket);

        using var put = await Client.PutJsonAsync("/api/reports/basket-benchmark", new { instrumentId = nifty50 }, Ct);
        var after = await ProfileAsync(basket);

        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync(Ct));
        before.GetProperty("indexDifference").GetProperty("index").GetProperty("number").GetDouble().ShouldBe(20.0, 1e-6);
        after.GetProperty("index").GetProperty("isDefault").GetBoolean().ShouldBeFalse();
        after.GetProperty("indexDifference").GetProperty("index").GetProperty("number").GetDouble().ShouldBe(10.0, 1e-6);
        (await CountAsync($"SELECT count(*) FROM accounts.account_events WHERE event = 'EssentialSettingChanged' AND detail LIKE 'reports.basket_benchmark_instrument=NSE:NIFTY (was %'")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM platform.app_settings WHERE key = 'portfolio.benchmark_instrument'")).ShouldBe(0);
        using var share = await Client.PutJsonAsync("/api/reports/basket-benchmark", new { instrumentId = (await SharesAsync(1))[0] }, Ct);
        share.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Without_nifty_500_in_the_master_the_profile_is_200_with_the_reason_and_the_ranking_has_no_rank()
    {
        await ImportAsync();
        var basket = await SeededBasketAsync();
        await StoreDailyAsync((await FindAsync("NSE:NIFTY")).Id, i => 1000 + i); // Nifty 50 has bars and must not stand in

        var body = await ProfileAsync(basket);
        var other = await CreateBasketAsync("Other");
        await AddAsync(other.Id, await SharesAsync(3));
        using var ranking = await Client.GetAsync(new Uri($"/api/reports/baskets/ranking?basket={basket},{other.Id}&{Window}", UriKind.Relative), Ct);
        var rows = (await ranking.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("rows");

        body.GetProperty("index").GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("hold").GetProperty("state").GetString().ShouldBe("value");
        var alpha = body.GetProperty("risk").GetProperty("alpha");
        (alpha.GetProperty("state").GetString(), alpha.GetProperty("reason").GetString()).ShouldBe(("unavailable", "Nifty 500 is not in the instrument master"));
        rows[0].GetProperty("rsReturn21").GetProperty("state").GetString().ShouldBe("unavailable");
        rows[0].GetProperty("rsRank").TryGetProperty("number", out var rank).ShouldBeTrue();
        rank.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Nifty_500_without_daily_bars_says_so_and_the_window_is_the_calendar()
    {
        await ImportAsync();
        var basket = await SeededBasketAsync();
        await InsertIndexAsync("NSE:NIFTY_500", "Nifty 500");
        using var response = await Client.GetAsync(new Uri($"/api/reports/baskets/{basket}/profile", UriKind.Relative), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        body.GetProperty("risk").GetProperty("alpha").GetProperty("reason").GetString().ShouldBe("Nifty 500 has no stored daily bars in this window");
        body.GetProperty("window").GetProperty("fromSource").GetString().ShouldBe("calendar");
    }

    private async Task<Guid> SeededBasketAsync()
    {
        var ids = await SharesAsync(3);
        await StoreDailyAsync(ids[0], i => 100 + i);
        await StoreDailyAsync(ids[1], i => 50 - (0.5 * i));
        await StoreDailyAsync(ids[2], i => 100 + (0.5 * i));
        var basket = await CreateBasketAsync("Profile");
        await AddAsync(basket.Id, ids);
        return basket.Id;
    }

    private async Task<JsonElement> ProfileAsync(Guid basket)
    {
        using var response = await Client.GetAsync(new Uri($"/api/reports/baskets/{basket}/profile?{Window}", UriKind.Relative), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        return body;
    }

    private async Task<Guid> InsertIndexAsync(string canonical, string displayName)
    {
        var id = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at) VALUES (@id, @symbol, 'NSE', 'IDX_I', 'index', @name, @name, 1, @symbol, false, now(), now())",
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("symbol", canonical);
        command.Parameters.AddWithValue("name", displayName);
        await command.ExecuteNonQueryAsync(Ct);
        return id;
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
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
