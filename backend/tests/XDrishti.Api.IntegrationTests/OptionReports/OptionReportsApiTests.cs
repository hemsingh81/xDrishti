using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Api.IntegrationTests.OptionReports;

/// <summary>
/// P14-16 criteria 1 to 6 end to end on a real database: the four routes need a session, answer each honest state (unavailable with its reason, never a number), the bounded error cases, an empty list on an empty database, the counts of stored fixtures, field names that carry no price, IV or Greek,
/// and <c>xd reports options</c> prints the same figures with the limits block and exit 0.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionReportsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Truncate = "TRUNCATE options.quality_results, options.quality_runs, options.fetch_log RESTART IDENTITY; TRUNCATE research.expression_legs, research.expression_trades, research.expression_runs, research.pair_trades, research.pair_formations; DELETE FROM research.extension_evaluations WHERE family IN ('expr:option_buy', 'expr:debit_spread', 'expr:credit_spread', 'expr:iron_condor', 'pairs:intraday-1', 'ext:iv-1')";

    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);
    private static readonly string Cash = new('a', 64);
    private static readonly string Profile = new('b', 64);
    private static readonly string OtherProfile = new('d', 64);
    private static readonly string RunId = new('1', 64);
    private static readonly string OtherRunId = new('2', 64);

    /// <summary>The camel-case names the four routes may carry anywhere in a response, with the ones the CLI JSON adds.</summary>
    private static readonly string[] AllowedNames =
    [
        "archive", "backfill", "bhThreshold", "candidates", "cashRunKey", "count", "createdAt", "data", "done", "empty", "expiryDerivedShare", "expressionRuns", "fallbackShare", "families", "family", "feasibility", "firstDate", "foldsEvaluable", "foldsTotal", "from", "gaps", "givenUp",
        "halfLives", "holdOutRead", "id", "items", "labels", "lastDate", "limits", "lotFeasibility", "max", "median", "min", "missing", "noData", "outcome", "outcomes", "paired", "pairFormations", "profileHash", "protocol", "quarter", "reason", "recordedSessions", "remarkVersion", "selected",
        "sessions", "share", "spreadFromRecentArchiveShare", "spreadModelFirstSession", "spreadModelLastSession", "spreadModelSessions", "state", "tested", "to", "trades", "trials", "truncated", "unavailable", "unpaired", "underlying", "underlyings", "usableSessions", "value", "verdict", "verdicts",
        "windowFrom", "windowTo",
    ];

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

    private static Uri Route(string path) => new(path, UriKind.Relative);

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using var response = await Client.GetAsync(Route(path), Ct);
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

    private async Task QualityAsync(string date, string code, string status, string? reason = null)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO options.quality_results (run_id, session_date, underlying_id, source, fetch_key, check_code, status, reason, value, threshold, n) VALUES (1, @d::date, @u, 'chain', NULL, @code, @status, @reason, 0.9, 0.95, 10)",
            ("d", date), ("u", _nifty), ("code", code), ("status", status), ("reason", (object?)reason ?? DBNull.Value));
    }

    private async Task FetchAsync(int offset, short attempt, string outcome)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO options.fetch_log (underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, window_from, window_to, attempt, started_at, ended_at, outcome, bars, latency_ms) VALUES (@u, 'W', 1, @o, 'CE', 5, '2025-05-20', '2025-06-20', @a, now(), now(), @outcome, 0, 10)",
            ("u", _nifty), ("o", (short)offset), ("a", attempt), ("outcome", outcome));
    }

    private async Task SeedArchiveAsync()
    {
        await QualityAsync("2026-10-06", "coverage", "pass");
        await QualityAsync("2026-10-06", "parity", "pass");
        await QualityAsync("2026-10-07", "coverage", "pass");
        await QualityAsync("2026-10-07", "parity", "warn");
        await QualityAsync("2026-10-08", "coverage", "unavailable", "not_recorded");
        await QualityAsync("2026-10-08", "parity", "unavailable", "not_recorded");
        await FetchAsync(0, 1, "ok");
        await FetchAsync(1, 1, "empty");
        await FetchAsync(2, 1, "timeout");
        await FetchAsync(2, 2, "timeout");
        await FetchAsync(2, 3, "timeout");
    }

    private static RemarkRow Paired(string key, DateTimeOffset entry, string expirySource, string sizable, params string[] labels) =>
        new(key, RemarkFates.Paired, "CE", 'W', 1, new DateOnly(2026, 7, 1), expirySource, 2_200_000, entry, entry.AddMinutes(30), 6000, 7000, 10.0, 1000m, null, 1.0, 0.5, "stop", 75, sizable, labels);

    private static RemarkRow Unpaired(string key, DateTimeOffset entry, string fate) =>
        new(key, fate, "CE", null, null, null, "none", null, entry, entry.AddMinutes(30), null, null, null, null, null, null, null, "stop", null, "n/a", []);

    private async Task SeedRunAsync(string runId, string profile, DateTimeOffset createdAfter)
    {
        var entry = new DateTimeOffset(new DateTime(2026, 6, 2, 10, 0, 0), India).AddMinutes((createdAfter - DateTimeOffset.UnixEpoch).Minutes);
        List<RemarkRow> rows =
        [
            Paired("p1", entry, "derived", "sizable", "spread_from_recent_archive", "bs_fallback"),
            Paired("p2", entry.AddMinutes(1), "contract", "sizable"),
            Paired("p3", entry.AddMinutes(2), "contract", "lot_unknown", "spread_from_recent_archive"),
            Paired("p4", entry.AddMinutes(3), "contract", "sizable"),
            Unpaired("u1", entry.AddMinutes(4), "spread_unavailable"),
            Unpaired("u2", entry.AddMinutes(5), "spread_unavailable"),
            Unpaired("u3", entry.AddMinutes(6), "no_series"),
        ];
        using var scope = Factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IExpressionTradeStore>().AppendAsync(new ExpressionRunRow(runId, Cash, profile, "expr-1", "{}", rows.Count, 4, "{\"no_series\":1,\"spread_unavailable\":2}", 0.25, ["basis: gross", "remark: expr-1"]), rows, Ct);
        stored.ShouldBeTrue();
    }

    private async Task FormationAsync(string id, string quarter, string from, string to, string detail)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO research.pair_formations (formation_id, family, quarter, window_from, window_to, candidates, tested, unavailable, selected, detail) VALUES (@id, 'pairs:intraday-1', @q, @f::date, @t::date, 10, 8, 2, 2, @detail::jsonb)",
            ("id", id), ("q", quarter), ("f", from), ("t", to), ("detail", detail));
    }

    private async Task VerdictAsync(string family, string verdict, string protocol, IReadOnlyList<string> missing)
    {
        var record = new ExtensionEvaluationRecord(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(family + verdict))), family, protocol, null, "expr-1", "none", new string('a', 64), null, null, 3, 1.4, "quarter", 6, 2, 0, null,
            "{\"mean_d\":\"unavailable\"}", verdict, missing, ["gross of charges"], "{}", "{}", false);
        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IExtensionEvaluationStore>().AppendAsync(record, DateTimeOffset.UtcNow, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/api/options/summary")]
    [InlineData("/api/research/expression-runs")]
    [InlineData("/api/research/expression-runs/abc")]
    [InlineData("/api/research/pair-formations")]
    public async Task Without_a_session_each_route_is_401(string path)
    {
        using var response = await Factory.CreateAnonymousClient().GetAsync(Route(path), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_empty_database_is_an_empty_list_on_every_route_and_not_an_error()
    {
        var summary = await GetAsync("/api/options/summary");
        var runs = await GetAsync("/api/research/expression-runs");
        var formations = await GetAsync("/api/research/pair-formations");

        summary.Status.ShouldBe(HttpStatusCode.OK);
        summary.Body.GetProperty("underlyings").GetArrayLength().ShouldBe(0);
        runs.Body.GetProperty("items").GetArrayLength().ShouldBe(0);
        (runs.Body.GetProperty("state").GetString(), runs.Body.GetProperty("reason").GetString()).ShouldBe(("unavailable", "no_runs"));
        formations.Body.GetProperty("items").GetArrayLength().ShouldBe(0);
        formations.Body.GetProperty("reason").GetString().ShouldBe("no_runs");
    }

    [Fact]
    public async Task The_archive_summary_counts_the_sessions_gaps_and_backfill_keys_and_says_unavailable_with_a_reason_for_the_model()
    {
        await SeedArchiveAsync();

        var (status, body) = await GetAsync("/api/options/summary");

        status.ShouldBe(HttpStatusCode.OK);
        var u = body.GetProperty("underlyings").EnumerateArray().Single();
        u.GetProperty("underlying").GetString().ShouldBe("NSE:NIFTY");
        u.GetProperty("recordedSessions").GetProperty("value").GetInt32().ShouldBe(2);
        u.GetProperty("usableSessions").GetProperty("value").GetInt32().ShouldBe(2);
        u.GetProperty("firstDate").GetProperty("value").GetString().ShouldBe("2026-10-06");
        u.GetProperty("lastDate").GetProperty("value").GetString().ShouldBe("2026-10-07");
        u.GetProperty("gaps").GetProperty("outcomes").EnumerateArray().Select(o => (o.GetProperty("outcome").GetString(), o.GetProperty("reason").GetString(), o.GetProperty("sessions").GetInt32())).ShouldBe([("pass", null, 1), ("warn", null, 1), ("unavailable", "not_recorded", 1)]);
        var backfill = u.GetProperty("backfill");
        (backfill.GetProperty("done").GetInt32(), backfill.GetProperty("empty").GetInt32(), backfill.GetProperty("noData").GetInt32(), backfill.GetProperty("givenUp").GetInt32()).ShouldBe((2, 1, 0, 1)); // done counts every finished key, empty included, as the backfill progress does
        var model = u.GetProperty("spreadModelSessions");
        (model.GetProperty("state").GetString(), model.GetProperty("reason").GetString(), model.GetProperty("value").ValueKind).ShouldBe(("unavailable", "no_model", JsonValueKind.Null));
        Names(body).Except(AllowedNames).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_expression_runs_list_shows_counts_unpaired_reasons_and_shares_filters_by_profile_and_bounds_the_limit()
    {
        await SeedRunAsync(RunId, Profile, DateTimeOffset.UnixEpoch.AddMinutes(1));
        await SeedRunAsync(OtherRunId, OtherProfile, DateTimeOffset.UnixEpoch.AddMinutes(30));

        var (status, body) = await GetAsync("/api/research/expression-runs");
        var one = await GetAsync("/api/research/expression-runs?profile=" + Profile + "&limit=1");
        var detail = await GetAsync("/api/research/expression-runs/" + RunId);

        status.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("items").GetArrayLength().ShouldBe(2);
        body.GetProperty("items")[0].GetProperty("id").GetString().ShouldBe(OtherRunId); // newest first
        one.Body.GetProperty("items").GetArrayLength().ShouldBe(1);
        one.Body.GetProperty("items")[0].GetProperty("profileHash").GetString().ShouldBe(Profile);
        detail.Status.ShouldBe(HttpStatusCode.OK);
        (detail.Body.GetProperty("trades").GetInt32(), detail.Body.GetProperty("paired").GetInt32()).ShouldBe((7, 4));
        detail.Body.GetProperty("unpaired").EnumerateArray().Select(x => (x.GetProperty("reason").GetString(), x.GetProperty("trades").GetInt32())).ShouldBe([("no_series", 1), ("spread_unavailable", 2)]);
        detail.Body.GetProperty("fallbackShare").GetProperty("value").GetDouble().ShouldBe(0.25);
        detail.Body.GetProperty("expiryDerivedShare").GetProperty("value").GetDouble().ShouldBe(0.25);
        detail.Body.GetProperty("spreadFromRecentArchiveShare").GetProperty("value").GetDouble().ShouldBe(0.5);
        detail.Body.GetProperty("lotFeasibility").EnumerateArray().Select(l => (l.GetProperty("feasibility").GetString(), l.GetProperty("paired").GetInt32())).Where(l => l.Item2 > 0).ShouldBe([("sizable", 3), ("lot_unknown", 1)]);
        Names(body).Concat(Names(detail.Body)).Except(AllowedNames).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?profile=nothex")]
    [InlineData("?profile=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task A_bad_limit_or_profile_is_400_options_invalid_query(string query)
    {
        using var response = await Client.GetAsync(Route("/api/research/expression-runs" + query), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("options.invalid_query");
    }

    [Theory]
    [InlineData("/api/research/expression-runs/" + "9999999999999999999999999999999999999999999999999999999999999999")]
    [InlineData("/api/research/expression-runs/not-an-id")]
    public async Task An_unknown_run_id_is_404_research_not_found(string path)
    {
        using var response = await Client.GetAsync(Route(path), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("research.not_found");
    }

    [Fact]
    public async Task The_pair_formations_list_shows_the_counts_threshold_and_half_lives_for_the_range_and_refuses_an_inverted_or_over_long_one()
    {
        var table = "[{\"rank\":1,\"threshold\":0.005,\"rejected\":true},{\"rank\":2,\"threshold\":0.01,\"rejected\":true}]";
        await FormationAsync(new string('e', 64), "2026Q2", "2026-04-01", "2026-06-30", "{\"rejected\":2,\"half_lives\":[{\"pair\":\"A|B\",\"sessions\":4.0},{\"pair\":\"C|D\",\"sessions\":8.0}],\"table\":" + table + "}");
        await FormationAsync(new string('f', 64), "2026Q3", "2026-07-01", "2026-09-30", "{\"rejected\":0,\"half_lives\":[],\"table\":[]}");

        var all = await GetAsync("/api/research/pair-formations?from=2026-01-01&to=2026-12-31");
        var q3 = await GetAsync("/api/research/pair-formations?from=2026-08-01&to=2026-08-31");
        var inverted = await Client.GetAsync(Route("/api/research/pair-formations?from=2026-02-01&to=2026-01-01"), Ct);
        var over = await Client.GetAsync(Route("/api/research/pair-formations?from=2025-01-01&to=2026-12-31"), Ct);

        var items = all.Body.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("quarter").GetString()).ShouldBe(["2026Q2", "2026Q3"]);
        (items[0].GetProperty("candidates").GetInt32(), items[0].GetProperty("tested").GetInt32(), items[0].GetProperty("unavailable").GetInt32(), items[0].GetProperty("selected").GetInt32()).ShouldBe((10, 8, 2, 2));
        items[0].GetProperty("bhThreshold").GetProperty("value").GetDouble().ShouldBe(0.01);
        items[0].GetProperty("halfLives").GetProperty("median").GetDouble().ShouldBe(6.0);
        items[1].GetProperty("bhThreshold").GetProperty("reason").GetString().ShouldBe("no_rejection");
        items[1].GetProperty("halfLives").GetProperty("count").ValueKind.ShouldBe(JsonValueKind.Null);
        q3.Body.GetProperty("items").GetArrayLength().ShouldBe(1);
        all.Body.GetProperty("truncated").GetBoolean().ShouldBeFalse();
        inverted.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        over.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await over.Content.ReadAsStringAsync(Ct)).ShouldContain("options.invalid_query");
        Names(all.Body).Except(AllowedNames).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_cli_prints_the_same_figures_as_the_routes_with_the_limits_block_and_exit_0()
    {
        await SeedArchiveAsync();
        await SeedRunAsync(RunId, Profile, DateTimeOffset.UnixEpoch.AddMinutes(1));
        await VerdictAsync("expr:option_buy", "NOT_EVALUABLE", "O", ["folds 2 of 6"]);
        await VerdictAsync("pairs:intraday-1", "INCONCLUSIVE", "S", []);
        var output = new StringWriter();
        var error = new StringWriter();
        var json = new StringWriter();

        var exit = await ReportsOptionsCommand.RunAsync([], Factory.Services, new SimulateConsole(output, error), Ct);
        var exitJson = await ReportsOptionsCommand.RunAsync(["--json", "--cut", "ext:iv-1"], Factory.Services, new SimulateConsole(json, error), Ct);
        var routeSummary = await GetAsync("/api/options/summary");
        var routeRun = await GetAsync("/api/research/expression-runs/" + RunId);

        exit.ShouldBe(0, error.ToString());
        exitJson.ShouldBe(0, error.ToString());
        var text = output.ToString();
        text.ShouldContain("  NSE:NIFTY: recorded 2, usable 2, first 2026-10-06, last 2026-10-07");
        text.ShouldContain("    gaps: pass 1, warn 1, unavailable (not_recorded) 1");
        text.ShouldContain("    backfill keys: done 2, empty 1, no data 0, given up 1");
        text.ShouldContain("    spread model: sessions unavailable (no_model)");
        text.ShouldContain($"run {RunId[..12]} profile {Profile[..12]}");
        text.ShouldContain("trades 7, paired 4, unpaired no_series 1, spread_unavailable 2");
        text.ShouldContain("model-price share 0.2500, derived-expiry share 0.2500, recent-archive-spread share 0.5000");
        text.ShouldContain("  expr:option_buy: NOT_EVALUABLE, trials 3, folds 2 of 6, hold-out not read");
        text.ShouldContain("    missing: folds 2 of 6");
        text.ShouldContain("  pairs:intraday-1: INCONCLUSIVE");
        text.ShouldContain("  expr:debit_spread: none stored");
        text.ShouldContain("  expr:credit_spread: none stored");
        text.ShouldContain("  expr:iron_condor: none stored");
        text.ShouldContain("  ext:iv-1: none stored");
        text.ShouldContain("\nlimits\n");
        text.ShouldContain("no price, IV, Greek, bar or quote is shown");
        var data = JsonDocument.Parse(json.ToString()).RootElement;
        JsonSerializer.Serialize(data.GetProperty("archive")).ShouldBe(JsonSerializer.Serialize(routeSummary.Body));
        data.GetProperty("expressionRuns").GetProperty("items")[0].GetProperty("trades").GetInt32().ShouldBe(routeRun.Body.GetProperty("trades").GetInt32());
        data.GetProperty("verdicts").GetProperty("families").EnumerateArray().Single(f => f.GetProperty("family").GetString() == "ext:iv-1").GetProperty("state").GetString().ShouldBe("cut");
        data.GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(3);
        Names(data).Except(AllowedNames).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_cli_refuses_an_unknown_cut_family_with_exit_1_and_a_bad_line_with_exit_2()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var refused = await ReportsOptionsCommand.RunAsync(["--cut", "basis:carry-1"], Factory.Services, new SimulateConsole(output, error), Ct);
        var bad = await ReportsOptionsCommand.RunAsync(["--nope"], Factory.Services, new SimulateConsole(output, error), Ct);

        refused.ShouldBe(1);
        bad.ShouldBe(2);
        output.ToString().ShouldBeEmpty();
        error.ToString().ShouldContain("options.unknown_family");
    }
}
