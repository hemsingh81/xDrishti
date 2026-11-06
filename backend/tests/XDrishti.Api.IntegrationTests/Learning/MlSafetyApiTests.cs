using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Learning;

/// <summary>
/// CF-10 part 10d-1, AC11 to AC13 (ADR 0220) over HTTP and a real database: the three ML switches are the owner's acts (a fresh password to raise how far a model may matter, the session only to lower it) and OFF with no row; a rollback proposal is applied by the owner (session and
/// CSRF, no password) through the registry's designation and its changelog row, twice and concurrently without a second demotion, and never re-promotes anything; a promotion needs a fresh password and, with the only evidence source there is (none), is refused with the reason; the stored
/// influence switch ON does not open the gate while the evidence is unavailable. Nothing here can place an order.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class MlSafetyApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string SetId = new('a', 64);

    private static readonly Uri Proposals = new("/api/learning/rollback/proposals", UriKind.Relative);

    private static readonly Uri Settings = new("/api/learning/ml-settings", UriKind.Relative);

    private static Uri Apply(long id) => new($"/api/learning/rollback/{id}/apply", UriKind.Relative);

    private static Uri Promote(long id) => new($"/api/learning/models/{id}/promote", UriKind.Relative);

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

    // the verdict of a champion must allow for every model of the feature version registered up to it (ChampionRule): the Nth model registered here says N trials
    private static ModelToRegister Model(char key, char sha, int trials) =>
        new(new string(key, 64), new string(sha, 64), 100, "FastTree", null, SetId, "tf-1", 3, 30, 10, 40, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1), ValidationOutcome.Eligible, 6, 5, 4, true, 3.5, 0.65, 0.23,
            string.Empty, "report text", "params", [-1.0, 0.0, 1.0], [0.3, 0.5, 0.7], [0.25, 0.25, 0.25], trials, 4, DateTimeOffset.UtcNow, "test");

    private async Task<long> RegisterAsync(char key, char sha, ModelRole role = ModelRole.Challenger)
    {
        using var scope = Factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IModelRegistry>();
        var trials = await registry.CountOtherModelsAsync("tf-1", new string(sha, 64), Ct) + 1;
        var id = (await registry.RegisterAsync(Model(key, sha, trials), Ct)).Id;
        if (role != ModelRole.Challenger)
        {
            (await scope.ServiceProvider.GetRequiredService<ICommandHandler<DesignateModelCommand, DesignationOutcome>>().HandleAsync(new DesignateModelCommand(id, role, "setup", "test"), Ct)).IsSuccess.ShouldBeTrue();
        }

        return id;
    }

    private async Task<long> ProposeAsync(long modelId, string trigger = "calibration", long? previous = null)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRollbackProposalStore>();
        return (await store.InsertAsync(new NewRollbackProposal(modelId, trigger, 20, 11.5, "the calibration numbers", [], previous, false, new string('a', 64)), new DateOnly(2026, 11, 6), Ct))!.Value;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, Uri uri, object? body = null, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body) };
        using var response = await (client ?? Client).SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> SwitchAsync(object body) => SendAsync(HttpMethod.Put, Settings, body);

    private Task<string> RoleAsync(long id) => ScalarAsync<string>("SELECT COALESCE((SELECT role FROM research.model_designations WHERE model_id = @m ORDER BY id DESC LIMIT 1), 'challenger')", ("m", id));

    [Fact]
    public async Task Without_a_session_every_route_is_401_and_nothing_is_written()
    {
        var anonymous = Factory.CreateAnonymousClient();

        var results = new[]
        {
            await SendAsync(HttpMethod.Get, Proposals, client: anonymous),
            await SendAsync(HttpMethod.Post, Apply(1), client: anonymous),
            await SendAsync(HttpMethod.Put, Settings, new { influenceEnabled = true, reason = "x", password = Factory.OwnerPassword }, anonymous),
            await SendAsync(HttpMethod.Put, Settings, new { autoRollback = true, reason = "x" }, anonymous),
            await SendAsync(HttpMethod.Post, Promote(1), new { reason = "x", password = Factory.OwnerPassword }, anonymous),
        };

        results.ShouldAllBe(r => r.Status == HttpStatusCode.Unauthorized);
        (await ScalarAsync<long>("SELECT count(*) FROM research.ml_switch_settings")).ShouldBe(0);
    }

    [Fact]
    public async Task The_list_is_empty_at_first_and_all_three_switches_are_off_with_no_row()
    {
        var result = await SendAsync(HttpMethod.Get, Proposals);

        result.Status.ShouldBe(HttpStatusCode.OK);
        result.Body.GetProperty("items").GetArrayLength().ShouldBe(0);
        var settings = result.Body.GetProperty("settings");
        foreach (var name in new[] { "influenceEnabled", "autoPromote", "autoRollback" })
        {
            settings.GetProperty(name).GetBoolean().ShouldBeFalse(name);
        }

        foreach (var name in new[] { "influenceSettingId", "autoPromoteSettingId", "autoRollbackSettingId" })
        {
            settings.GetProperty(name).ValueKind.ShouldBe(JsonValueKind.Null, name);
        }

        (await SendAsync(HttpMethod.Get, new Uri("/api/learning/rollback/proposals?state=done", UriKind.Relative))).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(HttpMethod.Get, new Uri("/api/learning/rollback/proposals?limit=0", UriKind.Relative))).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // ---- the switches ----

    [Theory]
    [InlineData("influenceEnabled")]
    [InlineData("autoPromote")]
    public async Task Turning_influence_or_auto_promotion_on_needs_a_fresh_password_a_missing_one_is_403_a_wrong_one_401_and_nothing_is_written(string switchName)
    {
        object Body(string? password) => switchName == "influenceEnabled" ? (object)new { influenceEnabled = true, reason = "I have read the evidence", password } : new { autoPromote = true, reason = "I have read the evidence", password };

        var missing = await SwitchAsync(Body(null));
        var empty = await SwitchAsync(Body(string.Empty));
        var wrong = await SwitchAsync(Body("not-the-password"));

        missing.Status.ShouldBe(HttpStatusCode.Forbidden);
        missing.Body.GetProperty("code").GetString().ShouldBe("auth.step_up_required");
        empty.Status.ShouldBe(HttpStatusCode.Forbidden);
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await ScalarAsync<long>("SELECT count(*) FROM research.ml_switch_settings")).ShouldBe(0);
    }

    [Fact]
    public async Task With_the_password_the_owner_turns_influence_on_the_row_names_the_owner_from_the_session_and_the_gate_still_refuses_because_the_evidence_is_unavailable()
    {
        var model = await RegisterAsync('c', 'd', ModelRole.Champion);

        var on = await SwitchAsync(new { influenceEnabled = true, reason = "I have read the evidence", password = Factory.OwnerPassword, actor = "owner:evil@example.test" });

        on.Status.ShouldBe(HttpStatusCode.OK);
        on.Body.GetProperty("changed").EnumerateArray().Select(c => c.GetString()).ShouldBe(["ml.influence.enabled"]);
        on.Body.GetProperty("switches").GetProperty("influence").GetProperty("on").GetBoolean().ShouldBeTrue();
        on.Body.GetProperty("switches").GetProperty("autoPromote").GetProperty("on").GetBoolean().ShouldBeFalse();
        (await ScalarAsync<string>("SELECT name || '|' || enabled::text || '|' || actor || '|' || reason FROM research.ml_switch_settings")).ShouldBe($"ml.influence.enabled|true|owner:{ApiFactory.OwnerEmail}|I have read the evidence");

        using var response = await Client.GetAsync(new Uri($"/api/learning/models/{model}/influence", UriKind.Relative), Ct);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        view.GetProperty("switchOn").GetBoolean().ShouldBeTrue();
        view.GetProperty("mayInfluence").GetBoolean().ShouldBeFalse("the hold-out figures, the forward figures and the calibration detector have no source");
        var codes = view.GetProperty("reasons").EnumerateArray().Select(r => r.GetProperty("code").GetString()).ToList();
        codes.ShouldNotContain("switch_off");
        codes.ShouldContain("holdout_not_scored");

        // the owner turns it off again without a password: the safe direction; the history keeps both rows
        var off = await SwitchAsync(new { influenceEnabled = false, reason = "back to advisory" });
        off.Status.ShouldBe(HttpStatusCode.OK);
        (await ScalarAsync<long>("SELECT count(*) FROM research.ml_switch_settings")).ShouldBe(2);
        (await ScalarAsync<bool>("SELECT enabled FROM research.ml_switch_settings ORDER BY id DESC LIMIT 1")).ShouldBeFalse();
    }

    [Fact]
    public async Task The_automatic_rollback_switch_needs_the_session_and_the_csrf_token_only_and_a_change_to_the_same_value_writes_nothing()
    {
        var on = await SwitchAsync(new { autoRollback = true, reason = "I have seen two proposals" });
        var same = await SwitchAsync(new { autoRollback = true, reason = "again" });

        on.Status.ShouldBe(HttpStatusCode.OK);
        on.Body.GetProperty("switches").GetProperty("autoRollback").GetProperty("on").GetBoolean().ShouldBeTrue();
        same.Body.GetProperty("changed").GetArrayLength().ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM research.ml_switch_settings WHERE name = 'ml.auto_rollback'")).ShouldBe(1);
        var list = await SendAsync(HttpMethod.Get, Proposals);
        list.Body.GetProperty("settings").GetProperty("autoRollback").GetBoolean().ShouldBeTrue();
        list.Body.GetProperty("settings").GetProperty("autoRollbackSettingId").GetInt64().ShouldBeGreaterThan(0);
        list.Body.GetProperty("settings").GetProperty("influenceEnabled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_change_without_a_csrf_token_is_refused_and_a_change_that_names_no_switch_or_gives_no_reason_is_400()
    {
        using var noToken = new HttpRequestMessage(HttpMethod.Put, Settings) { Content = JsonContent.Create(new { autoRollback = true, reason = "x" }) };
        var withoutCsrf = Factory.CreateAnonymousClient();
        withoutCsrf.DefaultRequestHeaders.Add("Cookie", Client.DefaultRequestHeaders.GetValues("Cookie").Single());
        using var refused = await withoutCsrf.SendAsync(noToken, Ct);

        var none = await SwitchAsync(new { reason = "x" });
        var blank = await SwitchAsync(new { autoRollback = true, reason = "  " });
        var longReason = await SwitchAsync(new { autoRollback = true, reason = new string('x', 501) });

        refused.StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
        none.Status.ShouldBe(HttpStatusCode.BadRequest);
        none.Body.GetProperty("code").GetString().ShouldBe("learning.ml_settings_invalid");
        blank.Status.ShouldBe(HttpStatusCode.BadRequest);
        longReason.Status.ShouldBe(HttpStatusCode.BadRequest);
        (await ScalarAsync<long>("SELECT count(*) FROM research.ml_switch_settings")).ShouldBe(0);
    }

    // ---- applying a rollback proposal ----

    [Fact]
    public async Task The_owner_applies_a_proposal_the_champion_becomes_a_challenger_through_the_registry_and_a_second_apply_changes_nothing()
    {
        var model = await RegisterAsync('1', 'a', ModelRole.Champion);
        var id = await ProposeAsync(model);

        var first = await SendAsync(HttpMethod.Post, Apply(id));
        var second = await SendAsync(HttpMethod.Post, Apply(id));

        first.Status.ShouldBe(HttpStatusCode.OK);
        (first.Body.GetProperty("demoted").GetBoolean(), first.Body.GetProperty("alreadyApplied").GetBoolean()).ShouldBe((true, false));
        second.Status.ShouldBe(HttpStatusCode.OK);
        second.Body.GetProperty("alreadyApplied").GetBoolean().ShouldBeTrue();
        (await RoleAsync(model)).ShouldBe("challenger");
        (await ScalarAsync<long>("SELECT count(*) FROM research.model_designations WHERE model_id = @m AND role = 'challenger'", ("m", model))).ShouldBe(2, "the registration and the demotion");
        var row = await ScalarAsync<string>("SELECT actor || '|' || from_state || '|' || to_state || '|' || evidence_ids::text FROM research.learning_changelog WHERE subject = @s AND to_state = 'challenger' AND from_state = 'champion'", ("s", $"model:{model}"));
        row.ShouldStartWith("owner:");
        row.ShouldContain($"rollback-proposal:{id}");
        (await ScalarAsync<string>("SELECT state || ',' || actor FROM research.rollback_proposals WHERE id = @id", ("id", id))).ShouldStartWith("applied,owner:");
        (await ScalarAsync<long>("SELECT count(*) FROM research.model_designations WHERE role = 'champion' AND model_id = @m", ("m", model))).ShouldBe(1, "applying a rollback names no champion");
        (await SendAsync(HttpMethod.Post, Apply(id + 1000))).Body.GetProperty("code").GetString().ShouldBe("learning.rollback_not_found");
    }

    [Fact]
    public async Task Applying_never_re_promotes_the_previous_champion_even_when_it_is_named_in_the_proposal()
    {
        var previous = await RegisterAsync('2', 'b', ModelRole.Champion);
        var current = await RegisterAsync('3', 'c');
        using (var scope = Factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ICommandHandler<DesignateModelCommand, DesignationOutcome>>().HandleAsync(new DesignateModelCommand(current, ModelRole.Champion, "better", "test"), Ct)).IsSuccess.ShouldBeTrue();
        }

        var id = await ProposeAsync(current, "band", previous);

        var applied = await SendAsync(HttpMethod.Post, Apply(id));

        applied.Body.GetProperty("previousChampionId").GetInt64().ShouldBe(previous);
        (await RoleAsync(current)).ShouldBe("challenger");
        (await RoleAsync(previous)).ShouldBe("retired", "re-promotion is the owner's act with a fresh password");
        (await ScalarAsync<long>("SELECT count(*) FROM research.model_designations WHERE role = 'champion' AND model_id IN (@a, @b)", ("a", previous), ("b", current))).ShouldBe(2, "only the two earlier designations");
    }

    [Fact]
    public async Task Concurrent_applies_demote_once_and_decide_the_proposal_once()
    {
        var model = await RegisterAsync('4', 'd', ModelRole.Champion);
        var id = await ProposeAsync(model);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var scope = Factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ICommandHandler<ApplyRollbackCommand, ApplyRollbackOutcome>>().HandleAsync(new ApplyRollbackCommand(id, "owner:racer@example.test"), Ct);
        }));

        outcomes.ShouldAllBe(o => o.IsSuccess, string.Join("; ", outcomes.Where(o => o.IsFailure).Select(o => o.Error.Message)));
        (await ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject = @s AND from_state = 'champion' AND to_state = 'challenger'", ("s", $"model:{model}"))).ShouldBe(1);
        (await RoleAsync(model)).ShouldBe("challenger");
        (await ScalarAsync<long>("SELECT count(*) FROM research.rollback_proposals WHERE id = @id AND state = 'applied'", ("id", id))).ShouldBe(1);
        outcomes.Count(o => o.Value is { AlreadyApplied: false }).ShouldBe(1, "one call recorded the decision");
    }

    [Fact]
    public async Task A_retired_model_is_never_demoted_into_the_challenger_pool_the_registry_checks_the_role_under_its_lock()
    {
        var first = await RegisterAsync('e', 'e', ModelRole.Champion);
        var second = await RegisterAsync('f', 'f', ModelRole.Champion); // retires the first
        (await RoleAsync(first)).ShouldBe("retired");
        var rows = await ScalarAsync<long>("SELECT count(*) FROM research.model_designations");
        var id = await ProposeAsync(first);

        using var scope = Factory.Services.CreateScope();
        var demoted = await scope.ServiceProvider.GetRequiredService<ICommandHandler<DemoteModelCommand, DesignationOutcome>>().HandleAsync(new DemoteModelCommand(first, "stale", "owner:racer@example.test", []), Ct);
        var applied = await SendAsync(HttpMethod.Post, Apply(id));

        demoted.Value.Changed.ShouldBeFalse();
        demoted.Value.Role.ShouldBe("retired");
        applied.Body.GetProperty("demoted").GetBoolean().ShouldBeFalse();
        (await RoleAsync(first)).ShouldBe("retired");
        (await RoleAsync(second)).ShouldBe("champion");
        (await ScalarAsync<long>("SELECT count(*) FROM research.model_designations")).ShouldBe(rows, "nothing was written");
    }

    [Fact]
    public async Task The_command_line_designation_refuses_a_model_that_was_rolled_back_and_names_a_clean_one()
    {
        var champion = await RegisterAsync('d', 'd', ModelRole.Champion);
        var id = await ProposeAsync(champion);
        await SendAsync(HttpMethod.Post, Apply(id));
        var clean = await RegisterAsync('c', 'c');

        using var scope = Factory.Services.CreateScope();
        var designate = scope.ServiceProvider.GetRequiredService<ICommandHandler<OwnerDesignateModelCommand, DesignationOutcome>>();
        var refused = await designate.HandleAsync(new OwnerDesignateModelCommand(champion, ModelRole.Champion, "back", "cli:xd"), Ct);
        var named = await designate.HandleAsync(new OwnerDesignateModelCommand(clean, ModelRole.Champion, "retrained", "cli:xd"), Ct);

        refused.Error.Code.ShouldBe("learning.designate_rolled_back");
        (await RoleAsync(champion)).ShouldBe("challenger");
        named.IsSuccess.ShouldBeTrue(named.IsFailure ? named.Error.Message : string.Empty);
        (await RoleAsync(clean)).ShouldBe("champion");
    }

    [Fact]
    public async Task The_bell_shows_an_open_proposal_and_not_an_applied_one()
    {
        var model = await RegisterAsync('5', 'e', ModelRole.Champion);
        var id = await ProposeAsync(model);

        async Task<List<string>> KindsAsync() =>
            [.. (await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/plans/notices", UriKind.Relative), Ct)).GetProperty("items").EnumerateArray().Where(i => i.GetProperty("id").GetString()!.StartsWith("rollback", StringComparison.Ordinal)).Select(i => i.GetProperty("kind").GetString()!)];

        var open = await KindsAsync();
        await SendAsync(HttpMethod.Post, Apply(id));
        var applied = await KindsAsync();

        open.ShouldBe(["rollback_proposed"]);
        applied.ShouldBeEmpty("applied by the owner: nothing is open and an owner's act needs no resolved notice");
    }

    // ---- promotion ----

    [Fact]
    public async Task Promotion_needs_a_fresh_password_and_with_no_source_of_evidence_is_refused_with_the_reasons_and_nothing_changes()
    {
        var champion = await RegisterAsync('6', 'f', ModelRole.Champion);
        var challenger = await RegisterAsync('7', '7');
        var changelog = await ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject_kind = 'model'");

        var missing = await SendAsync(HttpMethod.Post, Promote(challenger), new { reason = "better" });
        var wrong = await SendAsync(HttpMethod.Post, Promote(challenger), new { reason = "better", password = "not-the-password" });
        var refused = await SendAsync(HttpMethod.Post, Promote(challenger), new { reason = "better", password = Factory.OwnerPassword, actor = "auto" });
        var unknown = await SendAsync(HttpMethod.Post, Promote(challenger + 1000), new { reason = "better", password = Factory.OwnerPassword });

        missing.Status.ShouldBe(HttpStatusCode.Forbidden);
        missing.Body.GetProperty("code").GetString().ShouldBe("auth.step_up_required");
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        refused.Status.ShouldBe(HttpStatusCode.Conflict);
        refused.Body.GetProperty("code").GetString().ShouldBe("learning.promotion_refused");
        refused.Body.GetProperty("title").GetString()!.ShouldContain("not available");
        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
        (await RoleAsync(challenger)).ShouldBe("challenger");
        (await RoleAsync(champion)).ShouldBe("champion");
        (await ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject_kind = 'model'")).ShouldBe(changelog);
        (await ScalarAsync<long>("SELECT count(*) FROM research.ml_switch_settings")).ShouldBe(0, "promotion never touches a switch");
    }

    [Fact]
    public async Task A_retired_model_is_not_re_promoted_while_its_latest_evaluation_does_not_satisfy_the_predicate_without_the_switch()
    {
        var first = await RegisterAsync('8', '8', ModelRole.Champion);
        var second = await RegisterAsync('9', '9', ModelRole.Champion); // retires the first
        (await RoleAsync(first)).ShouldBe("retired");

        var refused = await SendAsync(HttpMethod.Post, Promote(first), new { reason = "bring it back", password = Factory.OwnerPassword });

        refused.Status.ShouldBe(HttpStatusCode.Conflict);
        refused.Body.GetProperty("code").GetString().ShouldBe("learning.repromotion_refused");
        (await RoleAsync(first)).ShouldBe("retired");
        (await RoleAsync(second)).ShouldBe("champion");
    }

    [Fact]
    public async Task Promoting_the_current_champion_again_is_a_no_op_that_still_needs_the_password()
    {
        var champion = await RegisterAsync('a', 'a', ModelRole.Champion);

        var noPassword = await SendAsync(HttpMethod.Post, Promote(champion), new { reason = "x" });
        var again = await SendAsync(HttpMethod.Post, Promote(champion), new { reason = "x", password = Factory.OwnerPassword });

        noPassword.Status.ShouldBe(HttpStatusCode.Forbidden);
        again.Status.ShouldBe(HttpStatusCode.OK);
        (again.Body.GetProperty("kind").GetString(), again.Body.GetProperty("changed").GetBoolean()).ShouldBe(("already_champion", false));
    }
}
