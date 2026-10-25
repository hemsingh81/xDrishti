using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Spec P8-13 criterion 8 on a real database: the stored entry-window study and meta-policy evaluation are read newest first or by id, and nothing stored is 404 with the kind's code.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReportKindsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string WindowsJson = """
        {"schemaVersion":1,"id":null,"outcome":"NoWindowProposed","knobs":{"bucketMinutes":30,"minWindowTrades":30,"minLift":0.03,"priorStrength":20,"minFoldTrades":5,"minEvaluableFolds":2,"holdOutMonths":6},
        "rows":120,"from":"2025-01-01","to":"2026-06-30","usedFolds":3,"outsideSession":0,
        "cells":[{"cellKey":"ORB|intraday|x","status":"NotEnoughEvidence","cellTrades":12,"cellWinRate":null,"windowFrom":null,"windowTo":null,"windowTrades":0,"keptShare":0,"shrunkRate":null,"lift":null,"meanRInside":null,"meanROutside":null,"removesMajority":false,"foldsEvaluable":0,"foldsHeld":0,"foldsRequired":2,"pooledLift":null,"reasons":["12 trades are fewer than 30."]}],
        "limits":["l"],"basis":"gross","sources":[{"runKey":"AAAA","style":"intraday","exit":"x"}],"text":"t limit: l","builtBy":null,"createdAt":null}
        """;

    private const string PolicyJson = """
        {"schemaVersion":1,"id":null,"outcome":"NoPolicy","reasons":["There is no champion model."],"knobs":{"split":"None","minKeptShare":0.5,"minLiftR":0.02,"minSkillR":0.1,"significanceZ":1.645,"trials":1,"holdOutMonths":6},
        "rows":0,"from":null,"to":null,"usedFolds":0,"folds":[],"pooled":{"candidates":0,"kept":0,"skipped":0,"meanKept":null,"meanSkipped":null,"baselinePerCandidate":0,"policyPerCandidate":0,"lift":0,"randomLift":0},
        "recent":{"candidates":0,"kept":0,"skipped":0,"meanKept":null,"meanSkipped":null,"baselinePerCandidate":0,"policyPerCandidate":0,"lift":0,"randomLift":0},"policy":[],
        "limits":["l"],"basis":"gross","sources":[{"runKey":"AAAA","style":"intraday","exit":"x"}],"text":"t limit: l","builtBy":null,"createdAt":null}
        """;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.learning_reports");
    }

    private async Task<long> InsertAsync(string kind, string outcome, string json, string hash)
    {
        using var scope = Factory.Services.CreateScope();
        var row = new NewLearningReport(kind, hash, outcome, new DateOnly(2026, 6, 30), "gross", "[{\"runKey\":\"AAAA\"}]", "{}", json, "t limit: l", "cli:xd", new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero));
        return (await scope.ServiceProvider.GetRequiredService<ILearningReportStore>().InsertAsync(row, Ct)).Stored.Id;
    }

    [Fact]
    public async Task With_nothing_stored_both_endpoints_are_404_with_their_own_code()
    {
        using var windows = await Client.GetAsync(new Uri("/api/learning/entry-windows", UriKind.Relative), Ct);
        using var policy = await Client.GetAsync(new Uri("/api/learning/meta-policy?id=999999", UriKind.Relative), Ct);

        windows.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await windows.Content.ReadAsStringAsync(Ct)).ShouldContain("learning.entry_windows_not_found");
        policy.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await policy.Content.ReadAsStringAsync(Ct)).ShouldContain("learning.meta_policy_not_found");
    }

    [Fact]
    public async Task A_stored_study_is_read_with_its_id_and_a_row_of_the_other_kind_is_not_returned()
    {
        var windowsId = await InsertAsync("entry_windows", "NoWindowProposed", WindowsJson, new string('1', 64));
        var policyId = await InsertAsync("meta_policy", "NoPolicy", PolicyJson, new string('2', 64));

        using var windows = await Client.GetAsync(new Uri("/api/learning/entry-windows", UriKind.Relative), Ct);
        using var policy = await Client.GetAsync(new Uri($"/api/learning/meta-policy?id={policyId}", UriKind.Relative), Ct);
        using var wrong = await Client.GetAsync(new Uri($"/api/learning/entry-windows?id={policyId}", UriKind.Relative), Ct);

        var w = await windows.Content.ReadFromJsonAsync<JsonElement>(Ct);
        w.GetProperty("id").GetInt64().ShouldBe(windowsId);
        w.GetProperty("cells")[0].GetProperty("status").GetString().ShouldBe("NotEnoughEvidence");
        w.GetProperty("builtBy").GetString().ShouldBe("cli:xd");
        var p = await policy.Content.ReadFromJsonAsync<JsonElement>(Ct);
        p.GetProperty("outcome").GetString().ShouldBe("NoPolicy");
        p.GetProperty("from").ValueKind.ShouldBe(JsonValueKind.Null);
        wrong.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
