using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Infrastructure.Ai.Assistant;
using XDrishti.Infrastructure.Ai.Evaluation;
using XDrishti.Mcp.Tests.Support;

namespace XDrishti.Mcp.Tests;

/// <summary>
/// The fixture database of the eval (spec P9-09 AC 1, 13, 14, ADR 0143): the seeder writes exactly the records of the fixture and only into an empty database of the right name, seeding twice changes nothing, the marker is read through the real tool endpoint, and the tools give
/// the hand-worked numbers of the questions from the seeded rows. Real host, real roles, a migrated throw-away server.
/// </summary>
[Collection(ReadOnlyDatabaseSuite.Name)]
public sealed class EvalFixtureDatabaseTests(ReadOnlyDatabase db) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string FixtureText = ReadFixtureText();

    private static readonly EvalFixture Fixture = EvalFixtureReader.Parse(FixtureText).Value;

    public async ValueTask InitializeAsync() => await CleanAsync();

    public async ValueTask DisposeAsync() => await CleanAsync();

    private async Task CleanAsync() => await ReadOnlyDatabase.ExecuteAsync(db.Owner, """
        SET session_replication_role = replica;
        TRUNCATE ai.settings CASCADE;
        DELETE FROM ai.eval_baselines; DELETE FROM ai.eval_cases; DELETE FROM ai.eval_runs; DELETE FROM ai.eval_marker;
        DELETE FROM research.lessons; DELETE FROM research.hypotheses; DELETE FROM research.sim_trades; DELETE FROM research.sim_runs; DELETE FROM research.cell_stats;
        DELETE FROM trading.plan_item_allocations; DELETE FROM trading.plan_items; DELETE FROM trading.plans;
        DELETE FROM portfolio.holding_lots; DELETE FROM portfolio.holdings; DELETE FROM accounts.portfolio_buckets; DELETE FROM accounts.broker_accounts;
        DELETE FROM market.basket_members; DELETE FROM market.baskets; DELETE FROM market.instruments;
        DELETE FROM lab.strategy_versions WHERE strategy_id IN ('ORB', 'VWAP_PULLBACK'); DELETE FROM lab.strategies WHERE id IN ('ORB', 'VWAP_PULLBACK');
        """);

    private static string ReadFixtureText()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "eval", "thresholds.json")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "eval", "fixture", "fixture.json"));
    }

    private Task<SeedOutcome> SeedAsync() => FixtureSeeder.SeedAsync(db.Owner, Fixture, FixtureText, ReadOnlyDatabase.Database, Ct);

    private static string Hash => EvalFixture.HashOf(FixtureText);

    private async Task<long> CountAsync(string table) => (long)(await ReadOnlyDatabase.ScalarAsync(db.Owner, $"SELECT count(*) FROM {table}"))!;

    private async Task<(TestMcpHost Host, McpClient Client)> StartAsync()
    {
        var host = await TestMcpHost.StartAsync(database: db);
        var options = new HttpClientTransportOptions { Endpoint = new Uri("http://xd-mcp/mcp"), TransportMode = HttpTransportMode.StreamableHttp, AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + TestMcpHost.ApiToken } };
        var client = await McpClient.CreateAsync(new HttpClientTransport(options, host.CreateClient(), null, ownsHttpClient: false), cancellationToken: Ct);
        return (host, client);
    }

    private static async Task<JsonNode> CallAsync(McpClient client, string tool, Dictionary<string, object?>? arguments = null)
    {
        var result = await client.CallToolAsync(tool, arguments ?? [], cancellationToken: Ct);
        return JsonNode.Parse(((TextContentBlock)result.Content.Single()).Text)!;
    }

    /// <summary>An exact decimal comes back from <c>run_sql</c> as text; a double as a number.</summary>
    private static double Number(JsonNode node) => node.GetValueKind() == System.Text.Json.JsonValueKind.String ? double.Parse(node.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture) : node.GetValue<double>();

    private static Dictionary<string, object?> Sql(string text) => new() { ["sql"] = text };

    private static async Task<JsonNode> OneRowAsync(McpClient client, string sql)
    {
        var json = await CallAsync(client, "run_sql", Sql(sql));
        json["ok"]!.GetValue<bool>().ShouldBeTrue(json.ToJsonString());
        return json["rows"]![0]!;
    }

    [Fact]
    public async Task Seeding_writes_exactly_the_records_of_the_fixture_and_the_marker_and_seeding_twice_changes_nothing()
    {
        (await SeedAsync()).ShouldBe(SeedOutcome.Seeded);

        (await CountAsync("market.instruments")).ShouldBe(8);
        (await CountAsync("market.baskets")).ShouldBe(3);
        (await CountAsync("market.basket_members")).ShouldBe(8);
        (await CountAsync("research.sim_runs")).ShouldBe(2);
        (await CountAsync("research.sim_trades")).ShouldBe(40);
        (await CountAsync("trading.plans")).ShouldBe(1);
        (await CountAsync("trading.plan_items")).ShouldBe(6);
        (await CountAsync("trading.plan_item_allocations")).ShouldBe(3);
        (await CountAsync("portfolio.holdings")).ShouldBe(2);
        (await CountAsync("research.cell_stats")).ShouldBe(3);
        (await CountAsync("research.lessons")).ShouldBe(4);
        (await CountAsync("research.hypotheses")).ShouldBe(5);
        (await ReadOnlyDatabase.ScalarAsync(db.Owner, "SELECT fixture_hash FROM ai.eval_marker WHERE id = 1")).ShouldBe(Hash);
        (await ReadOnlyDatabase.ScalarAsync(db.Owner, "SELECT enabled AND portfolio_context FROM ai.settings ORDER BY id DESC LIMIT 1")).ShouldBe(true);

        var before = await ReadOnlyDatabase.ScalarAsync(db.Owner, "SELECT md5(string_agg(trade_key || r_filled::text, ',' ORDER BY trade_key)) FROM research.sim_trades");
        (await SeedAsync()).ShouldBe(SeedOutcome.Unchanged);
        (await CountAsync("research.sim_trades")).ShouldBe(40);
        (await ReadOnlyDatabase.ScalarAsync(db.Owner, "SELECT md5(string_agg(trade_key || r_filled::text, ',' ORDER BY trade_key)) FROM research.sim_trades")).ShouldBe(before);
    }

    [Fact]
    public async Task The_seeder_refuses_another_database_data_that_is_not_the_fixture_and_a_changed_fixture()
    {
        var wrongName = await Should.ThrowAsync<SeedRefusedException>(() => FixtureSeeder.SeedAsync(db.Owner, Fixture, FixtureText, "xdrishti_eval", Ct));
        wrongName.Message.ShouldContain("xdrishti_eval");
        (await CountAsync("market.instruments")).ShouldBe(0);

        await ReadOnlyDatabase.ExecuteAsync(db.Owner, "INSERT INTO market.baskets (id, name, colour, purpose, description, position, created_at, revision) VALUES (gen_random_uuid(), 'Real', '#000000', 'trading', 'x', 1, now(), 1)");
        var data = await Should.ThrowAsync<SeedRefusedException>(SeedAsync);
        data.Message.ShouldContain("market.baskets");
        (await CountAsync("market.instruments")).ShouldBe(0, "a refusal writes nothing");
        await CleanAsync();

        (await SeedAsync()).ShouldBe(SeedOutcome.Seeded);
        var changed = await Should.ThrowAsync<SeedRefusedException>(() => FixtureSeeder.SeedAsync(db.Owner, Fixture, FixtureText + " ", ReadOnlyDatabase.Database, Ct));
        changed.Message.ShouldContain("another version");
    }

    [Fact]
    public async Task The_probe_reads_the_marker_through_the_tool_endpoint_and_finds_none_in_a_database_that_is_not_the_fixture()
    {
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            await ReadOnlyDatabase.ExecuteAsync(db.App, "INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) VALUES (true, false, 6, 'owner:probe@example.test', 'probe')");
            var probe = new EvalFixtureProbe(new ClientCatalog(client));

            (await probe.ReadFixtureHashAsync(Ct)).ShouldBeNull("no marker: a database that is not the fixture");

            await CleanAsync();
            await SeedAsync();
            (await probe.ReadFixtureHashAsync(Ct)).ShouldBe(Hash);
        }
    }

    [Fact]
    public async Task The_tables_of_the_eval_are_insert_only_for_the_runtime_role_and_the_marker_is_closed_to_it()
    {
        await SeedAsync();
        var run = Guid.NewGuid();
        await ReadOnlyDatabase.ExecuteAsync(db.App, $$"""
            INSERT INTO ai.eval_runs (id, started_at, finished_at, target, profile, sets, repeats, gating, model_id, model_sha256, runtime_digest, params_json, prompt_versions_json, sets_hash, thresholds_hash, fixture_hash, fingerprint, status, verdict, summary_json, actor)
            VALUES ('{{run}}', now(), now(), 'builtin', 'main', ARRAY['data'], 3, true, 'm', 'a', 'r', '{}', '[]', repeat('1', 64), repeat('2', 64), repeat('3', 64), repeat('4', 64), 'completed', 'PASS', '{}', 'cli:xd');
            INSERT INTO ai.eval_cases (run_id, "set", case_id, repeat, passed, detail_json, answer, tool_calls, latency_ms) VALUES ('{{run}}', 'data', 'DQ-01', 1, true, '{}', 'x', 1, 1);
            INSERT INTO ai.eval_baselines (run_id, accepted_by) VALUES ('{{run}}', 'cli:xd');
            """);

        foreach (var sql in new[]
        {
            "UPDATE ai.eval_runs SET verdict = 'FAIL'", "DELETE FROM ai.eval_runs", "UPDATE ai.eval_cases SET passed = false", "DELETE FROM ai.eval_cases", "UPDATE ai.eval_baselines SET reason = 'x'", "DELETE FROM ai.eval_baselines",
            "SELECT * FROM ai.eval_marker", "INSERT INTO ai.eval_marker (id, fixture_hash) VALUES (1, repeat('a', 64))", "DELETE FROM ai.eval_marker",
        })
        {
            (await ReadOnlyDatabase.SqlStateAsync(db.App, sql)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, sql);
        }

        (await ReadOnlyDatabase.SqlStateAsync(db.App, $"INSERT INTO ai.eval_baselines (run_id, accepted_by) VALUES ('{run}', 'cli:xd')")).ShouldBe(PostgresErrorCodes.UniqueViolation, "one baseline row per run");
        (await ReadOnlyDatabase.SqlStateAsync(db.App, $"INSERT INTO ai.eval_baselines (run_id, accepted_by) VALUES ('{Guid.NewGuid()}', 'service:x')")).ShouldNotBeNull();
        (await ReadOnlyDatabase.SqlStateAsync(db.App, $"INSERT INTO ai.eval_cases (run_id, \"set\", case_id, repeat, passed, detail_json, answer, tool_calls, latency_ms) VALUES ('{run}', 'data', 'DQ-01', 1, true, '{{}}', 'x', 1, 1)")).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await ReadOnlyDatabase.SqlStateAsync(db.App, "INSERT INTO ai.eval_runs (id, started_at, finished_at, target, profile, sets, repeats, gating, model_id, model_sha256, runtime_digest, params_json, prompt_versions_json, sets_hash, thresholds_hash, fixture_hash, fingerprint, status, verdict, summary_json, actor) VALUES (gen_random_uuid(), now(), now(), 'builtin', 'main', ARRAY['data'], 1, true, 'm', 'a', 'r', '{}', '[]', repeat('1', 64), repeat('2', 64), repeat('3', 64), repeat('4', 64), 'completed', 'PASS', '{}', 'cli:xd')")).ShouldBe(PostgresErrorCodes.CheckViolation, "a gating run needs two repeats");
    }

    [Fact]
    public async Task The_tools_give_the_hand_worked_numbers_of_the_questions_from_the_seeded_rows()
    {
        await SeedAsync();
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            // KPIs and groups from the stored trades (DQ-01 to DQ-14).
            var orb = await OneRowAsync(client, "select count(*) as n, sum(r_filled) as total_r, count(*) filter (where r_filled > 0) as wins, sum(r_filled) filter (where r_filled > 0) as win_r, max(r_filled) as best from aiv.sim_trades where strategy_id = 'ORB'");
            ((int)orb["n"]!.GetValue<long>(), orb["total_r"]!.GetValue<double>(), (int)orb["wins"]!.GetValue<long>(), orb["win_r"]!.GetValue<double>(), orb["best"]!.GetValue<double>()).ShouldBe((24, 4.0, 11, 17.0, 3.0));
            (await OneRowAsync(client, "select symbol from aiv.sim_trades where strategy_id = 'ORB' order by r_filled desc limit 1"))["symbol"]!.GetValue<string>().ShouldBe("EVC");
            var vwap = await OneRowAsync(client, "select count(*) as n, count(*) filter (where r_filled > 0) as wins, sum(r_filled) as total_r from aiv.sim_trades where strategy_id = 'VWAP_PULLBACK'");
            ((int)vwap["n"]!.GetValue<long>(), (int)vwap["wins"]!.GetValue<long>(), vwap["total_r"]!.GetValue<double>()).ShouldBe((16, 7, 1.0));
            var sell = await OneRowAsync(client, "select sum(r_filled) as total_r from aiv.sim_trades where strategy_id = 'ORB' and side = 'sell'");
            sell["total_r"]!.GetValue<double>().ShouldBe(2.5, 1e-9);
            var baskets = await CallAsync(client, "run_sql", Sql("select m.basket as name, count(*) as n, sum(t.r_filled) as total_r from aiv.sim_trades t join aiv.basket_members m on m.symbol = t.symbol where m.valid_to is null and m.basket in ('EvalAlpha', 'EvalBeta') group by m.basket order by m.basket"));
            baskets["ok"]!.GetValue<bool>().ShouldBeTrue(baskets.ToJsonString());
            baskets["rows"]!.AsArray().Select(r => (r!["name"]!.GetValue<string>(), r["n"]!.GetValue<long>(), r["total_r"]!.GetValue<double>())).ShouldBe([("EvalAlpha", 20L, 5.5), ("EvalBeta", 20L, -0.5)]);

            // The report engine answers insufficient below its minimum of 30 trades: a figure must be computed from the trades (honest limit, ADR 0143).
            var report = await CallAsync(client, "get_report", new() { ["report"] = "summary", ["query"] = new Dictionary<string, string> { ["strategy"] = "ORB" } });
            report["ok"]!.GetValue<bool>().ShouldBeTrue(report.ToJsonString());
            report["rows"]![0]!["n"]!.GetValue<int>().ShouldBe(24);
            report["rows"]![0]!["winRate"]!["state"]!.GetValue<string>().ShouldBe("insufficient");

            // The plan, the allocation and the cell (DQ-20 to DQ-23).
            var plan = await CallAsync(client, "get_plan", new() { ["date"] = "2026-03-03" });
            plan["ok"]!.GetValue<bool>().ShouldBeTrue(plan.ToJsonString());
            plan["rows"]!.AsArray().Count(r => r!["group"]?.GetValue<string>() == "primary").ShouldBe(3);
            var second = await OneRowAsync(client, "select symbol, side from aiv.plan_items where item_rank = 2");
            (second["symbol"]!.GetValue<string>(), second["side"]!.GetValue<string>()).ShouldBe(("EVD", "SELL"));
            var allocation = await OneRowAsync(client, "select a.quantity as shares, a.account as account from aiv.plan_item_allocations a join aiv.plan_items i on i.item_id = a.item_id where i.symbol = 'EVA'");
            (allocation["shares"]!.GetValue<long>(), allocation["account"]!.GetValue<string>()).ShouldBe((200L, "EvalPaper"));
            (await CallAsync(client, "get_cell", new() { ["strategy"] = "ORB", ["timeframe"] = "15m", ["side"] = "BUY" }))["rows"]!.AsArray().Select(r => (r!["window"]!.GetValue<string>(), r["n"]!.GetValue<int>())).ShouldBe([("all", 12), ("250d", 12), ("60d", 12)]);

            // The holdings and the basket of the portfolio (DQ-15 to DQ-19): the portfolio context is on in this database.
            var totals = (await CallAsync(client, "get_portfolio", new() { ["groupBy"] = "none" }))["rows"]![0]!;
            (totals["invested"]!["number"]!.GetValue<double>(), totals["value"]!["number"]!.GetValue<double>(), totals["unrealised"]!["number"]!.GetValue<double>()).ShouldBe((20000.0, 21100.0, 1100.0));
            var basket = (await CallAsync(client, "get_portfolio", new() { ["groupBy"] = "basket" }))["rows"]!.AsArray().Single(r => r!["group"]!.GetValue<string>() == "MyLongTerm")!;
            (basket["holdings"]!.GetValue<int>(), basket["value"]!["number"]!.GetValue<double>()).ShouldBe((2, 21100.0));
            var holdings = await CallAsync(client, "run_sql", Sql("select symbol, qty * avg_cost as invested, qty * (last_price - avg_cost) as unrealised from aiv.holdings order by symbol"));
            holdings["rows"]!.AsArray().Select(r => (r!["symbol"]!.GetValue<string>(), Number(r["invested"]!), Number(r["unrealised"]!))).ShouldBe([("EVH1", 10000.0, 2100.0), ("EVH2", 10000.0, -1000.0)]);

            // Learning (DQ-24, DQ-25): lessons and hypotheses are logs, one row per state change, and the tool filters rows; a lesson counted once by its newest state is what the questions ask (their wording says so).
            (await CallAsync(client, "get_learning", new() { ["kind"] = "lessons", ["status"] = "open" }))["rowCount"]!.GetValue<int>().ShouldBe(3, "the opening row of the dismissed lesson is still an open row");
            (await OneRowAsync(client, "select count(*) as n from (select distinct on (subject) subject, state from aiv.lessons order by subject, created_at desc) x where state = 'open'"))["n"]!.GetValue<long>().ShouldBe(2);
            (await OneRowAsync(client, "select count(*) as n from (select distinct on (rationale) rationale, state from aiv.hypotheses order by rationale, created_at desc) x where state in ('proposed', 'approved')"))["n"]!.GetValue<long>().ShouldBe(2);
        }
    }

    /// <summary>The tool server through a client that is already connected (the probe's port over the in-memory host).</summary>
    private sealed class ClientCatalog(McpClient client) : IEvalToolCatalog
    {
        public ValueTask<ToolCatalogSnapshot> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public async ValueTask<ToolInvocation> CallAsync(string name, string argumentsJson, CancellationToken cancellationToken)
        {
            var arguments = JsonNode.Parse(argumentsJson)!.AsObject().ToDictionary(p => p.Key, p => (object?)p.Value!.GetValue<string>());
            var result = await client.CallToolAsync(name, arguments, cancellationToken: cancellationToken);
            return new ToolInvocation(true, result.IsError == true, ((TextContentBlock)result.Content.Single()).Text);
        }
    }
}
