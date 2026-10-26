using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Spec P8-17 end to end on a real TimescaleDB: the phase check answers 200 with the ten criteria for a session and 401 without one, never prints MET for what only a host file can show, reads the real route table (criterion 5 MET: every route under the guarded prefixes
/// is a GET), and the answer path named by <c>backend/contracts/what-works-path.json</c> keeps p95 under a second on the seeded fixture (<c>ReportsPathBudgetTests</c> of the spec, kept here beside the fixture it uses).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReportsPhaseCheckApiTests(ApiFactory factory) : DataTestBase(factory)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await ReportFixture.SeedAsync(Factory, Ct);
    }

    private async Task<JsonElement> CheckAsync()
    {
        using var response = await Client.GetAsync(new Uri("/api/reports/phase-check", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static JsonElement Criterion(JsonElement body, int number) =>
        body.GetProperty("report").GetProperty("criteria").EnumerateArray().Single(c => c.GetProperty("number").GetInt32() == number);

    [Fact]
    public async Task The_check_returns_the_ten_criteria_with_the_limits_and_an_answer_and_never_meets_the_exit_alone()
    {
        var body = await CheckAsync();

        var report = body.GetProperty("report");
        report.GetProperty("criteria").GetArrayLength().ShouldBe(10);
        report.GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
        report.GetProperty("runs").GetInt32().ShouldBeGreaterThan(0);
        report.GetProperty("answer").GetProperty("kind").GetString().ShouldBeOneOf("ranked", "nothing_qualifies");
        report.GetProperty("headline").GetString()!.ShouldStartWith("P8 exit: NOT EVALUABLE");
        report.GetProperty("builtReports").GetArrayLength().ShouldBe(9);
        body.GetProperty("text").GetString()!.ShouldContain("limit:");
        // what only a host file can show is never read as a pass here
        foreach (var n in new[] { 1, 7, 8 })
        {
            Criterion(body, n).GetProperty("verdict").GetString().ShouldBe("NOT EVALUABLE");
        }
    }

    [Fact]
    public async Task The_evidence_state_and_double_counting_criteria_are_met_on_the_seeded_fixture_and_the_routes_are_read_only()
    {
        var body = await CheckAsync();

        foreach (var n in new[] { 2, 3, 4, 5, 9 })
        {
            Criterion(body, n).GetProperty("verdict").GetString().ShouldBe("MET", Criterion(body, n).GetProperty("detail").GetString());
        }
    }

    [Fact]
    public async Task Without_a_session_the_check_is_refused()
    {
        using var anonymous = Factory.CreateClient();

        using var response = await anonymous.GetAsync(new Uri("/api/reports/phase-check", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_answer_path_of_the_manifest_is_under_a_second_at_the_95th_percentile_after_a_warm_up()
    {
        var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "backend", "contracts", "what-works-path.json"), Ct)).RootElement;
        var basket = ReportFixture.Basket1.ToString("D");
        var paths = new Dictionary<string, string> { ["ListReportRuns"] = "/api/reports/runs", ["GetReportWhatWorks"] = "/api/reports/what-works?basket=" + basket };
        foreach (var step in manifest.GetProperty("answerPath").EnumerateArray())
        {
            var operation = step.GetProperty("operation").GetString()!;
            paths.ShouldContainKey(operation);
            var path = paths[operation];
            using (var cold = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct))
            {
                cold.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            using (await Client.GetAsync(new Uri(path, UriKind.Relative), Ct))
            {
            }

            var samples = new List<double>();
            for (var i = 0; i < 20; i++)
            {
                var started = Stopwatch.GetTimestamp();
                using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
                samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            samples.Sort();
            var p95 = samples[18];
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{operation}: p95 {p95:0} ms over 20 calls"));
            p95.ShouldBeLessThan(1000, operation);
        }
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "backend", "contracts", "what-works-path.json")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("the repository root with backend/contracts was not found");
    }
}
