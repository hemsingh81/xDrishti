using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec P8-13 on a real database: the smart-rule and loss-tag reads answer on empty data with named non-values and refuse bad input.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LearningEvidenceApiTests(ApiFactory factory) : DataTestBase(factory)
{
    [Fact]
    public async Task Smart_rules_without_a_plan_has_five_empty_rules_and_not_evaluable_outcomes()
    {
        using var response = await Client.GetAsync(new Uri("/api/learning/smart-rules?plans=30", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        view.GetProperty("state").GetString().ShouldBe("empty");
        view.GetProperty("rules").GetArrayLength().ShouldBe(5);
        foreach (var rule in view.GetProperty("rules").EnumerateArray())
        {
            rule.GetProperty("unavailableOn").ValueKind.ShouldBe(JsonValueKind.Null);
            rule.GetProperty("outcome").GetProperty("state").GetString().ShouldBe("not_evaluable");
            rule.GetProperty("outcome").GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    [Theory]
    [InlineData("/api/learning/smart-rules?plans=0")]
    [InlineData("/api/learning/smart-rules?plans=61")]
    [InlineData("/api/learning/loss-tags?run=nokey")]
    [InlineData("/api/learning/loss-tags?run=zz:swing:X")]
    public async Task Bad_input_is_refused_with_422_learning_invalid(string url)
    {
        using var response = await Client.GetAsync(new Uri(url, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("learning.invalid");
    }

    [Fact]
    public async Task Loss_tags_answer_and_list_what_is_not_built_and_an_unknown_run_is_404()
    {
        var run = new string('a', 64);
        using var unknown = await Client.GetAsync(new Uri($"/api/learning/loss-tags?run={run}:swing:X", UriKind.Relative), Ct);
        using var response = await Client.GetAsync(new Uri("/api/learning/loss-tags", UriKind.Relative), Ct);

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        view.GetProperty("state").GetString().ShouldBeOneOf("empty", "ok");
        view.GetProperty("notBuilt").GetArrayLength().ShouldBeGreaterThan(0);
    }
}
