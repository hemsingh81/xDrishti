using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>
/// Spec P9-09 Contract and AC 10, 14 on the real API and database: the eval results are read only (no route runs, grades or accepts), every read needs a session, the status says "no accepted eval run" until the owner accepts one and then compares the fingerprint of the files and the prompts
/// without any model; a stored run reads back with its cases; the assistant status takes its evaluation from it. No model is involved.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class AssistantEvalApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Runs the text as it is (braces of JSON included: the raw-SQL helper of EF reads them as placeholders).</summary>
    private Task<int> ExecuteAsync(string sql) => factory.WithDbAsync(async db =>
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync(Ct);
    });

    private Task<int> CleanAsync() => ExecuteAsync("DELETE FROM ai.eval_baselines; DELETE FROM ai.eval_cases; DELETE FROM ai.eval_runs;");

#pragma warning disable EF1002 // the values are constants and generated ids of these tests
    private async Task<Guid> StoreRunAsync(string verdict = "PASS", bool gating = true)
    {
        var id = Guid.NewGuid();
        var summary = """{"verdict":"PASS","nonGating":false,"sets":[{"set":"data","outcome":{"set":"data","state":"pass","reasons":[]},"rates":[{"name":"pass","k":28,"n":30,"low":0.79,"high":0.98}],"cases":30,"repeats":3,"stability":0.9,"intentMean":null,"intentLowest":null}],"smallSampleLine":"30 cases cannot separate 27 from 24 correct"}""";
        await ExecuteAsync($$"""
            INSERT INTO ai.eval_runs (id, started_at, finished_at, target, profile, sets, repeats, gating, model_id, model_sha256, runtime_digest, params_json, prompt_versions_json, sets_hash, thresholds_hash, fixture_hash, fingerprint, status, verdict, summary_json, actor)
            VALUES ('{{id}}', now() - interval '1 hour', now(), 'builtin', 'main', ARRAY['data'], 3, {{(gating ? "true" : "false")}}, 'm.gguf', repeat('a', 64), 'b1', '{"temperature":0}', '[]', repeat('1', 64), repeat('2', 64), repeat('3', 64), repeat('4', 64), 'completed', '{{verdict}}', '{{summary}}', 'cli:xd');
            INSERT INTO ai.eval_cases (run_id, "set", case_id, repeat, passed, grounded, detail_json, answer, tool_calls, latency_ms) VALUES ('{{id}}', 'data', 'DQ-01', 1, true, true, '{"codes":[]}', 'ORB 15m has 24 trades.', 2, 1200), ('{{id}}', 'data', 'DQ-02', 1, false, true, '{"codes":["wrong_number"]}', 'The win rate is 40%.', 1, 900);
            """);
        return id;
    }
