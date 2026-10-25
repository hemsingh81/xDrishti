using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Spec P8-12 on a real database: the learning reports are insert-or-skip by kind and hash, the database refuses a malformed row, and the read endpoints and the Journey answer from what is stored.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class CounterfactualApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string ReportJson = """
        {"schemaVersion":1,"id":null,"outcome":"NoDifference","reasons":["r"],"cut":"2025-12-31","to":"2026-06-30","frozenAgeMonths":6,"frozenTrainRows":100,
        "knobs":{"frozenMonths":12,"retrainMonths":3,"minWindowRows":10,"minJudgedWindows":3,"minGainLogLoss":0.005,"significanceZ":1.645,"trials":1,"policyTolerance":0.01,"holdOutMonths":6},
        "windows":[],"judgedWindows":0,"judged":null,"all":null,"limits":["l"],"basis":"gross","sources":[{"runKey":"AAAA","style":"intraday","exit":"x"}],"text":"t limit: l","builtBy":null,"createdAt":null,"sameRunsStored":0}
        """;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.learning_reports");
    }

    private static NewLearningReport Report(string hash, string text = "t limit: l", string kind = "counterfactual", string by = "cli:xd") =>
        new(kind, hash, "NoDifference", new DateOnly(2026, 6, 30), "gross", "[{\"runKey\":\"AAAA\"}]", "{}", ReportJson, text, by, new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));

    private async Task<(StoredLearningReport Stored, bool Created)> InsertAsync(NewLearningReport report)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ILearningReportStore>().InsertAsync(report, Ct);
    }

    [Fact]
    public async Task The_same_kind_and_hash_is_stored_once_and_another_hash_is_another_row()
    {
        var first = await InsertAsync(Report(new string('a', 64)));
        var again = await InsertAsync(Report(new string('a', 64)));
        var other = await InsertAsync(Report(new string('b', 64)));

        first.Created.ShouldBeTrue();
        again.Created.ShouldBeFalse();
        again.Stored.Id.ShouldBe(first.Stored.Id);
        other.Created.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Bad Kind", "t limit: l", "x")]
    [InlineData("counterfactual", "no limits here", "x")]
    [InlineData("counterfactual", "t limit: l", "")]
    public async Task The_database_refuses_a_bad_kind_a_text_without_limits_and_an_empty_builder(string kind, string text, string by)
    {
        var failure = await Should.ThrowAsync<PostgresException>(async () => await InsertAsync(Report(new string('c', 64), text, kind, by)));

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_database_refuses_a_bad_hash()
    {
        var failure = await Should.ThrowAsync<PostgresException>(async () => await InsertAsync(Report("not-a-hash")));

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task With_nothing_stored_the_endpoints_say_so_and_a_bad_limit_is_422()
    {
        using var newest = await Client.GetAsync(new Uri("/api/learning/counterfactual", UriKind.Relative), Ct);
        using var unknown = await Client.GetAsync(new Uri("/api/learning/counterfactual?id=999999", UriKind.Relative), Ct);
        using var list = await Client.GetAsync(new Uri("/api/learning/counterfactuals", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri("/api/learning/counterfactuals?limit=51", UriKind.Relative), Ct);
        using var journey = await Client.GetAsync(new Uri("/api/learning/journey", UriKind.Relative), Ct);

        newest.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await newest.Content.ReadAsStringAsync(Ct)).ShouldContain("learning.counterfactual_not_found");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var j = await journey.Content.ReadFromJsonAsync<JsonElement>(Ct);
        j.GetProperty("counterfactual").ValueKind.ShouldBe(JsonValueKind.Null);
        j.GetProperty("live").GetProperty("state").GetString().ShouldBe("not_evaluable");
        j.GetProperty("planPrice").GetProperty("basis").GetString().ShouldBe("simulated on plan prices");
    }

    [Fact]
    public async Task A_stored_counterfactual_is_read_newest_first_by_id_and_in_the_journey()
    {
        var older = await InsertAsync(Report(new string('d', 64)));
        var newer = await InsertAsync(Report(new string('e', 64)) with { CreatedAt = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero) });

        using var newest = await Client.GetAsync(new Uri("/api/learning/counterfactual", UriKind.Relative), Ct);
        using var one = await Client.GetAsync(new Uri($"/api/learning/counterfactual?id={older.Stored.Id}", UriKind.Relative), Ct);
        using var list = await Client.GetAsync(new Uri("/api/learning/counterfactuals?limit=5", UriKind.Relative), Ct);
        using var journey = await Client.GetAsync(new Uri("/api/learning/journey", UriKind.Relative), Ct);

        var n = await newest.Content.ReadFromJsonAsync<JsonElement>(Ct);
        n.GetProperty("id").GetInt64().ShouldBe(newer.Stored.Id);
        n.GetProperty("outcome").GetString().ShouldBe("NoDifference");
        n.GetProperty("sameRunsStored").GetInt32().ShouldBe(2);
        (await one.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64().ShouldBe(older.Stored.Id);
        var l = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
        l.GetArrayLength().ShouldBe(2);
        l[0].GetProperty("id").GetInt64().ShouldBe(newer.Stored.Id);
        var j = await journey.Content.ReadFromJsonAsync<JsonElement>(Ct);
        j.GetProperty("counterfactual").GetProperty("id").GetInt64().ShouldBe(newer.Stored.Id);
        j.GetProperty("evidence").GetProperty("basis").GetString().ShouldBe("gross");
        j.GetProperty("live").GetProperty("n").GetInt32().ShouldBe(0);
    }
}
