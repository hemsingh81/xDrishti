using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai;
using XDrishti.Infrastructure.Ai.Assistant;
using XDrishti.Mcp.Tests.Support;

namespace XDrishti.Mcp.Tests;

/// <summary>
/// Spec P9-03 contract tests of the client against the real <c>xd-mcp</c> host on a loopback port: the tool list with the bearer token, a call of <c>lab_validate</c> round trip, a wrong token or a stopped server as a state (never an exception), no address or token configured as disabled,
/// and the schema budget and golden file over all 16 tools (spec P9-05).
/// </summary>
public sealed class McpToolCatalogTests
{
    private static readonly JsonSerializerOptions GoldenOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static McpToolCatalog Catalog(string url, string token) =>
        new(Options.Create(new AssistantMcpOptions { Url = url, Token = token, CatalogCacheSeconds = 60, CallTimeoutSeconds = 10 }), TimeProvider.System, NullLoggerFactory.Instance);

    private static Task<TestMcpHost> StartAsync() => TestMcpHost.StartAsync(c => c.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "127.0.0.1" }), kestrel: true);

    [Fact]
    public async Task The_tools_are_listed_with_their_schemas_and_fit_the_schema_budget()
    {
        await using var host = await StartAsync();
        using var catalog = Catalog($"{host.Address}/mcp", TestMcpHost.ApiToken);

        var snapshot = await catalog.ListAsync(Ct);

        snapshot.State.ShouldBe(ToolCatalogState.Ready);
        snapshot.Tools.Select(t => t.Name).Order().ShouldBe(["calc", "describe_table", "get_basket", "get_cell", "get_instrument", "get_learning", "get_plan", "get_portfolio", "get_report", "get_system_status", "lab_validate", "list_runs", "list_tables", "run_sql", "search_notes"]);
        snapshot.Tools.ShouldAllBe(t => t.Description.Length > 0 && JsonDocument.Parse(t.InputSchemaJson).RootElement.ValueKind == JsonValueKind.Object);

        // The 16 tools the model sees (these 15 and the local make_chart) fit the schema budget, each description is at most 40 words, and the whole list equals the golden file (spec P9-05 AC 15 and 16).
        var all = snapshot.Tools.Append(ChartTool.Descriptor).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        all.Count.ShouldBe(16);
        ContextBudget.SchemaTokens(all).ShouldBeLessThanOrEqualTo(ContextBudget.SchemaTokenLimit);
        all.ShouldAllBe(t => t.Description.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 40, "descriptions are at most 40 words");
        var golden = JsonSerializer.Serialize(all.Select(t => new { t.Name, t.Description, Schema = JsonDocument.Parse(t.InputSchemaJson).RootElement }), GoldenOptions).ReplaceLineEndings("\n") + "\n";
        var path = Path.Combine(Path.GetDirectoryName(ThisFile())!, "Golden", "tool-schemas.json");
        if (Environment.GetEnvironmentVariable("XD_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, golden, Ct);
        }

        (await File.ReadAllTextAsync(path, Ct)).ReplaceLineEndings("\n").ShouldBe(golden, "the tool schemas changed: review them, then run once with XD_UPDATE_GOLDEN=1");
        snapshot.Tools.Single(t => t.Name == "get_report").Description.ShouldContain("gross");
        snapshot.Tools.Single(t => t.Name == "get_report").Description.ShouldContain("state");
        snapshot.Tools.Single(t => t.Name == "get_report").Description.ShouldContain("n,");
    }

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Fact]
    public async Task A_call_goes_through_and_the_envelope_text_comes_back()
    {
        await using var host = await StartAsync();
        using var catalog = Catalog($"{host.Address}/mcp", TestMcpHost.ApiToken);

        var call = await catalog.CallAsync("lab_validate", """{"yaml":"valid: true"}""", Ct);

        call.Reachable.ShouldBeTrue();
        var facts = ToolResultReader.Read(call.Text);
        facts.Parsed.ShouldBeTrue();
        facts.Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task Arguments_that_are_not_a_json_object_are_a_tool_error_from_the_client_without_a_call()
    {
        await using var host = await StartAsync();
        using var catalog = Catalog($"{host.Address}/mcp", TestMcpHost.ApiToken);

        var call = await catalog.CallAsync("lab_validate", "[1,2]", Ct);

        call.Reachable.ShouldBeTrue();
        ToolResultReader.Read(call.Text).ErrorCode.ShouldBe("tool.invalid_arguments");
    }

    [Fact]
    public async Task A_wrong_token_is_a_down_state_and_an_unreachable_call()
    {
        await using var host = await StartAsync();
        using var catalog = Catalog($"{host.Address}/mcp", "not-the-token-0123456789");

        (await catalog.ListAsync(Ct)).State.ShouldBe(ToolCatalogState.Down);
        (await catalog.CallAsync("lab_validate", """{"yaml":"x"}""", Ct)).Reachable.ShouldBeFalse();
    }

    [Fact]
    public async Task A_stopped_server_is_down_and_a_missing_address_or_token_is_disabled()
    {
        using var down = Catalog("http://127.0.0.1:1/mcp", TestMcpHost.ApiToken);
        using var noToken = Catalog("http://127.0.0.1:1/mcp", string.Empty);
        using var noUrl = Catalog(string.Empty, TestMcpHost.ApiToken);

        (await down.ListAsync(Ct)).State.ShouldBe(ToolCatalogState.Down);
        (await down.CallAsync("list_tables", "{}", Ct)).Reachable.ShouldBeFalse();
        (await noToken.ListAsync(Ct)).State.ShouldBe(ToolCatalogState.Disabled);
        (await noUrl.ListAsync(Ct)).State.ShouldBe(ToolCatalogState.Disabled);
        (await noUrl.CallAsync("list_tables", "{}", Ct)).Reachable.ShouldBeFalse();
    }

    [Fact]
    public async Task The_list_is_cached_and_the_options_never_show_the_token()
    {
        await using var host = await StartAsync();
        var options = new AssistantMcpOptions { Url = $"{host.Address}/mcp", Token = TestMcpHost.ApiToken };
        using var catalog = new McpToolCatalog(Options.Create(options), TimeProvider.System, NullLoggerFactory.Instance);

        var first = await catalog.ListAsync(Ct);
        var second = await catalog.ListAsync(Ct);

        second.ShouldBeSameAs(first);
        options.ToString().ShouldNotContain(TestMcpHost.ApiToken);
    }
}
