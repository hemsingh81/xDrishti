using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Learning;

/// <summary>
/// CF-10 part 10c-2 (ADR 0217) on a real database: <c>GET /api/learning/models/{id}/influence</c> reads the predicate and writes nothing; with the owner's switch off (the default, no row) every model is refused with its reasons; the gate records each changed answer as an insert-only
/// check; a hold-out look taken by another model of the family reads as <c>holdout_reused</c>.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class MlInfluenceApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string SetId = new('a', 64);

    private static readonly DateOnly BlockStart = new(2026, 5, 1);

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

    public override async ValueTask DisposeAsync()
    {
        await CleanAsync();
        await base.DisposeAsync();
    }

    private async Task CleanAsync()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM lab.holdout_unlocks; DELETE FROM lab.holdout_blocks; DELETE FROM research.ml_influence_checks; DELETE FROM research.model_designations; DELETE FROM research.model_versions");
        await ExecuteAsync(connection, "DELETE FROM research.training_sets WHERE set_id = @id", ("id", SetId));
    }

    private static ModelToRegister Model(char key, char sha, bool heldOut = true) =>
        new(new string(key, 64), new string(sha, 64), 100, "FastTree", null, SetId, "tf-1", 3, 30, 10, 40, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), heldOut ? BlockStart : null, ValidationOutcome.Eligible, 6, 5, 4, true, 3.5, 0.65, 0.23,
            string.Empty, "report text", "params", [-1.0, 0.0, 1.0], [0.3, 0.5, 0.7], [0.25, 0.25, 0.25], 1, 4, DateTimeOffset.UtcNow, "test");

    private async Task<long> RegisterAsync(ModelToRegister model)
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IModelRegistry>().RegisterAsync(model, Ct)).Id;
    }

    private async Task DesignateChampionAsync(long id)
    {
        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ICommandHandler<DesignateModelCommand, DesignationOutcome>>().HandleAsync(new DesignateModelCommand(id, ModelRole.Champion, "test", "test"), Ct)).IsSuccess.ShouldBeTrue();
    }

    private async Task<JsonElement> InfluenceAsync(long id)
    {
        using var response = await Client.GetAsync(new Uri($"/api/learning/models/{id}/influence", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static string[] Codes(JsonElement reasons) => [.. reasons.EnumerateArray().Select(r => r.GetProperty("code").GetString()!)];

    [Fact]
    public async Task An_unknown_model_is_a_404_problem()
    {
        using var response = await Client.GetAsync(new Uri("/api/learning/models/12345/influence", UriKind.Relative), Ct);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        problem.GetProperty("code").GetString().ShouldBe("models.not_found");
    }

    [Fact]
    public async Task A_challenger_is_refused_with_its_reasons_and_the_read_writes_nothing()
    {
        var id = await RegisterAsync(Model('b', 'e'));

        var view = await InfluenceAsync(id);

        view.GetProperty("mayInfluence").GetBoolean().ShouldBeFalse();
        view.GetProperty("role").GetString().ShouldBe("challenger");
        view.GetProperty("switchOn").GetBoolean().ShouldBeFalse("no row means off");
        view.GetProperty("terms").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ShouldBe(["HoldOutBeatsNoMl", "ForwardShadowOk", "NoCalibrationAlarm", "SwitchOn", "IsChampion"]);
        view.GetProperty("terms").EnumerateArray().Select(t => t.GetProperty("state").GetString()).ShouldBe(["unavailable", "unavailable", "unavailable", "fails", "fails"]);
        Codes(view.GetProperty("reasons")).ShouldBe(["holdout_not_scored", "forward_not_available", "calibration_alarm_unavailable", "switch_off", "not_champion"]);
        view.GetProperty("holdOut").GetProperty("state").GetString().ShouldBe("kept");
        view.GetProperty("holdOut").GetProperty("family").GetString().ShouldBe("model-family:tf-1");
        view.GetProperty("settingHash").GetString()!.ShouldMatch("^[0-9a-f]{64}$");
        view.GetProperty("notes").GetArrayLength().ShouldBeGreaterThan(0);
        (await ScalarAsync<long>("SELECT count(*) FROM research.ml_influence_checks")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM lab.holdout_unlocks")).ShouldBe(0);
    }

    [Fact]
    public async Task The_champion_is_refused_too_while_the_switch_is_off()
    {
        var id = await RegisterAsync(Model('c', 'd'));
        await DesignateChampionAsync(id);

        var view = await InfluenceAsync(id);

        view.GetProperty("role").GetString().ShouldBe("champion");
        view.GetProperty("mayInfluence").GetBoolean().ShouldBeFalse();
        Codes(view.GetProperty("reasons")).ShouldContain("switch_off");
        Codes(view.GetProperty("reasons")).ShouldNotContain("not_champion");
    }

    [Fact]
    public async Task A_hold_out_look_taken_by_another_model_of_the_family_reads_as_reused_whatever_the_numbers()
    {
        var first = await RegisterAsync(Model('1', 'a'));
        var second = await RegisterAsync(Model('2', 'b'));
        using (var scope = Factory.Services.CreateScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IModelRegistry>();
            var looks = scope.ServiceProvider.GetRequiredService<ModelHoldOutLooks>();
            await scope.ServiceProvider.GetRequiredService<IHoldOutStore>().InsertBlockAsync(new HoldOutBlock(BlockStart, new DateOnly(2024, 10, 1), new DateOnly(2026, 10, 30), 6, new string('d', 64), DateTimeOffset.UtcNow), Ct);

            var claimed = await looks.ClaimAsync((await registry.FindAsync(first, Ct))!, "owner", "owner:x", new string('a', 64), Ct);
            var refused = await looks.ClaimAsync((await registry.FindAsync(second, Ct))!, "owner", "owner:x", new string('a', 64), Ct);

            claimed.IsSuccess.ShouldBeTrue();
            claimed.Value.ReusedWindow.ShouldBeFalse();
            refused.Error.Code.ShouldBe(MlInfluenceErrors.HoldOutReusedCode);
        }

        var own = await InfluenceAsync(first);
        var other = await InfluenceAsync(second);

        own.GetProperty("holdOut").GetProperty("state").GetString().ShouldBe("used");
        own.GetProperty("holdOut").GetProperty("probabilityBeatsNoMl").ValueKind.ShouldBe(JsonValueKind.Null, "the figures are not scored in this version: unavailable, never a pass");
        Codes(own.GetProperty("reasons")).ShouldContain("holdout_probability_unavailable");
        other.GetProperty("holdOut").GetProperty("state").GetString().ShouldBe("reused");
        other.GetProperty("mayInfluence").GetBoolean().ShouldBeFalse();
        Codes(other.GetProperty("reasons")).ShouldContain("holdout_reused");
        Codes(other.GetProperty("reasons")).ShouldNotContain("holdout_probability_unavailable");
    }

    [Fact]
    public async Task The_gate_refuses_with_the_switch_off_records_each_changed_answer_once_and_hands_out_no_score()
    {
        var id = await RegisterAsync(Model('3', 'c'));
        IReadOnlyList<MlInfluenceAnswer> answers;
        MlGatedScore score;
        using (var scope = Factory.Services.CreateScope())
        {
            var gate = scope.ServiceProvider.GetRequiredService<IMlInfluenceGate>();
            answers = [await gate.AnswerAsync(id, Ct), await gate.AnswerAsync(id, Ct)];
            score = await gate.ScoreAsync(id, [0.1, 0.2, 0.3], Ct);
        }

        answers.ShouldAllBe(a => !a.MayInfluence);
        score.PWin.ShouldBeNull();
        (await ScalarAsync<long>("SELECT count(*) FROM research.ml_influence_checks WHERE model_id = @id", ("id", id))).ShouldBe(1, "the same evaluation is stored once");

        await DesignateChampionAsync(id);
        using (var scope = Factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IMlInfluenceGate>().AnswerAsync(id, Ct)).MayInfluence.ShouldBeFalse();
        }

        (await ScalarAsync<long>("SELECT count(*) FROM research.ml_influence_checks WHERE model_id = @id", ("id", id))).ShouldBe(2, "a changed evaluation is a new row");
        (await ScalarAsync<bool>("SELECT bool_or(may_influence) FROM research.ml_influence_checks")).ShouldBeFalse();
        (await ScalarAsync<string>("SELECT terms::text FROM research.ml_influence_checks WHERE model_id = @id ORDER BY id DESC LIMIT 1", ("id", id))).ShouldContain("IsChampion");
        (await ScalarAsync<long>("SELECT count(*) FROM research.ml_influence_checks WHERE setting_hash ~ '^[0-9a-f]{64}$' AND inputs_hash ~ '^[0-9a-f]{64}$'")).ShouldBe(2);
    }

    [Fact]
    public async Task The_gate_refuses_an_unknown_model_and_writes_no_check()
    {
        using var scope = Factory.Services.CreateScope();

        var answer = await scope.ServiceProvider.GetRequiredService<IMlInfluenceGate>().AnswerAsync(98765, Ct);

        answer.MayInfluence.ShouldBeFalse();
        answer.Reasons.ShouldHaveSingleItem().Code.ShouldBe("model_not_found");
        (await ScalarAsync<long>("SELECT count(*) FROM research.ml_influence_checks")).ShouldBe(0);
    }

}
