using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Learning;

/// <summary>
/// CF-10 part 10d-1, AC11 and AC12 (ADR 0220) on a real database: the daily run, resolved from the container as the service resolves it, over a champion whose scored outcomes are miscalibrated by 20 pp (a test source of evidence replaces the only one there is, which says nothing is
/// available, so the real P6-09 detector sees 200 outcomes). With the switch OFF (no row) it writes a proposal, the day's evaluation and a derived bell notice and leaves the registry alone; with <c>ml.auto_rollback</c> ON (the owner's session, no password) it moves the champion to
/// challenger as <c>auto</c> with the switch row, never as an owner, and names no champion; a second run of the day does nothing. With the production source (none) the run finds no trigger. <c>ml.auto_promote</c> ON promotes nothing while no source of challenger evidence exists.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class MlMonitorRunApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string SetId = new('a', 64);

    /// <summary>The source of evidence of this test: 200 outcomes predicted at 0.7 and observed at 0.5, for every champion.</summary>
    private sealed class MiscalibratedEvidence : IRollbackEvidence
    {
        public ValueTask<RollbackInputs> ReadAsync(StoredModel champion, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new RollbackInputs([.. Enumerable.Range(0, 200).Select(i => new CalibrationObservation("o" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), i, 0.7, i % 2 == 0 ? 1 : 0))], null));
    }

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
        await ExecuteAsync(connection, "DELETE FROM research.rollback_proposals; DELETE FROM research.ml_monitor_checks; DELETE FROM research.ml_switch_settings; DELETE FROM research.ml_influence_checks; DELETE FROM research.model_designations; DELETE FROM research.model_versions");
        await ExecuteAsync(connection, "DELETE FROM research.learning_changelog WHERE subject_kind = 'model'");
        await ExecuteAsync(connection, "DELETE FROM research.training_sets WHERE set_id = @id", ("id", SetId));
    }

    private static ModelToRegister Model(char key, int trials) =>
        new(new string(key, 64), new string(key, 64), 100, "FastTree", null, SetId, "tf-1", 3, 30, 10, 40, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1), ValidationOutcome.Eligible, 6, 5, 4, true, 3.5, 0.65, 0.23,
            string.Empty, "report text", "params", [-1.0, 0.0, 1.0], [0.3, 0.5, 0.7], [0.25, 0.25, 0.25], trials, 4, DateTimeOffset.UtcNow, "test");

    private async Task<long> RegisterAsync(char key, ModelRole role)
    {
        using var scope = Factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IModelRegistry>();
        var id = (await registry.RegisterAsync(Model(key, await registry.CountOtherModelsAsync("tf-1", new string(key, 64), Ct) + 1), Ct)).Id;
        if (role != ModelRole.Challenger)
        {
            (await scope.ServiceProvider.GetRequiredService<ICommandHandler<DesignateModelCommand, DesignationOutcome>>().HandleAsync(new DesignateModelCommand(id, role, "setup", "test"), Ct)).IsSuccess.ShouldBeTrue();
        }

        return id;
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Host(bool withEvidence) => Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        if (withEvidence)
        {
            s.RemoveAll<IRollbackEvidence>();
            s.AddSingleton<IRollbackEvidence>(new MiscalibratedEvidence());
        }
    }));

    private static async Task<MlRunOutcome> RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluateMlModelsCommand, MlRunOutcome>>().HandleAsync(new EvaluateMlModelsCommand(), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private Task<string> RoleAsync(long id) => ScalarAsync<string>("SELECT COALESCE((SELECT role FROM research.model_designations WHERE model_id = @m ORDER BY id DESC LIMIT 1), 'challenger')", ("m", id));

    [Fact]
    public async Task With_the_production_source_of_evidence_which_has_none_the_run_finds_no_trigger_and_records_the_evaluation_as_unavailable()
    {
        var champion = await RegisterAsync('1', ModelRole.Champion);
        await using var host = Host(withEvidence: false);

        var outcome = await RunAsync(host.Services);

        outcome.ShouldBe(new MlRunOutcome(1, 1, 0, 0, 0, 0, 0, 0));
        (await RoleAsync(champion)).ShouldBe("champion");
        (await ScalarAsync<long>("SELECT count(*) FROM research.rollback_proposals")).ShouldBe(0);
        (await ScalarAsync<string>("SELECT verdicts::text FROM research.ml_monitor_checks WHERE model_id = @m", ("m", champion))).ShouldContain("Unavailable");
    }

    [Fact]
    public async Task With_the_setting_off_the_run_writes_the_proposal_the_evaluation_and_a_notice_and_changes_no_model_and_a_second_run_does_nothing()
    {
        var champion = await RegisterAsync('2', ModelRole.Champion);
        var designations = await ScalarAsync<long>("SELECT count(*) FROM research.model_designations");
        await using var host = Host(withEvidence: true);

        var first = await RunAsync(host.Services);
        var second = await RunAsync(host.Services);

        first.ShouldBe(new MlRunOutcome(1, 1, 0, 1, 0, 0, 0, 0));
        second.ShouldBe(new MlRunOutcome(1, 0, 1, 0, 0, 0, 0, 0), "the model was evaluated today");
        (await RoleAsync(champion)).ShouldBe("champion");
        (await ScalarAsync<long>("SELECT count(*) FROM research.model_designations")).ShouldBe(designations);
        (await ScalarAsync<string>("SELECT trigger || ':' || state || ':' || previous_qualifies::text FROM research.rollback_proposals WHERE model_id = @m", ("m", champion))).ShouldBe("calibration:proposed:false");
        (await ScalarAsync<long>("SELECT count(*) FROM research.ml_monitor_checks WHERE model_id = @m", ("m", champion))).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM research.ml_switch_settings")).ShouldBe(0, "no row means OFF");
        var notices = (await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/plans/notices", UriKind.Relative), Ct)).GetProperty("items").EnumerateArray().Where(i => i.GetProperty("kind").GetString() == "rollback_proposed").ToList();
        notices.Count.ShouldBe(1);

        // the proposal is visible to the owner with the three switches beside it, all off
        var list = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/learning/rollback/proposals?state=proposed", UriKind.Relative), Ct);
        list.GetProperty("items").GetArrayLength().ShouldBe(1);
        list.GetProperty("items")[0].GetProperty("trigger").GetString().ShouldBe("calibration");
        list.GetProperty("settings").GetProperty("autoRollback").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task With_the_setting_on_the_run_demotes_the_champion_as_auto_with_the_switch_row_never_as_an_owner_and_names_no_champion()
    {
        var champion = await RegisterAsync('3', ModelRole.Champion);
        using var switched = await Client.PutAsJsonAsync(new Uri("/api/learning/ml-settings", UriKind.Relative), new { autoRollback = true, reason = "I have seen two proposals" }, Ct);
        switched.StatusCode.ShouldBe(HttpStatusCode.OK);
        var switchId = await ScalarAsync<long>("SELECT id FROM research.ml_switch_settings WHERE name = 'ml.auto_rollback'");
        await using var host = Host(withEvidence: true);

        var outcome = await RunAsync(host.Services);

        outcome.ShouldBe(new MlRunOutcome(1, 1, 0, 1, 1, 0, 0, 0));
        (await RoleAsync(champion)).ShouldBe("challenger");
        (await ScalarAsync<long>("SELECT count(*) FROM research.model_designations WHERE role = 'champion' AND id IN (SELECT max(id) FROM research.model_designations GROUP BY model_id)")).ShouldBe(0, "no champion");
        var row = await ScalarAsync<string>("SELECT actor || '|' || evidence_ids::text || '|' || reason FROM research.learning_changelog WHERE subject = @s AND from_state = 'champion' AND to_state = 'challenger'", ("s", $"model:{champion}"));
        row.ShouldStartWith("auto|");
        row.ShouldContain($"ml-switch:{switchId}");
        row.ShouldContain("rollback (calibration)");
        (await ScalarAsync<string>("SELECT created_by FROM research.model_designations WHERE model_id = @m ORDER BY id DESC LIMIT 1", ("m", champion))).ShouldBe("auto");
        (await ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject_kind = 'model' AND actor LIKE 'owner:%'")).ShouldBe(0, "the owner is never recorded as the approver of an automatic decision");
        (await ScalarAsync<string>("SELECT state || ':' || actor || ':' || auto_setting_id::text FROM research.rollback_proposals WHERE model_id = @m", ("m", champion))).ShouldBe($"applied:auto:{switchId}");
        var kinds = (await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/plans/notices", UriKind.Relative), Ct)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("kind").GetString()).Where(k => k!.StartsWith("rollback", StringComparison.Ordinal)).ToList();
        kinds.ShouldBe(["rollback_applied_auto"], "the automatic demotion is visible on the bell as a resolved notice");

        // a second run of the day does nothing more (the demoted model is a challenger, so no champion is looked at)
        (await RunAsync(host.Services)).ShouldBe(new MlRunOutcome(0, 0, 0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task With_the_promotion_setting_on_a_challenger_is_offered_as_auto_and_with_no_source_of_challenger_evidence_nothing_is_promoted()
    {
        var challenger = await RegisterAsync('4', ModelRole.Challenger);
        using var switched = await Client.PutAsJsonAsync(new Uri("/api/learning/ml-settings", UriKind.Relative), new { autoPromote = true, reason = "I have read the rule", password = Factory.OwnerPassword }, Ct);
        switched.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var host = Host(withEvidence: false);

        var outcome = await RunAsync(host.Services);

        outcome.PromotedAutomatically.ShouldBe(0);
        outcome.NotPromoted.ShouldBe(1);
        (await RoleAsync(challenger)).ShouldBe("challenger");
        (await ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject_kind = 'model' AND to_state = 'champion'")).ShouldBe(0);
    }
}