#pragma warning restore EF1002

    [Theory]
    [InlineData("/api/assistant/evals")]
    [InlineData("/api/assistant/evals/status")]
    [InlineData("/api/assistant/evals/11111111-1111-1111-1111-111111111111")]
    public async Task Every_read_needs_a_session(string path)
    {
        using var response = await factory.CreateAnonymousClient().GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("POST", "/api/assistant/evals")]
    [InlineData("POST", "/api/assistant/evals/run")]
    [InlineData("POST", "/api/assistant/evals/11111111-1111-1111-1111-111111111111/accept")]
    [InlineData("PUT", "/api/assistant/evals/11111111-1111-1111-1111-111111111111")]
    [InlineData("DELETE", "/api/assistant/evals/11111111-1111-1111-1111-111111111111")]
    public async Task There_is_no_route_that_runs_grades_accepts_changes_or_deletes_an_eval(string method, string path)
    {
        var signedIn = await factory.SignInAsync(Ct);
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));

        using var response = await signedIn.Client.SendAsync(request, Ct);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task With_no_run_the_list_is_empty_and_the_status_says_no_accepted_eval_run()
    {
        await CleanAsync();
        var signedIn = await factory.SignInAsync(Ct);

        using var list = await signedIn.Client.GetAsync(new Uri("/api/assistant/evals", UriKind.Relative), Ct);
        using var status = await signedIn.Client.GetAsync(new Uri("/api/assistant/evals/status", UriKind.Relative), Ct);
        using var assistant = await signedIn.Client.GetAsync(new Uri("/api/assistant/status", UriKind.Relative), Ct);

        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
        var json = await status.Content.ReadFromJsonAsync<JsonElement>(Ct);
        (json.GetProperty("detail").GetString(), json.GetProperty("stale").GetBoolean(), json.GetProperty("state").GetString()).ShouldBe(("no accepted eval run", false, "not_yet_evaluated"));
        json.GetProperty("currentFingerprint").GetString()!.Length.ShouldBe(64);
        (await assistant.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("evaluation").GetProperty("state").GetString().ShouldBe("not_yet_evaluated");
    }

    [Fact]
    public async Task A_stored_run_reads_back_with_its_cases_and_an_accepted_one_whose_fingerprint_differs_is_stale_with_the_reasons()
    {
        await CleanAsync();
        var id = await StoreRunAsync();
        await ExecuteAsync($"INSERT INTO ai.eval_baselines (run_id, accepted_by) VALUES ('{id}', 'cli:xd')");
        var signedIn = await factory.SignInAsync(Ct);

        using var list = await signedIn.Client.GetAsync(new Uri("/api/assistant/evals?limit=5", UriKind.Relative), Ct);
        using var one = await signedIn.Client.GetAsync(new Uri($"/api/assistant/evals/{id}", UriKind.Relative), Ct);
        using var status = await signedIn.Client.GetAsync(new Uri("/api/assistant/evals/status", UriKind.Relative), Ct);

        var runs = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
        runs.GetArrayLength().ShouldBe(1);
        (runs[0].GetProperty("isBaseline").GetBoolean(), runs[0].GetProperty("verdict").GetString(), runs[0].GetProperty("summary").GetProperty("sets")[0].GetProperty("outcome").GetProperty("state").GetString()).ShouldBe((true, "PASS", "Pass"));
        var detail = await one.Content.ReadFromJsonAsync<JsonElement>(Ct);
        detail.GetProperty("cases").GetArrayLength().ShouldBe(2);
        detail.GetProperty("cases")[1].GetProperty("answer").GetString().ShouldBe("The win rate is 40%.");
        var state = await status.Content.ReadFromJsonAsync<JsonElement>(Ct);
        state.GetProperty("stale").GetBoolean().ShouldBeTrue();
        state.GetProperty("baselineRunId").GetGuid().ShouldBe(id);
        state.GetProperty("reasons").EnumerateArray().Select(e => e.GetString()).ShouldContain("sets");
        state.GetProperty("reasons").EnumerateArray().Select(e => e.GetString()).ShouldContain("prompt chat");
        await CleanAsync();
    }

    [Fact]
    public async Task A_failing_newest_gating_run_makes_the_assistant_evaluation_failed_and_a_missing_run_is_not_found()
    {
        await CleanAsync();
        await StoreRunAsync("FAIL");
        var signedIn = await factory.SignInAsync(Ct);

        using var assistant = await signedIn.Client.GetAsync(new Uri("/api/assistant/status", UriKind.Relative), Ct);
        using var missing = await signedIn.Client.GetAsync(new Uri($"/api/assistant/evals/{Guid.NewGuid()}", UriKind.Relative), Ct);
        using var tooMany = await signedIn.Client.GetAsync(new Uri("/api/assistant/evals?limit=51", UriKind.Relative), Ct);
        using var none = await signedIn.Client.GetAsync(new Uri("/api/assistant/evals?limit=0", UriKind.Relative), Ct);

        (await assistant.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("evaluation").GetProperty("state").GetString().ShouldBe("failed");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("assistant.eval_not_found");
        (tooMany.StatusCode, none.StatusCode).ShouldBe((HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity));
        await CleanAsync();
    }
}
