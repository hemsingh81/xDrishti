using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Spec P6-07 on a real database: a model is registered once with its first designation, roles are appended (never updated), only an eligible model becomes champion and a new champion retires the old one, and the two endpoints read the registry.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ModelRegistryApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string SetId = new('a', 64);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, """
            INSERT INTO research.training_sets (set_id, label_set_id, rule_version, feature_version, basis, sources, required_features, half_life_days, prior_strength, min_trades, credible_level, from_date, to_date, labelled, row_count,
                positives, negatives, unresolved, no_target, dropped_no_features, dropped_unknown, created_at, created_by)
            VALUES (@id, @id, 'tb-1', 'tf-1', 'Gross', 's', 'cell_n', 180, 20, 30, 0.9, '2026-01-01', '2026-06-01', 10, 10, 6, 4, 0, 0, 0, 0, now(), 'test')
            """, ("id", SetId));
    }

    private async Task CleanAsync()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.model_designations");
        await ExecuteAsync(connection, "DELETE FROM research.model_versions");
        await ExecuteAsync(connection, "DELETE FROM research.training_sets WHERE set_id = @id", ("id", SetId));
    }

    private static ModelToRegister Model(char key, ValidationOutcome verdict, int width = 3, char sha = 'e', int trials = 1) =>
        new(new string(key, 64), new string(sha, 64), 100, "FastTree", null, SetId, "tf-1", width, 30, 10, 40, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), verdict, 6, 5, 4, true, 3.5, 0.65, 0.23,
            verdict == ValidationOutcome.Eligible ? string.Empty : "does not beat the baselines", "report text", "params", [-1.0, 0.0, 1.0], [0.3, 0.5, 0.7], [.. Enumerable.Repeat(0.25, width)], trials, 4, DateTimeOffset.UtcNow, "test");

    private async Task<long> RegisterAsync(ModelToRegister model)
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IModelRegistry>().RegisterAsync(model, Ct)).Id;
    }

    private async Task<Result<DesignationOutcome>> DesignateAsync(long id, ModelRole role)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<DesignateModelCommand, DesignationOutcome>>().HandleAsync(new DesignateModelCommand(id, role, "test reason", "test"), Ct);
    }

    [Fact]
    public async Task Before_any_registration_the_list_is_empty_and_a_model_is_not_found()
    {
        using var list = await Client.GetAsync(new Uri("/api/models", UriKind.Relative), Ct);
        using var one = await Client.GetAsync(new Uri("/api/models/1", UriKind.Relative), Ct);

        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
        one.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_model_is_registered_once_as_a_challenger_and_the_endpoints_show_it_with_its_limits()
    {
        var model = Model('b', ValidationOutcome.CannotBeatBaselines);

        var id = await RegisterAsync(model);
        var again = await RegisterAsync(model);

        again.ShouldBe(id);
        using var list = await Client.GetAsync(new Uri("/api/models", UriKind.Relative), Ct);
        var rows = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
        rows.GetArrayLength().ShouldBe(1);
        rows[0].GetProperty("role").GetString().ShouldBe("challenger");
        rows[0].GetProperty("verdict").GetString().ShouldBe("CannotBeatBaselines");
        rows[0].GetProperty("mayBeChampion").GetBoolean().ShouldBeFalse();
        rows[0].GetProperty("trainingSetId").GetString().ShouldBe(SetId);
        using var one = await Client.GetAsync(new Uri("/api/models/" + id, UriKind.Relative), Ct);
        var detail = await one.Content.ReadFromJsonAsync<JsonElement>(Ct);
        detail.GetProperty("designations").GetArrayLength().ShouldBe(1);
        detail.GetProperty("calibration").GetArrayLength().ShouldBe(3);
        detail.GetProperty("reportText").GetString().ShouldBe("report text");
        detail.GetProperty("limits").EnumerateArray().Select(l => l.GetString()).ShouldContain(l => l!.Contains("Advisory", StringComparison.Ordinal));
        detail.GetProperty("model").GetProperty("reasons")[0].GetString().ShouldBe("does not beat the baselines");
    }

    [Fact]
    public async Task Only_an_eligible_model_becomes_champion_and_a_new_champion_retires_the_old_one()
    {
        var weak = await RegisterAsync(Model('c', ValidationOutcome.Inconclusive));
        var first = await RegisterAsync(Model('d', ValidationOutcome.Eligible));
        var second = await RegisterAsync(Model('f', ValidationOutcome.Eligible));

        var refused = await DesignateAsync(weak, ModelRole.Champion);
        (await DesignateAsync(first, ModelRole.Champion)).IsSuccess.ShouldBeTrue();
        (await DesignateAsync(second, ModelRole.Champion)).IsSuccess.ShouldBeTrue();

        refused.Error.Code.ShouldBe(ModelErrors.NotEligibleCode);
        using var champions = await Client.GetAsync(new Uri("/api/models?role=champion", UriKind.Relative), Ct);
        var rows = await champions.Content.ReadFromJsonAsync<JsonElement>(Ct);
        rows.GetArrayLength().ShouldBe(1);
        rows[0].GetProperty("id").GetInt64().ShouldBe(second);
        using var retired = await Client.GetAsync(new Uri("/api/models?role=retired", UriKind.Relative), Ct);
        (await retired.Content.ReadFromJsonAsync<JsonElement>(Ct))[0].GetProperty("id").GetInt64().ShouldBe(first);
        using var history = await Client.GetAsync(new Uri("/api/models/" + first, UriKind.Relative), Ct);
        (await history.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("designations").EnumerateArray().Select(d => d.GetProperty("role").GetString()).ShouldBe(["challenger", "champion", "retired"]);
    }

    [Fact]
    public async Task A_model_judged_with_fewer_trials_than_the_models_registered_up_to_it_cannot_be_champion()
    {
        var one = await RegisterAsync(Model('1', ValidationOutcome.Eligible, sha: 'a', trials: 1));
        var rushed = await RegisterAsync(Model('2', ValidationOutcome.Eligible, sha: 'b', trials: 1));
        var careful = await RegisterAsync(Model('3', ValidationOutcome.Eligible, sha: 'c', trials: 3));
        using var scope = Factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IModelRegistry>();

        var others = await registry.CountOtherModelsAsync("tf-1", new string('a', 64), Ct);
        var stored = await registry.FindAsync(rushed, Ct);

        others.ShouldBe(2);
        stored!.RequiredTrials.ShouldBe(2);
        (await DesignateAsync(one, ModelRole.Champion)).IsSuccess.ShouldBeTrue();
        (await DesignateAsync(rushed, ModelRole.Champion)).Error.Code.ShouldBe(ModelErrors.NotEligibleCode);
        (await DesignateAsync(careful, ModelRole.Champion)).IsSuccess.ShouldBeTrue();
        using var list = await Client.GetAsync(new Uri("/api/models", UriKind.Relative), Ct);
        var rows = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
        rows.EnumerateArray().Single(r => r.GetProperty("id").GetInt64() == rushed).GetProperty("mayBeChampion").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_list_leaves_out_the_report_and_the_arrays_and_a_repeated_designation_writes_nothing()
    {
        var id = await RegisterAsync(Model('4', ValidationOutcome.Eligible));
        using var scope = Factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IModelRegistry>();

        var listed = (await registry.ListAsync(null, 10, Ct)).Single();
        var first = await registry.DesignateAsync(id, ModelRole.Champion, "r", "t", DateTimeOffset.UtcNow, Ct);
        var second = await registry.DesignateAsync(id, ModelRole.Champion, "r", "t", DateTimeOffset.UtcNow, Ct);

        listed.Model.ReportText.ShouldBeEmpty();
        listed.Model.Parameters.ShouldBeEmpty();
        listed.Model.Importance.ShouldBeEmpty();
        listed.Model.Reasons.ShouldBeEmpty();
        first.ShouldBeTrue();
        second.ShouldBeFalse();
        (await registry.DesignationsAsync(id, Ct)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_list_refuses_a_role_it_does_not_know_and_a_limit_out_of_range()
    {
        using var role = await Client.GetAsync(new Uri("/api/models?role=king", UriKind.Relative), Ct);
        using var limit = await Client.GetAsync(new Uri("/api/models?limit=500", UriKind.Relative), Ct);

        role.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        limit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_model_needs_a_stored_training_set()
    {
        var model = Model('9', ValidationOutcome.Eligible) with { TrainingSetId = new string('7', 64) };

        var failure = await Should.ThrowAsync<Npgsql.PostgresException>(() => RegisterAsync(model));

        failure.SqlState.ShouldBe(Npgsql.PostgresErrorCodes.ForeignKeyViolation);
    }
}
