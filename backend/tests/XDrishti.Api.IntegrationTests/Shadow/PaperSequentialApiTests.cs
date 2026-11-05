using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Shadow;

/// <summary>
/// CF-09 (ADR 0213) on a real database: the route needs a session, says 404 when there is no series, refuses a bad scope, and on a seeded edge series (six graded losses of one session) returns the hand numbers of the test with the design effect, the placeholder rho, the settings
/// hash and the honest statements. The database clock <c>paper.shadow_now()</c> is replaced by the owner role to seed the forward-only rows and restored after.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PaperSequentialApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Route = new("/api/paper/sequential", UriKind.Relative);

    private static readonly DateOnly Session = new(2099, 2, 3);

    private async Task SetDatabaseClockAsync(DateTimeOffset? at)
    {
        var body = at is { } t ? $"SELECT '{t.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}+00'::timestamptz" : "SELECT clock_timestamp()";
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, $"CREATE OR REPLACE FUNCTION paper.shadow_now() RETURNS timestamptz LANGUAGE sql VOLATILE AS $fn$ {body} $fn$");
    }

    private async Task SeedGradedLossesAsync(int count)
    {
        await SetDatabaseClockAsync(new DateTimeOffset(2099, 2, 3, 2, 0, 0, TimeSpan.Zero));
        await using var connection = await OpenAsync();
        var plan = await ScalarAsync<long>(
            "INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at) VALUES (@k, @d, @c, 1, repeat('a', 64), 'b', true, 'n', 'l', 'probe', @at) RETURNING id",
            ("k", "seq-" + Guid.NewGuid().ToString("N")), ("d", Session), ("c", new DateTimeOffset(2099, 2, 3, 3, 35, 0, TimeSpan.Zero)), ("at", new DateTimeOffset(2099, 2, 2, 15, 40, 0, TimeSpan.Zero)));
        await ExecuteAsync(connection, "INSERT INTO paper.shadow_sessions (session_date, n_rows, status, digest, prev_digest, written_at) VALUES (@d, @n, 'ok', repeat('a', 64), NULL, '2000-01-01')", ("d", Session), ("n", count));
        for (var i = 0; i < count; i++)
        {
            var item = await ScalarAsync<long>(
                "INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable) VALUES (@p, @r, 'primary', 's', 'BUY', 'ORB', 'h', 't', 's', 't', 'v', '{}', 'c', 1, 'd', 'b', '{}') RETURNING id",
                ("p", plan), ("r", i + 1));
            await ExecuteAsync(
                connection,
                "INSERT INTO paper.shadow_entries (session_date, plan_id, plan_item_id, trade_key, ticket_group, cell_key, strategy_hash, symbol, side, time_rule, counts_for_edge, written_at, row_hash) VALUES (@d, @p, @i, @k, 'primary', 'c', repeat('a', 64), 's', 'BUY', 'v', true, '2000-01-01', repeat('b', 64))",
                ("d", Session), ("p", plan), ("i", item), ("k", $"ORB|S{i}|Buy|2099-02-03"));
        }

        await SetDatabaseClockAsync(new DateTimeOffset(2099, 2, 3, 11, 0, 0, TimeSpan.Zero));
        await ExecuteAsync(
            connection,
            "INSERT INTO paper.shadow_grades (entry_id, status, r_sim, r_basis, r_gross, engine_version, graded_at) SELECT id, 'graded', -1.0, 'gross', -1.0, 'sim-6', '2000-01-01' FROM paper.shadow_entries");
    }

    public override async ValueTask DisposeAsync()
    {
        await SetDatabaseClockAsync(null);
        await base.DisposeAsync();
    }

    [Fact]
    public async Task Without_a_session_the_route_is_401()
    {
        using var response = await Factory.CreateAnonymousClient().GetAsync(Route, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task With_no_shadow_row_there_is_no_series_and_the_answer_is_404()
    {
        using var response = await Client.GetAsync(Route, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("code").GetString().ShouldBe("paper.not_found");
    }

    [Theory]
    [InlineData("?scope=everything")]
    [InlineData("?scope=strategy")]
    [InlineData("?scope=system&strategy=ORB")]
    [InlineData("?scope=strategy&strategy=a|b")]
    public async Task A_bad_scope_or_strategy_is_400(string query)
    {
        using var response = await Client.GetAsync(new Uri("/api/paper/sequential" + query, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_seeded_edge_series_returns_the_hand_numbers_the_placeholder_rho_the_hash_and_the_honest_statements()
    {
        await SeedGradedLossesAsync(6); // one session of 6 candidates: design effect 1 + 5 x 0.3 = 2.5

        var view = await Client.GetFromJsonAsync<JsonElement>(Route, Ct);
        var edge = view.GetProperty("edge");

        view.GetProperty("hasSeries").GetBoolean().ShouldBeTrue();
        view.GetProperty("scope").GetString().ShouldBe("system");
        edge.GetProperty("state").GetString().ShouldBe("monitoring");
        edge.GetProperty("tradesCounted").GetInt32().ShouldBe(6);
        edge.GetProperty("effectiveN").GetDouble().ShouldBe(2.4, 1e-9);
        edge.GetProperty("sum").GetDouble().ShouldBe(6 * -0.097777778 / 2.5, 1e-6);
        edge.GetProperty("basis").GetString().ShouldNotBeNull().ShouldContain("modelled fills; no queue, no latency");
        view.GetProperty("rho").GetDouble().ShouldBe(0.3);
        view.GetProperty("rhoIsPlaceholder").GetBoolean().ShouldBeTrue();
        view.GetProperty("settingsHash").GetString().ShouldNotBeNull().Length.ShouldBe(64);
        view.GetProperty("boundaries").GetProperty("failure").GetDouble().ShouldBe(-2.944439, 5e-7);
        view.GetProperty("behavioural").GetProperty("tradesCounted").GetInt32().ShouldBe(0);
        view.GetProperty("line").GetString().ShouldNotBeNull().ShouldContain("report only, never a pass");
        string.Join('\n', view.GetProperty("statements").EnumerateArray().Select(s => s.GetString())).ShouldContain("Gaussian");

        using var strategy = await Client.GetAsync(new Uri("/api/paper/sequential?scope=strategy&strategy=NOPE", UriKind.Relative), Ct);
        strategy.StatusCode.ShouldBe(HttpStatusCode.NotFound); // no candidate of that strategy: no series in that scope
    }
}
