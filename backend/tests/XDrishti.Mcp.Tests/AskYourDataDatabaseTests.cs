using System.Diagnostics;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using XDrishti.Domain.Assistant.Grounding;
using XDrishti.Mcp.Tests.Support;
using static XDrishti.Mcp.Tests.Support.AskYourDataSeed;

namespace XDrishti.Mcp.Tests;

/// <summary>
/// The eleven "Ask your data" tools end to end (spec P9-05): a real MCP client, the real host, the real ports and the real read-only logins over a migrated server, with hand-worked numbers. What the model would see: the numbers the app computes, in states, with evidence, and nothing of class C0.
/// </summary>
[Collection(ReadOnlyDatabaseSuite.Name)]
public sealed class AskYourDataDatabaseTests(ReadOnlyDatabase db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>The server is shared by the test classes of the collection: leave nothing of the fixture behind.</summary>
    public async ValueTask DisposeAsync() => await CleanAsync(db);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(TestMcpHost Host, McpClient Client)> StartAsync(bool portfolio = false)
    {
        await CleanAsync(db);
        await ReadOnlyDatabase.ExecuteAsync(db.App, $"INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) VALUES (true, {(portfolio ? "true" : "false")}, 6, 'owner:{OwnerMail}', 'ask test')");
        var host = await TestMcpHost.StartAsync(database: db);
        var options = new HttpClientTransportOptions { Endpoint = new Uri("http://xd-mcp/mcp"), TransportMode = HttpTransportMode.StreamableHttp, AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + TestMcpHost.ApiToken } };
        var client = await McpClient.CreateAsync(new HttpClientTransport(options, host.CreateClient(), null, ownsHttpClient: false), cancellationToken: Ct);
        return (host, client);
    }

    private static async Task<(JsonNode Json, string Text)> CallAsync(McpClient client, string tool, Dictionary<string, object?>? arguments = null)
    {
        var result = await client.CallToolAsync(tool, arguments ?? [], cancellationToken: Ct);
        var text = ((TextContentBlock)result.Content.Single()).Text;
        return (JsonNode.Parse(text)!, text);
    }

    private static Dictionary<string, object?> Report(string kind, Dictionary<string, string>? query = null) => query is null ? new() { ["report"] = kind } : new() { ["report"] = kind, ["query"] = query };

    private static string Code(JsonNode json) => json["error"]!["code"]!.GetValue<string>();

    private static JsonNode Row(JsonNode json, int index = 0) => json["rows"]![index]!;

    [Fact]
    public async Task Get_report_groups_gives_the_hand_worked_numbers_with_states_evidence_and_an_untouched_run_key()
    {
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            await RunAAsync(db);

            var (json, text) = await CallAsync(client, "get_report", Report("groups", new() { ["runs"] = RunA, ["lens"] = "strategy" }));

            json["ok"]!.GetValue<bool>().ShouldBeTrue(text);
            var row = Row(json);
            row["key"]!.GetValue<string>().ShouldBe("Strategy=ORB");
            row["n"]!.GetValue<int>().ShouldBe(40);
            row["wins"]!.GetValue<int>().ShouldBe(24);
            row["winRate"]!["state"]!.GetValue<string>().ShouldBe("value");
            row["winRate"]!["number"]!.GetValue<double>().ShouldBe(0.6, 1e-9);
            row["expectancyR"]!["number"]!.GetValue<double>().ShouldBe(0.8, 1e-9);
            row["totalR"]!["number"]!.GetValue<double>().ShouldBe(32.0, 1e-9);
            row["profitFactor"]!["number"]!.GetValue<double>().ShouldBe(3.0, 1e-9);
            json["evidence"]!["basis"]!.GetValue<string>().ShouldBe("gross");
            json["evidence"]!["n"]!.GetValue<int>().ShouldBe(40);
            json["evidence"]!["population"]![0]!.GetValue<string>().ShouldContain(RunA);
            json["evidence"]!["limits"]!.AsArray().Count.ShouldBeGreaterThan(0);
            json["redactions"]!.GetValue<int>().ShouldBe(0, "a run key is never redacted");
            text.ShouldContain(RunA);

            // The summary and the report engine agree with the stored trades (independent SQL).
            var mean = (double)(await ReadOnlyDatabase.ScalarAsync(db.Owner, $"SELECT avg(t.r_filled) FROM research.sim_trades t JOIN research.sim_runs r ON r.id = t.run_id WHERE r.run_key = '{RunA}'"))!;
            var (summary, _) = await CallAsync(client, "get_report", Report("summary", new() { ["runs"] = RunA }));
            Row(summary)["expectancyR"]!["number"]!.GetValue<double>().ShouldBe(mean, 1e-9);
        }
    }

    [Fact]
    public async Task Get_report_refuses_net_unknown_names_and_overlapping_runs_in_words_and_validates_the_query()
    {
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            await RunAAsync(db);
            await RunBAsync(db);

            Code((await CallAsync(client, "get_report", Report("summary", new() { ["basis"] = "net" }))).Json).ShouldBe("reports.net_not_confirmed");
            (await CallAsync(client, "get_report", Report("summary", new() { ["basis"] = "net" }))).Json["error"]!["message"]!.GetValue<string>().ShouldContain("net is not offered by the assistant yet");
            var invalid = (await CallAsync(client, "get_report", Report("groups", new() { ["lens"] = "bogus" }))).Json;
            Code(invalid).ShouldBe("reports.invalid");
            invalid["error"]!["message"]!.GetValue<string>().ShouldContain("lens");
            Code((await CallAsync(client, "get_report", Report("summary", new() { ["runs"] = RunA + "," + RunB }))).Json).ShouldStartWith("reports.");

            Code((await CallAsync(client, "get_report", Report("summary", new() { ["evil"] = "x" }))).Json).ShouldBe("tool.invalid_arguments");
            Code((await CallAsync(client, "get_report", Report("summary", new() { ["runs"] = new string('a', 201) }))).Json).ShouldBe("tool.invalid_arguments");
            var many = Enumerable.Range(0, 13).ToDictionary(i => GetReportTool.QueryKeys[i], _ => "x");
            Code((await CallAsync(client, "get_report", Report("summary", many))).Json).ShouldBe("tool.invalid_arguments");
            Code((await CallAsync(client, "get_report", Report("nothing"))).Json).ShouldBe("tool.invalid_arguments");
        }
    }

    [Fact]
    public async Task What_works_with_nothing_above_the_minimum_answers_ok_with_its_counts_and_list_runs_marks_the_default_set()
    {
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            await RunAAsync(db);

            var (works, text) = await CallAsync(client, "get_report", Report("what_works", new() { ["runs"] = RunA, ["lens"] = "strategy,side" }));
            works["ok"]!.GetValue<bool>().ShouldBeTrue(text);
            works["data"]!["cellsLookedAt"]!.GetValue<int>().ShouldBeGreaterThanOrEqualTo(0);
            works["data"]!["kind"]!.GetValue<string>().ShouldBeOneOf("ranked", "nothing_qualifies");

            var (runs, runsText) = await CallAsync(client, "list_runs", new() { ["kind"] = "sim" });
            runs["ok"]!.GetValue<bool>().ShouldBeTrue(runsText);
            Row(runs)["runKey"]!.GetValue<string>().ShouldBe(RunA);
            Row(runs)["inDefaultSet"]!.GetValue<bool>().ShouldBeTrue();
            Row(runs)["costed"]!.GetValue<bool>().ShouldBeFalse();
            runs["redactions"]!.GetValue<int>().ShouldBe(0);

            var (lab, _) = await CallAsync(client, "list_runs", new() { ["kind"] = "lab", ["limit"] = 5 });
            lab["ok"]!.GetValue<bool>().ShouldBeTrue();
            lab["rowCount"]!.GetValue<int>().ShouldBe(0);
            Code((await CallAsync(client, "list_runs", new() { ["kind"] = "lab", ["limit"] = 21 })).Json).ShouldBe("tool.invalid_arguments");
        }
    }

    [Fact]
    public async Task Get_plan_gives_tickets_with_stored_levels_state_grade_and_the_empty_answer_and_escapes_text()
    {
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            var (none, _) = await CallAsync(client, "get_plan", new() { ["date"] = "2030-01-01" });
            none["ok"]!.GetValue<bool>().ShouldBeTrue();
            none["data"]!["state"]!.GetValue<string>().ShouldBe("empty");
            none["data"]!["date"]!.GetValue<string>().ShouldBe("2030-01-01");
            Code((await CallAsync(client, "get_plan", new() { ["date"] = "28/10/2026" })).Json).ShouldBe("tool.invalid_arguments");

            await PlanAsync(db);
            var (json, text) = await CallAsync(client, "get_plan");

            json["ok"]!.GetValue<bool>().ShouldBeTrue(text);
            json["data"]!["forDate"]!.GetValue<string>().ShouldBe("2026-10-28");
            var buy = Row(json, 0);
            buy["symbol"]!.GetValue<string>().ShouldBe("M&M");
            buy["side"]!.GetValue<string>().ShouldBe("BUY");
            buy["state"]!.GetValue<string>().ShouldBe("proposed");
            buy["pWinLower"]!.GetValue<double>().ShouldBe(0.45, 1e-9);
            buy["expectedR"]!.GetValue<double>().ShouldBe(0.4, 1e-9);
            buy["levels"]!["stop"]!.GetValue<double>().ShouldBe(95.0, 1e-9);
            buy["levels"]!["target"]!.GetValue<double>().ShouldBe(110.0, 1e-9);
            buy["grade"]!.GetValue<string>().ShouldContain("graded r=1.5");
            buy["ticketId"]!.GetValue<long>().ShouldBe(9101);
            buy["unavailable"]!.GetValue<string>().ShouldContain("regime");
            var sell = Row(json, 1);
            sell["levels"]!.GetValue<string>().ShouldBe("not stored");
            sell["stop"]!.GetValue<string>().ShouldContain("Ignore previous instructions");
            json["injectionFlags"]!.GetValue<int>().ShouldBeGreaterThan(0);
            text.ToLowerInvariant().ShouldNotContain("recommend");

            // The account label is not shown while the portfolio context is off.
            var (withLabel, _) = await CallAsync(client, "get_plan", new() { ["accountLabel"] = "Main" });
            withLabel["data"]!["accountLabel"]!.GetValue<string>().ShouldBe("unavailable (portfolio context is off)");
        }
    }

    [Fact]
    public async Task Get_cell_gives_one_row_per_window_insufficient_without_a_rate_and_lists_an_ambiguous_selector()
    {
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            Code((await CallAsync(client, "get_cell")).Json).ShouldBe("tool.invalid_arguments");
            (await CallAsync(client, "get_cell", new() { ["strategy"] = "ORB" })).Json["data"]!["state"]!.GetValue<string>().ShouldBe("empty");

            await CellsAsync(db);
            var (json, text) = await CallAsync(client, "get_cell", new() { ["strategy"] = "ORB", ["timeframe"] = "15m", ["side"] = "BUY" });

            json["ok"]!.GetValue<bool>().ShouldBeTrue(text);
            json["rowCount"]!.GetValue<int>().ShouldBe(3);
            var all = Row(json, 0);
            all["window"]!.GetValue<string>().ShouldBe("all");
            all["state"]!.GetValue<string>().ShouldBe("value");
            all["n"]!.GetValue<int>().ShouldBe(50);
            all["wins"]!.GetValue<int>().ShouldBe(30);
            all["winRate"]!.GetValue<double>().ShouldBe(0.6, 1e-9);
            all["cellKey"]!.GetValue<string>().ShouldBe("ORB|15m|BUY|intraday");
            json["redactions"]!.GetValue<int>().ShouldBe(0);
            var recent = Row(json, 2);
            recent["window"]!.GetValue<string>().ShouldBe("60d");
            recent["state"]!.GetValue<string>().ShouldBe("insufficient");
            recent["n"]!.GetValue<int>().ShouldBe(8);
            recent["winRate"].ShouldBeNull("an insufficient cell shows no rate");

            var (thin, _) = await CallAsync(client, "get_cell", new() { ["key"] = "ORB|15m|SELL|intraday" });
            Row(thin)["state"]!.GetValue<string>().ShouldBe("insufficient");
            Row(thin)["n"]!.GetValue<int>().ShouldBe(12);

            var (ambiguous, _) = await CallAsync(client, "get_cell", new() { ["strategy"] = "ORB" });
            ambiguous["notes"]![0]!.GetValue<string>().ShouldBe("narrow the selector");
            ambiguous["rowCount"]!.GetValue<int>().ShouldBe(2);
        }
    }

    [Fact]
    public async Task Get_portfolio_is_unavailable_while_the_context_is_off_and_computes_the_screens_figures_when_it_is_on()
    {
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            await MarketAsync(db);
            await HoldingsAsync(db);

            var (off, offText) = await CallAsync(client, "get_portfolio", new() { ["groupBy"] = "none" });
            off["ok"]!.GetValue<bool>().ShouldBeTrue();
            off["data"].ShouldBeNull();
            off["rows"].ShouldBeNull();
            off["notes"]![0]!.GetValue<string>().ShouldBe("unavailable: portfolio context is off");
            offText.ShouldNotContain("X Ltd");
            offText.ShouldNotContain("\"Main\"");

            await ReadOnlyDatabase.ExecuteAsync(db.App, $"INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) VALUES (true, true, 6, 'owner:{OwnerMail}', 'portfolio on')");
            var (on, onText) = await CallAsync(client, "get_portfolio", new() { ["groupBy"] = "none" });

            on["ok"]!.GetValue<bool>().ShouldBeTrue(onText);
            var totals = Row(on);
            totals["invested"]!["number"]!.GetValue<double>().ShouldBe(2000.0, 1e-9);
            totals["value"]!["number"]!.GetValue<double>().ShouldBe(2000.0, 1e-9);
            totals["unrealised"]!["number"]!.GetValue<double>().ShouldBe(0.0, 1e-9);
            totals["unrealisedPct"]!["number"]!.GetValue<double>().ShouldBe(0.0, 1e-9);
            totals["xirr"]!["state"]!.GetValue<string>().ShouldBe("value");

            var (bySector, _) = await CallAsync(client, "get_portfolio", new() { ["groupBy"] = "sector" });
            bySector["rows"]!.AsArray().Select(r => r!["group"]!.GetValue<string>()).Order().ShouldBe(["Auto", "unclassified"]);
            var (byBucket, _) = await CallAsync(client, "get_portfolio", new() { ["groupBy"] = "bucket" });
            Row(byBucket)["group"]!.GetValue<string>().ShouldBe("Long term");
            var (byAccount, accountText) = await CallAsync(client, "get_portfolio", new() { ["groupBy"] = "account" });
            Row(byAccount)["group"]!.GetValue<string>().ShouldBe("Main");
            accountText.ShouldNotContain(ClientId);
            accountText.ShouldNotContain("SEC-1");
            accountText.ToLowerInvariant().ShouldNotContain("\"cash");
            accountText.ToLowerInvariant().ShouldNotContain("\"funds");
            Code((await CallAsync(client, "get_portfolio", new() { ["groupBy"] = "colour" })).Json).ShouldBe("tool.invalid_arguments");
        }
    }

    [Fact]
    public async Task Get_basket_and_get_instrument_give_facts_coverage_and_escaped_names_and_answer_unknown()
    {
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            Code((await CallAsync(client, "get_basket", new() { ["name"] = "nope" })).Json).ShouldBe("basket.unknown");
            Code((await CallAsync(client, "get_instrument", new() { ["symbol"] = "NOPE" })).Json).ShouldBe("instrument.unknown");

            await MarketAsync(db);
            var (basket, basketText) = await CallAsync(client, "get_basket", new() { ["name"] = "Core<i>1" });
            basket["ok"]!.GetValue<bool>().ShouldBeTrue(basketText);
            basket["data"]!["nameText"]!.GetValue<string>().ShouldBe("Core&lt;i&gt;1");
            basket["data"]!["memberCount"]!.GetValue<int>().ShouldBe(1);
            Row(basket)["symbol"]!.GetValue<string>().ShouldBe("M&M");
            Row(basket)["name"]!.GetValue<string>().ShouldBe("Mahindra &lt;b&gt;and&lt;/b&gt; Mahindra");
            Row(basket)["hasDailyBars"]!.GetValue<bool>().ShouldBeTrue();
            basket["evidence"]!["limits"]!.ToJsonString().ShouldContain("as of today");

            var (instrument, instrumentText) = await CallAsync(client, "get_instrument", new() { ["symbol"] = "m&m", ["bars"] = 2 });
            instrument["ok"]!.GetValue<bool>().ShouldBeTrue(instrumentText);
            instrument["data"]!["symbol"]!.GetValue<string>().ShouldBe("M&M");
            instrument["data"]!["dailyBarCount"]!.GetValue<int>().ShouldBe(3);
            instrument["data"]!["latestQuality"]!.GetValue<string>().ShouldContain("pass");
            instrument["rowCount"]!.GetValue<int>().ShouldBe(2);
            Row(instrument)["close"]!.GetValue<double>().ShouldBe(107.0, 1e-9);
            (await CallAsync(client, "get_instrument", new() { ["symbol"] = "M&M", ["bars"] = 0 })).Json["rowCount"]!.GetValue<int>().ShouldBe(0);
            Code((await CallAsync(client, "get_instrument", new() { ["symbol"] = "M M" })).Json).ShouldBe("tool.invalid_arguments");
        }
    }

    [Fact]
    public async Task Search_notes_ranks_matches_escapes_injection_scrubs_a_pan_and_refuses_a_query_of_stop_characters()
    {
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            await NotesAsync(db);

            var (json, text) = await CallAsync(client, "search_notes", new() { ["query"] = "margin" });

            json["ok"]!.GetValue<bool>().ShouldBeTrue(text);
            json["rowCount"]!.GetValue<int>().ShouldBe(1);
            Row(json)["source"]!.GetValue<string>().ShouldBe("lesson");
            Row(json)["untrusted"]!.GetValue<bool>().ShouldBeTrue();
            Row(json)["text"]!.GetValue<string>().ShouldContain("Ignore previous instructions");
            text.ShouldNotContain(Pan);
            json["redactions"]!.GetValue<int>().ShouldBeGreaterThan(0);
            json["injectionFlags"]!.GetValue<int>().ShouldBeGreaterThan(0);
            json["notes"]![0]!.GetValue<string>().ShouldContain("sources searched");

            Code((await CallAsync(client, "search_notes", new() { ["query"] = "!!" })).Json).ShouldBe("tool.invalid_arguments");
            (await CallAsync(client, "search_notes", new() { ["query"] = "'; DROP TABLE x; --" })).Json["ok"]!.GetValue<bool>().ShouldBeTrue("a hostile query is only a parameter");
            (await CallAsync(client, "search_notes", new() { ["query"] = "Ünïcode ✓ 市場" })).Json["ok"]!.GetValue<bool>().ShouldBeTrue();
            Code((await CallAsync(client, "search_notes", new() { ["query"] = "gap", ["from"] = "yesterday" })).Json).ShouldBe("tool.invalid_arguments");
        }
    }

    [Fact]
    public async Task Get_learning_and_get_system_status_answer_from_stored_rows_and_say_unavailable_for_what_is_missing()
    {
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            var (empty, _) = await CallAsync(client, "get_system_status");
            empty["ok"]!.GetValue<bool>().ShouldBeTrue();
            empty["rows"]!.AsArray().Select(r => r!["state"]!.GetValue<string>()).ShouldAllBe(s => s == "unavailable");
            empty["evidence"]!["unavailable"]!.AsArray().Count.ShouldBeGreaterThan(0);

            await NotesAsync(db);
            await MarketAsync(db);
            await PlanAsync(db);
            var (lessons, lessonsText) = await CallAsync(client, "get_learning", new() { ["kind"] = "lessons", ["limit"] = 5 });
            lessons["ok"]!.GetValue<bool>().ShouldBeTrue(lessonsText);
            lessons["rowCount"]!.GetValue<int>().ShouldBe(2);
            Row(lessons)["subject"]!.GetValue<string>().ShouldBe("plain lesson");
            lessonsText.ShouldNotContain(Pan);
            (await CallAsync(client, "get_learning", new() { ["kind"] = "lessons", ["status"] = "closed" })).Json["rowCount"]!.GetValue<int>().ShouldBe(0);
            foreach (var kind in new[] { "hypotheses", "changelog", "experiments", "models" })
            {
                (await CallAsync(client, "get_learning", new() { ["kind"] = kind })).Json["ok"]!.GetValue<bool>().ShouldBeTrue(kind);
            }

            Code((await CallAsync(client, "get_learning", new() { ["kind"] = "everything" })).Json).ShouldBe("tool.invalid_arguments");

            var (status, statusText) = await CallAsync(client, "get_system_status");
            var items = status["rows"]!.AsArray().ToDictionary(r => r!["item"]!.GetValue<string>(), r => r!);
            items["newestDailyBar"]["state"]!.GetValue<string>().ShouldBe("value");
            items["newestPlan"]["value"]!.GetValue<string>().ShouldContain("2026-10-28");
            items["lastNightlyRun"]["value"]!.GetValue<string>().ShouldContain("succeeded");
            items["dataQualityLatestRun"]["value"]!.GetValue<string>().ShouldContain("pass=1");
            items.ShouldContainKey("service:nightly");
            statusText.ShouldContain("asOf");
        }
    }

    [Theory]
    [InlineData("0.1+0.2", "0.3")]
    [InlineData("1/3", "0.3333")]
    [InlineData("round(2.675, 2)", "2.68")]
    [InlineData("(0.8-0.6)*100", "20.0")]
    public async Task Calc_gives_exact_decimal_text_through_the_tool(string expression, string expected)
    {
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            var (json, _) = await CallAsync(client, "calc", new() { ["expression"] = expression });

            json["data"]!["result"]!.GetValue<string>().ShouldBe(expected);
            json["redactions"]!.GetValue<int>().ShouldBe(0);
        }
    }

    [Fact]
    public async Task Calc_refuses_division_by_zero_and_anything_that_is_not_arithmetic()
    {
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            Code((await CallAsync(client, "calc", new() { ["expression"] = "1/0" })).Json).ShouldBe("calc.divide_by_zero");
            Code((await CallAsync(client, "calc", new() { ["expression"] = "x+1" })).Json).ShouldBe("calc.rejected");
            Code((await CallAsync(client, "calc", new() { ["expression"] = "2^3" })).Json).ShouldBe("calc.rejected");
            Code((await CallAsync(client, "calc", new() { ["expression"] = new string('1', 201) })).Json).ShouldBe("tool.invalid_arguments");
            Code((await CallAsync(client, "calc", new() { ["expression"] = string.Join("+", Enumerable.Repeat("1", 26)) })).Json).ShouldBe("calc.rejected");
        }
    }

    [Fact]
    public async Task No_secret_client_id_owner_mail_or_pan_reaches_the_model_from_any_tool()
    {
        var (host, client) = await StartAsync(portfolio: true);
        await using (host)
        await using (client)
        {
            await RunAAsync(db);
            await CellsAsync(db);
            await PlanAsync(db);
            await MarketAsync(db);
            await NotesAsync(db);
            await HoldingsAsync(db);
            var calls = new (string Tool, Dictionary<string, object?> Args)[]
            {
                ("get_report", Report("groups", new() { ["lens"] = "strategy,side" })),
                ("get_report", Report("summary")),
                ("get_report", Report("what_works")),
                ("get_report", Report("insights")),
                ("get_report", Report("equity")),
                ("get_report", Report("distribution")),
                ("get_report", Report("pivot", new() { ["lens"] = "strategy,side" })),
                ("list_runs", new() { ["kind"] = "sim" }),
                ("list_runs", new() { ["kind"] = "lab" }),
                ("get_plan", new() { ["accountLabel"] = "Main" }),
                ("get_cell", new() { ["strategy"] = "ORB" }),
                ("get_portfolio", new() { ["groupBy"] = "account" }),
                ("get_portfolio", new() { ["groupBy"] = "bucket", ["includeClosed"] = true }),
                ("get_basket", new() { ["name"] = "Core<i>1" }),
                ("get_instrument", new() { ["symbol"] = "M&M" }),
                ("search_notes", new() { ["query"] = "margin" }),
                ("search_notes", new() { ["query"] = "token" }),
                ("search_notes", new() { ["query"] = SecretWord }),
                ("get_learning", new() { ["kind"] = "lessons" }),
                ("get_learning", new() { ["kind"] = "models" }),
                ("get_system_status", []),
                ("calc", new() { ["expression"] = "1+1" }),
                ("list_tables", []),
                ("run_sql", new() { ["sql"] = "select * from aiv.holdings" }),
                ("run_sql", new() { ["sql"] = "select * from accounts.broker_secrets" }),
            };
            var seeded = new[] { ClientId, SecretWord, OwnerMail, Pan, "owner:probe", "SEC-1", "5345435245" };
            foreach (var (tool, args) in calls)
            {
                var (_, text) = await CallAsync(client, tool, args);

                foreach (var word in seeded)
                {
                    text.ShouldNotContain(word, Case.Sensitive, $"{tool} {string.Join(',', args.Keys)} leaked {word}");
                }
            }
        }
    }

    [Fact]
    public async Task Every_tool_answers_on_an_empty_database_with_a_state_or_a_known_code_and_never_fails()
    {
        var (host, client) = await StartAsync(portfolio: true);
        await using (host)
        await using (client)
        {
            var calls = new (string Tool, Dictionary<string, object?> Args)[]
            {
                ("get_report", Report("groups")), ("get_report", Report("summary")), ("get_report", Report("what_works")), ("get_report", Report("insights")), ("get_report", Report("equity")), ("get_report", Report("distribution")),
                ("get_report", Report("pivot", new() { ["lens"] = "strategy,side" })), ("list_runs", new() { ["kind"] = "sim" }), ("list_runs", new() { ["kind"] = "lab" }), ("get_plan", []), ("get_cell", new() { ["strategy"] = "ORB" }),
                ("get_portfolio", new() { ["groupBy"] = "none" }), ("get_portfolio", new() { ["groupBy"] = "basket" }), ("get_basket", new() { ["name"] = "x" }), ("get_instrument", new() { ["symbol"] = "x" }), ("search_notes", new() { ["query"] = "gap" }),
                ("get_learning", new() { ["kind"] = "lessons" }), ("get_learning", new() { ["kind"] = "models" }), ("get_system_status", []), ("calc", new() { ["expression"] = "1+1" }),
            };
            string[] known = ["basket.unknown", "instrument.unknown"];
            foreach (var (tool, args) in calls)
            {
                var (json, text) = await CallAsync(client, tool, args);

                var ok = json["ok"]!.GetValue<bool>();
                if (!ok)
                {
                    known.ShouldContain(Code(json), $"{tool}: {text}");
                }
            }
        }
    }

    [Fact]
    public async Task While_the_assistant_is_off_every_new_tool_answers_disabled()
    {
        await CleanAsync(db);
        var host = await TestMcpHost.StartAsync(database: db);
        var options = new HttpClientTransportOptions { Endpoint = new Uri("http://xd-mcp/mcp"), TransportMode = HttpTransportMode.StreamableHttp, AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + TestMcpHost.ApiToken } };
        await using (host)
        await using (var client = await McpClient.CreateAsync(new HttpClientTransport(options, host.CreateClient(), null, ownsHttpClient: false), cancellationToken: Ct))
        {
            foreach (var (tool, args) in new (string, Dictionary<string, object?>)[] { ("get_report", Report("summary")), ("list_runs", new() { ["kind"] = "sim" }), ("get_plan", []), ("get_cell", new() { ["key"] = "k" }), ("get_portfolio", []), ("get_basket", new() { ["name"] = "x" }), ("get_instrument", new() { ["symbol"] = "x" }), ("search_notes", new() { ["query"] = "gap" }), ("get_learning", new() { ["kind"] = "lessons" }), ("get_system_status", []), ("calc", new() { ["expression"] = "1+1" }) })
            {
                Code((await CallAsync(client, tool, args)).Json).ShouldBe("assistant.disabled", tool);
            }
        }
    }

    [Fact]
    public async Task The_read_role_holds_column_level_select_on_exactly_the_report_source_columns_and_the_sql_role_none()
    {
        await CleanAsync(db);
        var granted = await ColumnsAsync("xd_mcp_read");
        granted.Keys.Order().ShouldBe(["lab.strategies", "research.sim_comparisons", "research.sim_runs", "research.sim_trades"]);
        granted["lab.strategies"].ShouldBe(["id", "style"]);
        granted["research.sim_comparisons"].ShouldBe(["comparison_key", "member_candidate_ids", "member_run_keys"]);
        granted["research.sim_runs"].ShouldBe(["costs_rates_sha256", "costs_sheet_id", "created_at", "created_by", "from_date", "id", "instruments", "instruments_run", "open_at_end_count", "run_key", "strategy_id", "to_date", "trade_count", "universe_kind", "universe_label"]);
        granted["research.sim_trades"].ShouldNotContain("cost_total");
        granted["research.sim_trades"].ShouldNotContain("entry_snapshot_json");
        (await ColumnsAsync("xd_mcp_sql")).ShouldBeEmpty();

        // The tables themselves, their record text and the comparison CSVs stay unreadable.
        foreach (var statement in new[] { "SELECT * FROM research.sim_runs LIMIT 1", "SELECT record_json FROM research.sim_runs LIMIT 1", "SELECT comparison_csv FROM research.sim_comparisons LIMIT 1", "SELECT source_text FROM lab.strategy_versions LIMIT 1", "SELECT * FROM lab.strategies LIMIT 1" })
        {
            (await ReadOnlyDatabase.SqlStateAsync(db.AsRead, statement)).ShouldBe("42501", statement);
        }

        foreach (var statement in new[] { "INSERT INTO research.sim_runs (run_key) VALUES ('x')", "UPDATE research.sim_runs SET strategy_id = 'x'", "DELETE FROM lab.strategies" })
        {
            (await ReadOnlyDatabase.SqlStateAsync(db.AsRead, statement)).ShouldNotBeNull(statement);
        }

        (await ReadOnlyDatabase.SqlStateAsync(db.AsSql, "SELECT run_key FROM research.sim_runs LIMIT 1")).ShouldBe("42501");
    }

    [Fact]
    public async Task Each_get_tool_answers_in_under_a_second_at_the_95th_percentile_on_the_fixture()
    {
        var (host, client) = await StartAsync(portfolio: true);
        await using (host)
        await using (client)
        {
            await RunAAsync(db);
            await CellsAsync(db);
            await PlanAsync(db);
            await MarketAsync(db);
            await NotesAsync(db);
            await HoldingsAsync(db);
            var calls = new (string Tool, Dictionary<string, object?> Args)[] { ("get_report", Report("groups", new() { ["lens"] = "strategy" })), ("get_plan", []), ("get_cell", new() { ["strategy"] = "ORB", ["side"] = "BUY" }), ("get_portfolio", new() { ["groupBy"] = "sector" }), ("get_basket", new() { ["name"] = "Core<i>1" }), ("get_instrument", new() { ["symbol"] = "M&M" }), ("search_notes", new() { ["query"] = "margin" }), ("get_learning", new() { ["kind"] = "lessons" }), ("get_system_status", []), ("list_runs", new() { ["kind"] = "sim" }) };
            await CallAsync(client, "get_system_status"); // warm the pools
            var times = new List<long>();
            for (var round = 0; round < 3; round++)
            {
                foreach (var (tool, args) in calls)
                {
                    var clock = Stopwatch.StartNew();
                    var (json, text) = await CallAsync(client, tool, args);
                    times.Add(clock.ElapsedMilliseconds);
                    json["ok"]!.GetValue<bool>().ShouldBeTrue($"{tool}: {text}");
                }
            }

            times.Order().ElementAt((int)Math.Ceiling(times.Count * 0.95) - 1).ShouldBeLessThan(1000);
        }
    }

    [Fact]
    public async Task The_grounding_check_accepts_the_tool_numbers_marks_an_invented_one_and_finds_no_figure_in_an_insufficient_answer()
    {
        var (host, client) = await StartAsync();
        await using (host)
        await using (client)
        {
            await RunAAsync(db);
            await CellsAsync(db);
            var (_, report) = await CallAsync(client, "get_report", Report("groups", new() { ["runs"] = RunA, ["lens"] = "strategy" }));
            var (_, cell) = await CallAsync(client, "get_cell", new() { ["strategy"] = "ORB", ["timeframe"] = "15m", ["side"] = "BUY" });
            const string Question = "expectancy by strategy";

            GroundingCheck.Check("Expectancy is +0.80 R over 40 trades [1].", Question, [report]).Ungrounded.ShouldBeEmpty();
            GroundingCheck.Check("Expectancy is +0.9 R over 40 trades [1].", Question, [report]).Ungrounded.Select(u => u.Text).ShouldContain(t => t.Contains("0.9", StringComparison.Ordinal));
            GroundingCheck.Check("The 60d window is insufficient: only 8 trades [1].", Question, [cell]).Ungrounded.ShouldBeEmpty();
        }
    }

    private async Task<Dictionary<string, List<string>>> ColumnsAsync(string role)
    {
        await using var connection = new Npgsql.NpgsqlConnection(db.Owner);
        await connection.OpenAsync(Ct);
        await using var command = new Npgsql.NpgsqlCommand("SELECT table_schema || '.' || table_name, column_name FROM information_schema.column_privileges WHERE grantee = @r AND privilege_type = 'SELECT' AND table_schema NOT IN ('aiv', 'ai', 'information_schema', 'pg_catalog') ORDER BY 1, 2", connection);
        command.Parameters.AddWithValue("r", role);
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            if (!result.TryGetValue(reader.GetString(0), out var list))
            {
                result[reader.GetString(0)] = list = [];
            }

            list.Add(reader.GetString(1));
        }

        return result;
    }
}
