using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>
/// CF-12 part 12a (ADR 0218) on a real database: both routes need a session, the decay route refuses a missing or malformed strategy and says 404 when there is no series, a seeded edge series is read (without a backtest reference it is "unavailable" with the reason and
/// the kill rules are not evaluated rather than fine), and the loop route says what has no source yet. Both are pure reads: no row is written.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ConfidenceApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Loop = new("/api/confidence/loop", UriKind.Relative);

    private static readonly DateOnly Session = new(2099, 3, 4);

    private async Task SetDatabaseClockAsync(DateTimeOffset? at)
    {
        var body = at is { } t ? $"SELECT '{t.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}+00'::timestamptz" : "SELECT clock_timestamp()";
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, $"CREATE OR REPLACE FUNCTION paper.shadow_now() RETURNS timestamptz LANGUAGE sql VOLATILE AS $fn$ {body} $fn$");
    }

    private async Task SeedGradedLossesAsync(int count)
    {
        await SetDatabaseClockAsync(new DateTimeOffset(2099, 3, 4, 2, 0, 0, TimeSpan.Zero));
        await using var connection = await OpenAsync();
        var plan = await ScalarAsync<long>(
            "INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at) VALUES (@k, @d, @c, 1, repeat('a', 64), 'b', true, 'n', 'l', 'probe', @at) RETURNING id",
            ("k", "dec-" + Guid.NewGuid().ToString("N")), ("d", Session), ("c", new DateTimeOffset(2099, 3, 4, 3, 35, 0, TimeSpan.Zero)), ("at", new DateTimeOffset(2099, 3, 3, 15, 40, 0, TimeSpan.Zero)));
        await ExecuteAsync(connection, "INSERT INTO paper.shadow_sessions (session_date, n_rows, status, digest, prev_digest, written_at) VALUES (@d, @n, 'ok', repeat('a', 64), NULL, '2000-01-01')", ("d", Session), ("n", count));
        for (var i = 0; i < count; i++)
        {
            var item = await ScalarAsync<long>(
                "INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable) VALUES (@p, @r, 'primary', 's', 'BUY', 'DEC', 'h', 't', 's', 't', 'v', '{}', 'c', 1, 'd', 'b', '{}') RETURNING id",
                ("p", plan), ("r", i + 1));
            await ExecuteAsync(
                connection,
                "INSERT INTO paper.shadow_entries (session_date, plan_id, plan_item_id, trade_key, ticket_group, cell_key, strategy_hash, symbol, side, time_rule, counts_for_edge, written_at, row_hash) VALUES (@d, @p, @i, @k, 'primary', 'c', repeat('a', 64), 's', 'BUY', 'v', true, '2000-01-01', repeat('b', 64))",
                ("d", Session), ("p", plan), ("i", item), ("k", $"DEC|S{i}|Buy|2099-03-04"));
        }

        await SetDatabaseClockAsync(new DateTimeOffset(2099, 3, 4, 11, 0, 0, TimeSpan.Zero));
        await ExecuteAsync(
            connection,
            "INSERT INTO paper.shadow_grades (entry_id, status, r_sim, r_basis, r_gross, engine_version, graded_at) SELECT id, 'graded', -1.0, 'gross', -1.0, 'sim-6', '2000-01-01' FROM paper.shadow_entries");
    }

    public override async ValueTask DisposeAsync()
    {
        await SetDatabaseClockAsync(null);
        await base.DisposeAsync();
    }

    [Theory]
    [InlineData("/api/confidence/decay?strategy=DEC")]
    [InlineData("/api/confidence/loop")]
    public async Task Without_a_session_the_routes_are_401(string route)
    {
        using var response = await Factory.CreateAnonymousClient().GetAsync(new Uri(route, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?strategy=")]
    [InlineData("?strategy=a|b")]
    public async Task A_missing_or_malformed_strategy_is_400(string query)
    {
        using var response = await Client.GetAsync(new Uri("/api/confidence/decay" + query, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("code").GetString().ShouldBe("paper.invalid");
    }

    [Fact]
    public async Task A_strategy_with_no_shadow_row_has_no_series_and_the_answer_is_404()
    {
        using var response = await Client.GetAsync(new Uri("/api/confidence/decay?strategy=NOPE", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("code").GetString().ShouldBe("paper.not_found");
    }

    [Fact]
    public async Task A_seeded_edge_series_without_a_reference_is_unavailable_with_the_reason_and_changes_nothing()
    {
        await SeedGradedLossesAsync(6);
        var before = await ScalarAsync<long>("SELECT (SELECT count(*) FROM paper.shadow_entries) + (SELECT count(*) FROM paper.shadow_grades)");

        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/confidence/decay?strategy=DEC", UriKind.Relative), Ct);

        view.GetProperty("strategy").GetString().ShouldBe("DEC");
        view.GetProperty("hasSeries").GetBoolean().ShouldBeTrue();
        view.GetProperty("reference").GetProperty("state").GetString().ShouldBe("unavailable");
        var edge = view.GetProperty("edge");
        edge.GetProperty("state").GetString().ShouldBe("unavailable");
        edge.GetProperty("trades").GetInt32().ShouldBe(6);
        edge.GetProperty("note").GetString().ShouldNotBeNull().ShouldContain("unavailable");
        var kill = view.GetProperty("kill").EnumerateArray().ToDictionary(k => k.GetProperty("rule").GetString()!);
        kill.Keys.ShouldBe(["cost_stress", "drawdown", "decay_twice", "failure_boundary"]);
        kill["cost_stress"].GetProperty("state").GetString().ShouldBe("not_evaluated");
        kill["failure_boundary"].GetProperty("proposalOnly").GetBoolean().ShouldBeTrue();
        view.GetProperty("settings").GetProperty("settingsHash").GetString().ShouldNotBeNull().Length.ShouldBe(64);
        view.GetProperty("line").GetString().ShouldNotBeNull().ShouldContain("report only");
        (await ScalarAsync<long>("SELECT (SELECT count(*) FROM paper.shadow_entries) + (SELECT count(*) FROM paper.shadow_grades)")).ShouldBe(before);
    }

    [Fact]
    public async Task The_loop_report_says_unavailable_for_every_section_with_the_reason_and_the_settings()
    {
        var view = await Client.GetFromJsonAsync<JsonElement>(Loop, Ct);

        foreach (var section in new[] { "arms", "curve", "slope", "lift" })
        {
            view.GetProperty(section).GetProperty("state").GetString().ShouldBe("unavailable", section);
            view.GetProperty(section).GetProperty("reason").GetString().ShouldNotBeNullOrWhiteSpace(section);
        }

        view.GetProperty("settings").GetProperty("minDays").GetInt32().ShouldBe(20);
        view.GetProperty("settings").GetProperty("settingsHash").GetString().ShouldNotBeNull().Length.ShouldBe(64);
        view.GetProperty("line").GetString().ShouldNotBeNull().ShouldContain("report only");
    }
}
