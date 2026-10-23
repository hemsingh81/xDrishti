using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Spec P6-18 on a real database: the overview reads the stores and answers on an empty one, and the phase check does not call what is not stored.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LearningOverviewApiTests(ApiFactory factory) : DataTestBase(factory)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.lessons");
        await ExecuteAsync(connection, "DELETE FROM research.hypotheses");
        await ExecuteAsync(connection, "DELETE FROM research.experiments");
    }

    [Fact]
    public async Task The_overview_answers_with_the_sections_the_limits_and_zero_experiments()
    {
        using var response = await Client.GetAsync(new Uri("/api/learning/overview", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        json.GetProperty("experiments").GetProperty("counted").GetInt32().ShouldBe(0);
        json.GetProperty("hypotheses").GetProperty("counted").GetInt32().ShouldBe(0);
        json.GetProperty("lessons").GetProperty("counted").GetInt32().ShouldBe(0);
        json.GetProperty("models").GetProperty("split").ValueKind.ShouldBe(JsonValueKind.Array);
        json.GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task With_no_experiment_stored_the_phase_check_does_not_call_the_timing()
    {
        using var scope = Factory.Services.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<EvaluatePhaseCheckQuery, Result<PhaseCheckView>>>().HandleAsync(new EvaluatePhaseCheckQuery(null), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Report.Criteria[2].Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
        result.Value.Text.ShouldContain("limit: about 2.1 years");
    }
}
