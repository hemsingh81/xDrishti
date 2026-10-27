using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Mcp.Tests.Support;

namespace XDrishti.Mcp.Tests;

/// <summary>Spec P9-02 AC 11 and 12: the real host on an in-memory server with a real MCP client: the token, the host name, the body cap, the rate limit, the tool list and its read-only annotations.</summary>
public sealed class McpTransportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpRequestMessage Post(string uri, string body, string? token = TestMcpHost.ApiToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static async Task<McpClient> ConnectAsync(TestMcpHost host, string token = TestMcpHost.ApiToken)
    {
        var options = new HttpClientTransportOptions { Endpoint = new Uri("http://xd-mcp/mcp"), TransportMode = HttpTransportMode.StreamableHttp, AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token } };
        return await McpClient.CreateAsync(new HttpClientTransport(options, host.CreateClient(), null, ownsHttpClient: false), cancellationToken: Ct);
    }

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content.Single()).Text;

    [Fact]
    public async Task Without_a_token_or_with_a_wrong_one_the_answer_is_401_and_the_same()
    {
        await using var host = await TestMcpHost.StartAsync();
        using var client = host.CreateClient();
        const string Body = """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""";

        var none = await client.SendAsync(Post("http://xd-mcp/mcp", Body, token: null), Ct);
        var wrong = await client.SendAsync(Post("http://xd-mcp/mcp", Body, token: "not-the-token"), Ct);
        var empty = await client.SendAsync(Post("http://xd-mcp/mcp", Body, token: string.Empty), Ct);

        none.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        empty.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await none.Content.ReadAsStringAsync(Ct)).ShouldBe(await wrong.Content.ReadAsStringAsync(Ct));
        none.Headers.WwwAuthenticate.ToString().ShouldBe(wrong.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task A_client_whose_configured_token_is_empty_cannot_call_with_an_empty_one()
    {
        await using var host = await TestMcpHost.StartAsync(c => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:Tokens:Hermes"] = string.Empty }));
        using var client = host.CreateClient();

        var response = await client.SendAsync(Post("http://xd-mcp/mcp", """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", token: " "), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task With_no_token_configured_at_all_every_call_is_refused()
    {
        await using var host = await TestMcpHost.StartAsync(c => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:Tokens:Api"] = string.Empty, ["Mcp:Tokens:Other"] = string.Empty }));
        using var client = host.CreateClient();

        (await client.SendAsync(Post("http://xd-mcp/mcp", "{}", token: TestMcpHost.ApiToken), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_host_name_other_than_xd_mcp_is_refused()
    {
        await using var host = await TestMcpHost.StartAsync();
        using var client = host.CreateClient();

        (await client.SendAsync(Post("http://evil.example/mcp", "{}"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync("http://localhost/health", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_body_over_64_KB_is_refused()
    {
        await using var host = await TestMcpHost.StartAsync();
        using var client = host.CreateClient();
        var body = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"run_sql","arguments":{"sql":"x"}}}""" + new string(' ', 70_000);

        var response = await client.SendAsync(Post("http://xd-mcp/mcp", body), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Theory]
    [InlineData("/MCP")]
    [InlineData("/Mcp")]
    [InlineData("/mcp/")]
    [InlineData("//mcp")]
    [InlineData("/mcp/x")]
    [InlineData("/%6Dcp")]
    [InlineData("/%4Dcp")]
    [InlineData("/mcp%2F")]
    [InlineData("/nothing")]
    [InlineData("/HEALTH/")]
    [InlineData("/")]
    public async Task Every_path_but_health_needs_the_token_whatever_its_casing_slashes_or_encoding(string path)
    {
        await using var host = await TestMcpHost.StartAsync();
        using var client = host.CreateClient();

        var response = await client.SendAsync(Post("http://xd-mcp" + path, """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", token: null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
    }

    [Theory]
    [InlineData("/MCP")]
    [InlineData("/Mcp")]
    public async Task A_differently_cased_path_has_the_body_cap_and_the_rate_limit_too(string path)
    {
        await using var host = await TestMcpHost.StartAsync();
        using var client = host.CreateClient();

        var big = await client.SendAsync(Post("http://xd-mcp" + path, new string(' ', 70_000)), Ct);
        big.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        for (var i = 0; i < 60; i++)
        {
            await client.SendAsync(Post("http://xd-mcp" + path, """{"jsonrpc":"2.0","id":1,"method":"tools/list"}"""), Ct);
        }

        (await client.SendAsync(Post("http://xd-mcp" + path, "{}"), Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/HEALTH")]
    public async Task Health_stays_anonymous_in_any_casing(string path)
    {
        await using var host = await TestMcpHost.StartAsync();
        using var client = host.CreateClient();

        (await client.GetAsync("http://xd-mcp" + path, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task More_than_sixty_calls_a_minute_per_token_give_429_and_a_new_minute_starts_again_and_another_token_has_its_own_count()
    {
        await using var host = await TestMcpHost.StartAsync();
        using var client = host.CreateClient();
        const string Body = """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""";

        for (var i = 0; i < 60; i++)
        {
            (await client.SendAsync(Post("http://xd-mcp/mcp", Body), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, $"call {i + 1}");
        }

        var sixtyFirst = await client.SendAsync(Post("http://xd-mcp/mcp", Body), Ct);
        sixtyFirst.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        sixtyFirst.Headers.RetryAfter.ShouldNotBeNull();
        (await client.SendAsync(Post("http://xd-mcp/mcp", Body, TestMcpHost.OtherToken), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        host.Time.Advance(TimeSpan.FromSeconds(61));

        (await client.SendAsync(Post("http://xd-mcp/mcp", Body), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_tool_list_is_exactly_the_fifteen_tools_with_schemas_that_forbid_extra_properties_and_read_only_annotations()
    {
        await using var host = await TestMcpHost.StartAsync();
        await using var client = await ConnectAsync(host);

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        tools.Select(t => t.Name).Order().ShouldBe(["calc", "describe_table", "get_basket", "get_cell", "get_instrument", "get_learning", "get_plan", "get_portfolio", "get_report", "get_system_status", "lab_validate", "list_runs", "list_tables", "run_sql", "search_notes"]);
        foreach (var tool in tools)
        {
            var schema = tool.ProtocolTool.InputSchema;
            schema.GetProperty("type").GetString().ShouldBe("object");
            schema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse(tool.Name);
            var annotations = tool.ProtocolTool.Annotations!;
            annotations.ReadOnlyHint.ShouldBe(true, tool.Name);
            annotations.DestructiveHint.ShouldBe(false, tool.Name);
            annotations.IdempotentHint.ShouldBe(true, tool.Name);
            annotations.OpenWorldHint.ShouldBe(false, tool.Name);
        }

        tools.Single(t => t.Name == "run_sql").ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ShouldBe(["sql"]);
    }

    [Fact]
    public async Task No_tool_name_or_description_is_a_write_or_an_order()
    {
        await using var host = await TestMcpHost.StartAsync();
        await using var client = await ConnectAsync(host);

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        foreach (var tool in tools)
        {
            tool.Name.ShouldNotMatch("(?i)(insert|update|delete|write|place|order|set_|create|drop|execute|send|cancel|modify)");
            tool.Description.ShouldNotMatch("(?i)\\b(insert|update|delete|write|drop|execute|send|cancel|modify|recommend)\\b"); // read-only is the annotation (checked in the test above), not a word every description has to spend tokens on
            tool.ProtocolTool.Annotations!.ReadOnlyHint.ShouldBe(true, tool.Name);
        }
    }

    [Fact]
    public async Task A_tool_call_answers_with_the_envelope_as_text()
    {
        await using var host = await TestMcpHost.StartAsync();
        await using var client = await ConnectAsync(host);

        var result = await client.CallToolAsync("list_tables", new Dictionary<string, object?>(), cancellationToken: Ct);

        var json = JsonNode.Parse(Text(result))!;
        json["ok"]!.GetValue<bool>().ShouldBeTrue();
        json["tool"]!.GetValue<string>().ShouldBe("list_tables");
        json["rows"]!.AsArray().Count.ShouldBe(36);
        Text(result).Length.ShouldBeLessThanOrEqualTo(6000);
    }

    [Fact]
    public async Task While_the_assistant_is_off_every_tool_answers_disabled_and_no_other_port_is_used()
    {
        await using var host = await TestMcpHost.StartAsync(context: new FakeContext(enabled: false));
        await using var client = await ConnectAsync(host);

        foreach (var (name, args) in new (string, Dictionary<string, object?>)[] { ("list_tables", []), ("describe_table", new() { ["name"] = "sim_trades" }), ("run_sql", new() { ["sql"] = "select 1" }), ("lab_validate", new() { ["yaml"] = "valid" }) })
        {
            var json = JsonNode.Parse(Text(await client.CallToolAsync(name, args, cancellationToken: Ct)))!;

            json["ok"]!.GetValue<bool>().ShouldBeFalse(name);
            json["error"]!["code"]!.GetValue<string>().ShouldBe("assistant.disabled", name);
        }

        host.Catalog.Calls.ShouldBe(0);
        host.Sql.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_call_with_bad_or_extra_arguments_gives_invalid_arguments_and_never_an_exception()
    {
        await using var host = await TestMcpHost.StartAsync();
        await using var client = await ConnectAsync(host);

        var extra = JsonNode.Parse(Text(await client.CallToolAsync("list_tables", new Dictionary<string, object?> { ["colour"] = "red" }, cancellationToken: Ct)))!;
        var wrongType = JsonNode.Parse(Text(await client.CallToolAsync("run_sql", new Dictionary<string, object?> { ["sql"] = 5 }, cancellationToken: Ct)))!;
        var outOfRange = JsonNode.Parse(Text(await client.CallToolAsync("run_sql", new Dictionary<string, object?> { ["sql"] = "select 1", ["maxRows"] = 500 }, cancellationToken: Ct)))!;
        var missing = JsonNode.Parse(Text(await client.CallToolAsync("describe_table", new Dictionary<string, object?>(), cancellationToken: Ct)))!;
        var secretTable = JsonNode.Parse(Text(await client.CallToolAsync("describe_table", new Dictionary<string, object?> { ["name"] = "accounts.broker_secrets" }, cancellationToken: Ct)))!;
        var tooLong = JsonNode.Parse(Text(await client.CallToolAsync("lab_validate", new Dictionary<string, object?> { ["yaml"] = new string('x', 65_537) }, cancellationToken: Ct)))!;

        foreach (var (json, property) in new[] { (extra, "colour"), (wrongType, "sql"), (outOfRange, "maxRows"), (missing, "name"), (secretTable, "name"), (tooLong, "yaml") })
        {
            json["ok"]!.GetValue<bool>().ShouldBeFalse(property);
            json["error"]!["code"]!.GetValue<string>().ShouldBe("tool.invalid_arguments", property);
            json["error"]!["message"]!.GetValue<string>().ShouldContain($"'{property}'");
        }
    }

    [Fact]
    public async Task Describe_table_returns_the_columns_and_an_unknown_name_gives_table_unknown()
    {
        await using var host = await TestMcpHost.StartAsync();
        await using var client = await ConnectAsync(host);

        var known = JsonNode.Parse(Text(await client.CallToolAsync("describe_table", new Dictionary<string, object?> { ["name"] = "sim_trades" }, cancellationToken: Ct)))!;
        var unknown = JsonNode.Parse(Text(await client.CallToolAsync("describe_table", new Dictionary<string, object?> { ["name"] = "nope" }, cancellationToken: Ct)))!;

        known["rows"]!.AsArray().Select(r => r!["name"]!.GetValue<string>()).ShouldContain("run_key");
        known["data"]!["name"]!.GetValue<string>().ShouldBe("sim_trades");
        unknown["error"]!["code"]!.GetValue<string>().ShouldBe("table.unknown");
    }

    [Fact]
    public async Task Run_sql_passes_a_checked_statement_to_the_runner_and_labels_the_answer()
    {
        var sql = new FakeSql(new SqlRunOutcome(SqlRunStatus.Ok, [new SqlColumn("symbol", "character varying"), new SqlColumn("n", "bigint")], [["TCS", 5L]], false, 4, null));
        await using var host = await TestMcpHost.StartAsync(sql: sql);
        await using var client = await ConnectAsync(host);

        var json = JsonNode.Parse(Text(await client.CallToolAsync("run_sql", new Dictionary<string, object?> { ["sql"] = "select symbol, count(*) as n from aiv.sim_trades group by 1;" }, cancellationToken: Ct)))!;

        json["ok"]!.GetValue<bool>().ShouldBeTrue();
        json["rows"]![0]!["symbol"]!.GetValue<string>().ShouldBe("TCS");
        json["evidence"]!["basis"]!.GetValue<string>().ShouldBe("ad-hoc query, not checked by the report engine");
        json["evidence"]!["sqlHash"]!.GetValue<string>().ShouldBe(XDrishti.Domain.Assistant.SqlPreCheck.Hash("select symbol, count(*) as n from aiv.sim_trades group by 1;"));
        sql.Asked.Single().Statement.ShouldBe("select symbol, count(*) as n from aiv.sim_trades group by 1");
        sql.Asked.Single().MaxRows.ShouldBe(50);
        sql.Asked.Single().Timeout.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Run_sql_cleans_only_the_cells_of_text_columns_and_decides_by_the_manifest_name_then_the_database_type()
    {
        var hash = new string('f', 64);
        var outcome = new SqlRunOutcome(
            SqlRunStatus.Ok,
            [new SqlColumn("description", "character varying(500)"), new SqlColumn("alias", "text"), new SqlColumn("h", "character(64)"), new SqlColumn("u", "uuid"), new SqlColumn("symbol", "text"), new SqlColumn("tags", "text[]"), new SqlColumn("run_key", "character(64)"), new SqlColumn("empty_alias", "text")],
            [["<b>PAN ABCDE1234F</b>", "PAN ABCDE1234F", hash, "123e4567-e89b-12d3-a456-426614174000", "12345678901", new List<object?> { "ABCDE1234F", "ok" }, hash, null]],
            false,
            2,
            null);
        await using var host = await TestMcpHost.StartAsync(sql: new FakeSql(outcome));
        await using var client = await ConnectAsync(host);

        var row = JsonNode.Parse(Text(await client.CallToolAsync("run_sql", new Dictionary<string, object?> { ["sql"] = "select 1" }, cancellationToken: Ct)))!["rows"]![0]!;

        row["description"]!.GetValue<string>().ShouldBe("&lt;b&gt;PAN [redacted]&lt;/b&gt;");
        row["alias"]!.GetValue<string>().ShouldBe("PAN [redacted]");
        row["h"]!.GetValue<string>().ShouldBe(hash, "a hash under another name is a hash");
        row["u"]!.GetValue<string>().ShouldBe("123e4567-e89b-12d3-a456-426614174000", "a uuid is not text");
        row["symbol"]!.GetValue<string>().ShouldBe("12345678901", "the manifest knows symbol as an identifier");
        row["tags"]![0]!.GetValue<string>().ShouldBe("[redacted]");
        row["tags"]![1]!.GetValue<string>().ShouldBe("ok");
        row["run_key"]!.GetValue<string>().ShouldBe(hash);
    }

    [Theory]
    [InlineData("select 1; select 2", "multiple_statements")]
    [InlineData("select 1 -- x", "comment")]
    [InlineData("delete from aiv.t", "not_select")]
    [InlineData("select * into temp t from aiv.instruments", "into")]
    public async Task A_refused_query_never_reaches_the_runner(string text, string reason)
    {
        var sql = new FakeSql();
        await using var host = await TestMcpHost.StartAsync(sql: sql);
        await using var client = await ConnectAsync(host);

        var json = JsonNode.Parse(Text(await client.CallToolAsync("run_sql", new Dictionary<string, object?> { ["sql"] = text }, cancellationToken: Ct)))!;

        json["error"]!["code"]!.GetValue<string>().ShouldBe("sql.rejected");
        json["error"]!["message"]!.GetValue<string>().ShouldContain(reason);
        sql.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refusal_shows_the_sqlstate_and_a_fixed_text_never_the_server_message()
    {
        await using var host = await TestMcpHost.StartAsync(sql: new FakeSql(SqlRunOutcome.Failed(SqlRunStatus.Error, 5, "22P02")));
        await using var client = await ConnectAsync(host);

        var json = JsonNode.Parse(Text(await client.CallToolAsync("run_sql", new Dictionary<string, object?> { ["sql"] = "select 1" }, cancellationToken: Ct)))!;

        json["error"]!["code"]!.GetValue<string>().ShouldBe("sql.error");
        json["error"]!["message"]!.GetValue<string>().ShouldContain("SQLSTATE 22P02");
    }

    [Theory]
    [InlineData(SqlRunStatus.Denied, "sql.denied")]
    [InlineData(SqlRunStatus.Timeout, "sql.timeout")]
    [InlineData(SqlRunStatus.Error, "sql.error")]
    public async Task A_database_refusal_gives_its_code(SqlRunStatus status, string code)
    {
        await using var host = await TestMcpHost.StartAsync(sql: new FakeSql(SqlRunOutcome.Failed(status, 5, "column x does not exist")));
        await using var client = await ConnectAsync(host);

        var json = JsonNode.Parse(Text(await client.CallToolAsync("run_sql", new Dictionary<string, object?> { ["sql"] = "select x from aiv.instruments" }, cancellationToken: Ct)))!;

        json["ok"]!.GetValue<bool>().ShouldBeFalse();
        json["error"]!["code"]!.GetValue<string>().ShouldBe(code);
    }

    [Fact]
    public async Task Lab_validate_returns_the_verdict_the_hash_and_the_problems()
    {
        await using var host = await TestMcpHost.StartAsync();
        await using var client = await ConnectAsync(host);

        var json = JsonNode.Parse(Text(await client.CallToolAsync("lab_validate", new Dictionary<string, object?> { ["yaml"] = "valid: true" }, cancellationToken: Ct)))!;

        json["data"]!["valid"]!.GetValue<bool>().ShouldBeTrue();
        json["data"]!["hash"]!.GetValue<string>().ShouldBe("abc");
        json["data"]!["problems"]!.AsArray().Count.ShouldBe(0);
    }

    [Fact]
    public async Task The_health_endpoint_is_anonymous_says_nothing_and_follows_the_database()
    {
        var context = new FakeContext();
        await using var host = await TestMcpHost.StartAsync(context: context);
        using var client = host.CreateClient();

        var healthy = await client.GetAsync("http://xd-mcp/health", Ct);
        context.Down = true;
        var down = await client.GetAsync("http://xd-mcp/health", Ct);

        healthy.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await healthy.Content.ReadAsStringAsync(Ct)).ShouldBe("\"ok\"");
        down.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await down.Content.ReadAsStringAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_server_has_no_session_each_call_stands_alone()
    {
        await using var host = await TestMcpHost.StartAsync();
        using var client = host.CreateClient();

        var response = await client.SendAsync(Post("http://xd-mcp/mcp", """{"jsonrpc":"2.0","id":1,"method":"tools/list"}"""), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Mcp-Session-Id").ShouldBeFalse();
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("list_tables");
    }

    [Fact]
    public async Task The_container_health_probe_asks_the_health_endpoint_with_the_one_accepted_host_name_and_follows_the_database()
    {
        var context = new FakeContext();
        await using var host = await TestMcpHost.StartAsync(context: context, kestrel: true);

        (await HealthProbe.RunAsync($"{host.Address}/health")).ShouldBe(0);

        context.Down = true;
        (await HealthProbe.RunAsync($"{host.Address}/health")).ShouldBe(1);
        (await HealthProbe.RunAsync("http://127.0.0.1:1/health")).ShouldBe(1, "nothing listens there");
    }
}
