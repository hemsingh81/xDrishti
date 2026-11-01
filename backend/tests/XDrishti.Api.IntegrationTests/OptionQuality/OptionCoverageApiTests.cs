using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.OptionQuality;

/// <summary>
/// P14-04 criterion 8 end to end on a real database: <c>GET /api/options/coverage</c> needs a session, answers each state (usable, warn, unavailable with its reason, rolling per key), the cumulative usable chain sessions, an empty list when nothing is recorded,
/// 400 <c>options.invalid_query</c> for an unknown underlying or a bad range, and never carries a price, IV or Greek.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionCoverageApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Truncate = "TRUNCATE options.quality_results, options.quality_runs RESTART IDENTITY";

    private static readonly string[] AllowedNames = ["underlying", "sessions", "usableChainSessions", "truncated", "date", "source", "fetchKey", "checks", "usable", "code", "status", "value", "threshold", "reason"];

    private Guid _nifty;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, Truncate);
        _nifty = await SeedAsync("NIFTY", type: "index", dhanId: false);
    }

    public override async ValueTask DisposeAsync()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, Truncate);
        }

        await base.DisposeAsync();
    }

    private async Task InsertAsync(long run, string date, string source, string code, string status, string? reason = null, double? value = null, string? fetchKey = null)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO options.quality_results (run_id, session_date, underlying_id, source, fetch_key, check_code, status, reason, value, threshold, n) VALUES (@run, @d::date, @u, @source, @key, @code, @status, @reason, @value, 0.95, 10)",
            ("run", run), ("d", date), ("u", _nifty), ("source", source), ("key", (object?)fetchKey ?? DBNull.Value), ("code", code), ("status", status), ("reason", (object?)reason ?? DBNull.Value), ("value", (object?)(decimal?)(value is { } v ? (decimal)v : null) ?? DBNull.Value));
    }

    private async Task ChainDayAsync(string date, string coverage, string parity, long run = 1, string? parityReason = null)
    {
        await InsertAsync(run, date, "chain", "coverage", coverage, coverage == "unavailable" ? "not_recorded" : null, 0.97);
        await InsertAsync(run, date, "chain", "parity", parity, parityReason ?? (parity == "unavailable" ? "too_few_points" : null), 0.93);
        await InsertAsync(run, date, "chain", "greeks", "pass", null, 0);
    }

    private static Uri Route(string query) => new("/api/options/coverage" + query, UriKind.Relative);

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string query)
    {
        using var response = await Client.GetAsync(Route(query), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static IEnumerable<string> Names(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var inner in Names(property.Value))
                {
                    yield return inner;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray().SelectMany(Names))
            {
                yield return item;
            }
        }
    }

    [Fact]
    public async Task Without_a_session_the_route_is_401()
    {
        using var response = await Factory.CreateAnonymousClient().GetAsync(Route("?underlying=NIFTY"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Nothing_recorded_is_an_empty_list_with_a_zero_count_and_not_an_error()
    {
        var (status, body) = await GetAsync("?underlying=NSE:NIFTY");

        status.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("underlying").GetString().ShouldBe("NSE:NIFTY");
        body.GetProperty("sessions").GetArrayLength().ShouldBe(0);
        body.GetProperty("usableChainSessions").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Each_state_is_shown_with_its_status_value_threshold_and_reason_and_the_usable_flags_count_up()
    {
        await ChainDayAsync("2026-10-07", "pass", "pass");
        await ChainDayAsync("2026-10-08", "pass", "warn");
        await ChainDayAsync("2026-10-09", "pass", "unavailable");
        await ChainDayAsync("2026-10-06", "unavailable", "unavailable", parityReason: "no_two_sided_quote");
        await InsertAsync(1, "2026-10-07", "rolling", "coverage", "pass", null, 1, "key-1");
        await InsertAsync(1, "2026-10-07", "rolling", "stitching", "fail", null, 0.02, "key-1");

        var (status, body) = await GetAsync("?underlying=nifty&from=2026-10-06&to=2026-10-09");

        status.ShouldBe(HttpStatusCode.OK);
        var sessions = body.GetProperty("sessions").EnumerateArray().ToList();
        sessions.Select(s => (s.GetProperty("date").GetString(), s.GetProperty("source").GetString(), s.GetProperty("usable").GetBoolean())).ShouldBe([
            ("2026-10-06", "chain", false), ("2026-10-07", "chain", true), ("2026-10-07", "rolling", false), ("2026-10-08", "chain", true), ("2026-10-09", "chain", false)]);
        body.GetProperty("usableChainSessions").GetInt32().ShouldBe(2);
        var first = sessions[0].GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("code").GetString() == "coverage");
        first.GetProperty("status").GetString().ShouldBe("unavailable");
        first.GetProperty("reason").GetString().ShouldBe("not_recorded");
        var covered = sessions[1].GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("code").GetString() == "coverage");
        covered.GetProperty("value").GetDouble().ShouldBe(0.97, 0.0001);
        covered.GetProperty("threshold").GetDouble().ShouldBe(0.95, 0.0001);
        sessions[2].GetProperty("fetchKey").GetString().ShouldBe("key-1");
    }

    [Fact]
    public async Task The_newest_run_decides_and_the_count_includes_the_days_before_the_range()
    {
        await ChainDayAsync("2026-10-01", "pass", "pass", run: 1);
        await ChainDayAsync("2026-10-09", "fail", "pass", run: 1);
        await ChainDayAsync("2026-10-09", "pass", "pass", run: 2);

        var (_, body) = await GetAsync("?underlying=NSE:NIFTY&from=2026-10-09&to=2026-10-09");

        body.GetProperty("sessions").GetArrayLength().ShouldBe(1);
        body.GetProperty("sessions")[0].GetProperty("usable").GetBoolean().ShouldBeTrue();
        body.GetProperty("usableChainSessions").GetInt32().ShouldBe(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?underlying=NSE:NOPE")]
    [InlineData("?underlying=NIFTY&from=2026-10-09&to=2026-10-01")]
    [InlineData("?underlying=NIFTY&from=2025-01-01&to=2026-10-09")]
    public async Task A_missing_or_unknown_underlying_a_reversed_range_and_a_range_over_400_days_are_400_options_invalid_query(string query)
    {
        var (status, body) = await GetAsync(query);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().ShouldBe("options.invalid_query");
    }

    [Fact]
    public async Task A_range_of_exactly_400_days_is_accepted()
    {
        var (status, _) = await GetAsync("?underlying=NIFTY&from=2025-09-07&to=2026-10-11");

        status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_answer_names_no_price_iv_or_greek_field()
    {
        await ChainDayAsync("2026-10-09", "pass", "pass");
        await InsertAsync(1, "2026-10-09", "rolling", "coverage", "pass", null, 1, "key-1");

        var (_, body) = await GetAsync("?underlying=NIFTY&from=2026-10-09&to=2026-10-09");

        Names(body).Distinct().Order(StringComparer.Ordinal).ShouldAllBe(name => AllowedNames.Contains(name));
    }

    [Fact]
    public async Task The_service_is_listed_on_by_default_in_the_api_host()
    {
        var list = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/services", UriKind.Relative), Ct);

        var service = list.GetProperty("items").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "options-quality");
        service.GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }
}
