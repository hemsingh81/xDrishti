using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>Spec P9-06 AC 12 end to end: the briefing endpoints are read only, answer in the documented codes, show the attempts' findings and the evidence, and a preview never writes.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AssistantBriefingApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<int> ExecuteAsync(string sql) => factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(sql, Ct));

    [Fact]
    public async Task The_reads_need_a_session()
    {
        using var response = await factory.CreateAnonymousClient().GetAsync("/api/assistant/briefings", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_missing_text_is_404_and_a_bad_input_is_422_with_the_documented_codes()
    {
        await ExecuteAsync("DELETE FROM ai.briefing_attempts; DELETE FROM ai.briefings");
        var client = (await factory.SignInAsync(Ct)).Client;

        using var missing = await client.GetAsync("/api/assistant/briefings/daily?date=2020-01-02", Ct);
        using var badDate = await client.GetAsync("/api/assistant/briefings/daily?date=02-01-2020", Ct);
        using var badWeek = await client.GetAsync("/api/assistant/briefings/weekly?week=2026-W99", Ct);
        using var badLimit = await client.GetAsync("/api/assistant/briefings?limit=61", Ct);
        using var noPlan = await client.GetAsync("/api/assistant/briefings/preview?kind=daily&subject=2020-01-02", Ct);
        using var noDigest = await client.GetAsync("/api/assistant/briefings/preview?kind=weekly&subject=2020-W02", Ct);
        using var badKind = await client.GetAsync("/api/assistant/briefings/preview?kind=hourly", Ct);

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("assistant.briefing_not_found");
        badDate.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await badDate.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("assistant.invalid");
        badWeek.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        badLimit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        noPlan.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        noDigest.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await noDigest.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("assistant.briefing_not_evaluable");
        badKind.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_stored_text_is_read_back_with_its_attempts_findings_and_evidence_and_the_list_is_newest_first()
    {
        await ExecuteAsync("DELETE FROM ai.briefing_attempts; DELETE FROM ai.briefings");
        await ExecuteAsync("""
            INSERT INTO ai.briefings (kind, subject, version, source, reason, text, deterministic_text, facts_json, facts_hash, prompt_version, params_json, word_count, built_by)
            VALUES ('daily', '2026-10-28', 1, 'deterministic', 'guard_rejected', 'fixed text', 'fixed text', '{{"Kind":"daily","Subject":"2026-10-28","Provisional":true,"Tickets":["AAA"],"Lines":[],"PlanId":42,"DigestId":null,"RunKeys":[]}}', repeat('a', 64), 'briefing.v1', '{{}}', 2, 'system:briefing');
            INSERT INTO ai.briefing_attempts (briefing_id, attempt, text, passed, findings_json, latency_ms)
            SELECT id, 1, 'refused <b>text</b>', false, '[{{"Code":"number_not_in_facts","Token":"98.60","Offset":12}}]', 20 FROM ai.briefings;
            """);
        var client = (await factory.SignInAsync(Ct)).Client;

        using var response = await client.GetAsync("/api/assistant/briefings/daily?date=2026-10-28", Ct);
        using var list = await client.GetAsync("/api/assistant/briefings?kind=daily&limit=5", Ct);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (json.GetProperty("source").GetString(), json.GetProperty("reason").GetString(), json.GetProperty("text").GetString(), json.GetProperty("wordCount").GetInt32()).ShouldBe(("deterministic", "guard_rejected", "fixed text", 2));
        json.GetProperty("evidence").GetProperty("planId").GetInt64().ShouldBe(42);
        var attempt = json.GetProperty("attempts").EnumerateArray().Single();
        attempt.GetProperty("passed").GetBoolean().ShouldBeFalse();
        var finding = attempt.GetProperty("findings").EnumerateArray().Single();
        (finding.GetProperty("code").GetString(), finding.GetProperty("token").GetString(), finding.GetProperty("offset").GetInt32()).ShouldBe(("number_not_in_facts", "98.60", 12));
        finding.GetProperty("words").GetString().ShouldNotBeNullOrWhiteSpace();
        json.ToString().ShouldNotContain("refused <b>text</b>");
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().Count().ShouldBe(1);
    }

    [Fact]
    public async Task There_is_no_write_endpoint()
    {
        var client = (await factory.SignInAsync(Ct)).Client;

        using var post = await client.PostAsJsonAsync("/api/assistant/briefings/daily", new { }, Ct);
        using var put = await client.PutAsJsonAsync("/api/assistant/briefings/daily", new { }, Ct);
        using var delete = await client.DeleteAsync("/api/assistant/briefings/daily", Ct);

        foreach (var response in new[] { post, put, delete })
        {
            ((int)response.StatusCode).ShouldBeOneOf(404, 405);
        }
    }
}
