using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Infrastructure.Ai.Evaluation;
using XDrishti.Infrastructure.Ai.ReadOnly;
using XDrishti.Infrastructure.Ai.ReadOnly.EvidenceApi;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>
/// AI-01 AC 3 and 4 (ADR 0280): the eval's evidence questions have real goldens. The fixture (with its replay run, strategy versions and shadow session) is seeded into <c>xdrishti_eval</c>, the reports-only API serves it, and the tool server's own evidence reader reads it with the service token, as in the eval stack. The
/// figures asserted here are the ones written into <c>eval/sets/evidence-questions.jsonl</c>.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class EvalEvidenceGoldenTests(ApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> _eval = null!;
    private string _connection = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        await using (var connection = new NpgsqlConnection(factory.AdminConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var drop = new NpgsqlCommand("DROP DATABASE IF EXISTS xdrishti_eval WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync(Ct);
            NpgsqlConnection.ClearAllPools(); // the pooled connections of the earlier test's database are dead
            await using var create = new NpgsqlCommand("CREATE DATABASE xdrishti_eval", connection);
            await create.ExecuteNonQueryAsync(Ct);
        }

        admin.Database = "xdrishti_eval";
        _connection = admin.ConnectionString;
        _eval = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:ConnectionString", _connection);
            b.UseSetting("Api:ReportsOnly", "true");
            b.UseSetting("BrokerVault:Key", string.Empty);
            b.UseSetting("ExecHandover:Key", string.Empty);
        });
        await DatabaseMigrator.MigrateAsync(_eval.Services);
        await using (var clean = new NpgsqlConnection(_connection))
        {
            // the test host's own startup seeds its three baskets; the real migrate step leaves the table empty
            await clean.OpenAsync(Ct);
            await using var delete = new NpgsqlCommand("TRUNCATE market.baskets CASCADE", clean);
            await delete.ExecuteNonQueryAsync(Ct);
        }

        var text = File.ReadAllText(FindFixture());
        var fixture = EvalFixtureReader.Parse(text).Value;
        (await FixtureSeeder.SeedAsync(_connection, fixture, text, "xdrishti_eval", Ct)).ShouldBe(SeedOutcome.Seeded);
    }

    public async ValueTask DisposeAsync()
    {
        await _eval.DisposeAsync();
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand("DROP DATABASE IF EXISTS xdrishti_eval WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
        NpgsqlConnection.ClearAllPools();
    }

    private static string FindFixture()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "eval", "fixture", "fixture.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("eval/fixture/fixture.json");
    }

    private ApiEvidenceReader Reader(string? token = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:Api:BaseUrl"] = "http://xd-api-eval:8080", ["Mcp:Api:Token"] = token ?? factory.ServiceToken }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddAiBasketApi(configuration);
        services.AddAiEvidenceApi();
        services.AddHttpClient<EvidenceApiClient>().ConfigurePrimaryHttpMessageHandler(() => _eval.Server.CreateHandler());
        return (ApiEvidenceReader)services.BuildServiceProvider().GetRequiredService<IAiEvidenceReader>();
    }

    [Fact]
    public async Task EQ01_the_last_replay_returned_1_2_percent_before_costs_with_its_labels_the_fence_and_the_survivorship_label_of_its_sessions()
    {
        var reader = Reader();

        var list = await reader.GetEvidenceAsync("replay_list", null, null, null, Ct);
        list.ErrorCode.ShouldBeNull(list.ErrorMessage);
        var run = list.Data!["body"]![0]!;
        run["finalEquity"]!.GetValue<decimal>().ShouldBe(1_012_000m);
        run["costsLabel"]!.GetValue<string>().ShouldContain("before costs");
        var answer = await reader.GetEvidenceAsync("replay_run", run["id"]!.GetValue<string>(), null, null, Ct);

        answer.ErrorCode.ShouldBeNull(answer.ErrorMessage);
        var body = answer.Data!["body"]!;
        body["figures"]!["returnPct"]!.GetValue<decimal>().ShouldBe(1.2m);
        body["figures"]!["trades"]!.GetValue<int>().ShouldBe(2);
        body["basis"]!.GetValue<string>().ShouldBe("gross");
        body["hindsight"]!.AsArray().Select(l => l!["code"]!.GetValue<string>()).ShouldBe(["before_costs", "data_seen_before", "filters_without_history", "settings_now", "survivorship", "versions_start"]);
        answer.Data["fence"]!.GetValue<string>().ShouldBe(ApiEvidenceReader.ReplayFence);
    }

    [Fact]
    public async Task EQ02_and_EQ03_the_strategy_ORB_is_not_ready_and_its_effect_sub_score_is_unavailable_for_want_of_a_forward_mean()
    {
        var reader = Reader();

        var list = await reader.GetEvidenceAsync("readiness_list", null, null, null, Ct);
        var card = await reader.GetEvidenceAsync("readiness_card", null, "ORB", 1, Ct);

        list.ErrorCode.ShouldBeNull(list.ErrorMessage);
        list.Data!["body"]!["strategies"]!.AsArray().Select(r => r!["strategy"]!.GetValue<string>()).ShouldBe(["ORB", "VWAP_PULLBACK"]);
        list.Data["body"]!["system"]!["labelText"]!.GetValue<string>().ShouldBe("Not ready");
        card.ErrorCode.ShouldBeNull(card.ErrorMessage);
        var body = card.Data!["body"]!;
        (body["label"]!.GetValue<string>(), body["labelText"]!.GetValue<string>(), body["score"]!.GetValue<int>()).ShouldBe(("not_ready", "Not ready", 0));
        var effect = body["subScores"]!.AsArray().Single(s => s!["name"]!.GetValue<string>() == "Effect")!;
        effect["state"]!.GetValue<string>().ShouldBe("unavailable");
        effect["score"].ShouldBeNull();
        effect["reading"]!.GetValue<string>().ShouldBe("no forward mean, spread or effective n");
        (await reader.GetEvidenceAsync("readiness_card", null, "NOPE", 1, Ct)).ErrorCode.ShouldBe("evidence.not_found");
    }

    [Fact]
    public async Task EQ04_the_shadow_record_has_a_mean_R_of_0_3083_over_12_graded_candidates_with_a_90_percent_interval_and_says_it_is_a_separate_count()
    {
        var answer = await Reader().GetEvidenceAsync("shadow_summary", null, null, null, Ct);

        answer.ErrorCode.ShouldBeNull(answer.ErrorMessage);
        var body = answer.Data!["body"]!;
        body["separateCount"]!.GetValue<string>().ShouldContain("separate count");
        var all = body["groups"]!.AsArray().Single(g => g!["group"]!.GetValue<string>() == "all")!;
        all["n"]!.GetValue<int>().ShouldBe(12);
        all["meanR"]!["number"]!.GetValue<double>().ShouldBe(37.0 / 120, 1e-12);
        all["low90"]!["number"]!.GetValue<double>().ShouldBe(-5.0 / 24, 1e-12);
        all["high90"]!["number"]!.GetValue<double>().ShouldBe(49.0 / 60, 1e-12);
        body["costs"]!["gross"]!.GetValue<bool>().ShouldBeTrue();
        answer.Data.AsObject().ContainsKey("fence").ShouldBeFalse();
    }

    [Fact]
    public async Task With_a_wrong_token_every_kind_is_tool_unavailable_which_the_grader_marks_not_evaluable()
    {
        var reader = Reader(token: "not-the-service-token");

        foreach (var kind in new[] { "replay_list", "readiness_list", "shadow_summary" })
        {
            var down = await reader.GetEvidenceAsync(kind, null, null, null, Ct);

            down.ErrorCode.ShouldBe("tool.unavailable");
            down.Data.ShouldBeNull();
        }
    }

    [Fact]
    public async Task The_reports_only_host_answers_404_on_every_route_of_the_three_modules_but_the_six_evidence_gets()
    {
        using var client = _eval.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", factory.ServiceToken);
        var id = Guid.NewGuid();

        foreach (var (method, path) in new[] { ("POST", "/api/replay/runs"), ("GET", $"/api/replay/runs/{id}/trades"), ("GET", $"/api/replay/runs/{id}/sessions"), ("PUT", "/api/paper/shadow/settings"), ("GET", "/api/confidence/readiness-settings"), ("PUT", "/api/confidence/readiness-settings") })
        {
            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative)), Ct);

            response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound, $"{method} {path}");
        }

        foreach (var path in new[] { "/api/replay/runs", "/api/confidence/scorecards", "/api/paper/shadow/summary" })
        {
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

            response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, path);
        }
    }
}
