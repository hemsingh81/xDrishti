using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Infrastructure.Ai.Evaluation;
using XDrishti.Infrastructure.Ai.ReadOnly;
using XDrishti.Infrastructure.Ai.ReadOnly.BasketApi;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>
/// BK-12b AC 11 and 12: the eval's basket questions BQ-01, BQ-02, BQ-04 and BQ-06 have real numeric goldens. The synthetic fixture is seeded into a database named <c>xdrishti_eval</c> of the test server, the reports-only API (<c>Api:ReportsOnly</c>) serves it, and the tool server's own reader and client
/// read it with the service token, exactly as in the eval stack. The numbers asserted here are the ones written into <c>eval/sets/basket-questions.jsonl</c>.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class EvalBasketGoldenTests(ApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> _eval = null!;
    private string _connection = string.Empty;
    private readonly List<AiBasketRef> _refs = [];

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

        await using var read = new NpgsqlConnection(_connection);
        await read.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT id, name FROM market.baskets", read);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            _refs.Add(new AiBasketRef(reader.GetGuid(0), reader.GetString(1)));
        }
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

    private sealed class Data(IReadOnlyList<AiBasketRef> baskets) : IAiDataReader
    {
        public Task<IReadOnlyList<AiBasketRef>> GetBasketRefsAsync(CancellationToken cancellationToken) => Task.FromResult(baskets);

        public Task<AiToolData> GetBasketContextAsync(Guid basketId, DateOnly date, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> ListLabRunsAsync(string? strategy, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetPlanAsync(string? date, string? accountLabel, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetCellAsync(string? key, string? strategy, string? timeframe, string? side, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetPortfolioAsync(string groupBy, bool includeClosed, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetBasketAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetInstrumentAsync(string symbol, int bars, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> SearchNotesAsync(string query, string? from, string? to, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetLearningAsync(string kind, string? status, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetSystemStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private BasketReportReader Reader(string? token = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:Api:BaseUrl"] = "http://xd-api-eval:8080", ["Mcp:Api:Token"] = token ?? factory.ServiceToken }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddAiBasketApi(configuration);
        services.AddHttpClient<BasketApiClient>().ConfigurePrimaryHttpMessageHandler(() => _eval.Server.CreateHandler());
        return new BasketReportReader(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), new Data(_refs), TimeProvider.System);
    }

    private static Dictionary<string, string> Q(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    [Fact]
    public async Task BQ02_the_hold_of_EvalGamma_is_8_3333_percent_against_nifty_500_and_the_252_day_figures_are_insufficient()
    {
        var answer = await Reader().GetAsync("basket_profile", Q(("basket", "EvalGamma")), Ct);

        answer.ErrorCode.ShouldBeNull(answer.ErrorMessage);
        var data = answer.Data!;
        data["hold"]!["state"]!.GetValue<string>().ShouldBe("value");
        data["hold"]!["number"]!.GetValue<double>().ShouldBe(25.0 / 3, 1e-9);
        (data["index"]!["symbol"]!.GetValue<string>(), data["index"]!["isDefault"]!.GetValue<bool>()).ShouldBe(("NSE:NIFTY_500", true));
        data["members"]!["counted"]!.GetValue<int>().ShouldBe(3);
        data["risk"]!["beta252"]!["state"]!.GetValue<string>().ShouldBe("insufficient");
        data["risk"]!["vol20"]!["state"]!.GetValue<string>().ShouldBe("value");
        data["risk"]!["mdd"]!["state"]!.GetValue<string>().ShouldBe("value");
    }

    [Fact]
    public async Task BQ06_the_four_member_basket_EvalQuad_has_its_correlation_insufficient_with_n_4_of_at_least_5()
    {
        var answer = await Reader().GetAsync("basket_profile", Q(("basket", "EvalQuad")), Ct);

        answer.ErrorCode.ShouldBeNull(answer.ErrorMessage);
        var rho = answer.Data!["correlation"]!["rhoBar"]!;
        rho["state"]!.GetValue<string>().ShouldBe("insufficient");
        rho["reason"]!.GetValue<string>().ShouldBe("n = 4, at least 5 needed");
        rho.AsObject().ContainsKey("number").ShouldBeFalse();
    }

    [Fact]
    public async Task BQ04_EvalGamma_and_EvalQuad_overlap_by_a_jaccard_of_0_4_and_BQ01_EvalGamma_and_EvalTwin_are_not_separated_with_no_rank()
    {
        var reader = Reader();

        var overlap = await reader.GetAsync("basket_ranking", Q(("baskets", "EvalGamma,EvalQuad")), Ct);
        var twins = await reader.GetAsync("basket_ranking", Q(("baskets", "EvalGamma,EvalTwin")), Ct);

        overlap.ErrorCode.ShouldBeNull(overlap.ErrorMessage);
        overlap.Data!["overlap"]![0]!["jaccard"]!["number"]!.GetValue<double>().ShouldBe(0.4, 1e-12);
        twins.ErrorCode.ShouldBeNull(twins.ErrorMessage);
        twins.Data!["separated"]!.GetValue<bool>().ShouldBeFalse();
        twins.Data.AsObject().ContainsKey("rankWindow").ShouldBeFalse();
        twins.Data["rows"]!.AsArray().ShouldAllBe(row => !row!.AsObject().ContainsKey("rsRank"));
        twins.Data["rows"]![0]!["rsReturn63"]!["state"]!.GetValue<string>().ShouldBe("value");
        twins.Data["rows"]![0]!["rsReturn63"]!["number"]!.GetValue<double>().ShouldBe(twins.Data["rows"]![1]!["rsReturn63"]!["number"]!.GetValue<double>(), 1e-12);
        twins.Notes.ShouldContain(n => n.Contains("not separated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_the_eval_api_the_basket_questions_get_tool_unavailable_which_the_grader_marks_not_evaluable()
    {
        var down = await Reader(token: "not-the-service-token").GetAsync("basket_profile", Q(("basket", "EvalGamma")), Ct);

        down.ErrorCode.ShouldBe("tool.unavailable");
        var q = EvalCaseReader.ReadLines(File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindFixture())!, "..", "sets", "basket-questions.jsonl"))).Value.Single(c => c.Id == "BQ-02");
        var grade = AnswerGrader.Grade(q, "EvalGamma held +8.33%.", ["{\"error\":{\"code\":\"" + down.ErrorCode + "\"}}"]);
        grade.NotEvaluable.ShouldBeTrue();
        grade.Passed.ShouldBeFalse();
    }

    [Fact]
    public void The_reports_only_host_over_the_fixture_maps_only_get_report_routes_with_no_docs_contract_or_hub_and_never_runs_a_hosted_service_of_the_app()
    {
        var endpoints = _eval.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().ToList();

        var api = endpoints.Where(e => e.RoutePattern.RawText!.StartsWith("/api/", StringComparison.Ordinal)).ToList();
        api.ShouldNotBeEmpty();
        api.ShouldAllBe(e => e.RoutePattern.RawText!.StartsWith("/api/reports/", StringComparison.Ordinal));
        api.SelectMany(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods).Distinct().ShouldBe(["GET"]);
        endpoints.ShouldNotContain(e => e.RoutePattern.RawText!.Contains("openapi", StringComparison.Ordinal) || e.RoutePattern.RawText.Contains("/hubs/", StringComparison.Ordinal) || e.RoutePattern.RawText.Contains("docs", StringComparison.Ordinal));
    }

    [Fact]
    public void A_reports_only_host_refuses_to_start_over_any_database_but_xdrishti_eval()
    {
        using var wrong = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Api:ReportsOnly", "true");
            b.UseSetting("BrokerVault:Key", string.Empty);
            b.UseSetting("ExecHandover:Key", string.Empty);
        });

        Should.Throw<InvalidOperationException>(() => wrong.CreateClient()).Message.ShouldContain("only over the database 'xdrishti_eval'");
    }
}
