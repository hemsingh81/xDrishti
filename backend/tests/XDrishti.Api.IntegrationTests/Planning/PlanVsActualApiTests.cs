using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec P8-04 on a real database: the two new read-only GETs answer on empty data with honest states, refuse a bad range with 422, and give the same bytes twice.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class PlanVsActualApiTests(ApiFactory factory) : DataTestBase(factory)
{
    [Fact]
    public async Task Plan_vs_actual_without_graded_tickets_has_an_empty_chain_and_the_basis_text()
    {
        using var response = await Client.GetAsync(new Uri("/api/reports/plan-vs-actual", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        view.GetProperty("chain").GetArrayLength().ShouldBe(5);
        view.GetProperty("chain")[0].GetProperty("metric").GetProperty("state").GetString().ShouldBe("empty");
        view.GetProperty("chain")[0].GetProperty("metric").GetProperty("number").ValueKind.ShouldBe(JsonValueKind.Null);
        view.GetProperty("evidence").GetProperty("basis").GetString()!.ShouldContain("realised fills, not simulated");
        view.GetProperty("notDecomposed").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Plan_vs_actual_is_byte_stable_and_refuses_bad_ranges()
    {
        var first = await Client.GetStringAsync(new Uri("/api/reports/plan-vs-actual", UriKind.Relative), Ct);
        var second = await Client.GetStringAsync(new Uri("/api/reports/plan-vs-actual", UriKind.Relative), Ct);
        using var reversed = await Client.GetAsync(new Uri("/api/reports/plan-vs-actual?from=2026-03-02&to=2026-03-01", UriKind.Relative), Ct);
        using var wide = await Client.GetAsync(new Uri("/api/reports/plan-vs-actual?from=2025-01-01&to=2026-03-01", UriKind.Relative), Ct);

        second.ShouldBe(first);
        reversed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        wide.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await reversed.Content.ReadAsStringAsync(Ct)).ShouldContain("reports.invalid");
    }

    [Fact]
    public async Task Reading_either_endpoint_writes_nothing()
    {
        string[] tables = ["research.experiments", "research.model_versions", "research.model_designations", "trading.plans", "trading.plan_runs", "trading.grades", "trading.journal_entries", "workflow.approvals"];
        var before = new List<long>();
        foreach (var table in tables)
        {
            before.Add(await ScalarAsync<long>("SELECT count(*) FROM " + table));
        }

        (await Client.GetAsync(new Uri("/api/learning/phase-check", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        (await Client.GetAsync(new Uri("/api/reports/plan-vs-actual", UriKind.Relative), Ct)).EnsureSuccessStatusCode();

        for (var i = 0; i < tables.Length; i++)
        {
            (await ScalarAsync<long>("SELECT count(*) FROM " + tables[i])).ShouldBe(before[i], tables[i]);
        }
    }

    [Fact]
    public async Task The_learning_phase_check_is_not_evaluable_without_a_model_and_prints_the_limits()
    {
        using var response = await Client.GetAsync(new Uri("/api/learning/phase-check", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        view.GetProperty("criteria").GetArrayLength().ShouldBe(3);
        view.GetProperty("criteria").EnumerateArray().ShouldAllBe(c => c.GetProperty("verdict").GetString() == "NOT EVALUABLE");
        view.GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
    }
}
