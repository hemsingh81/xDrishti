using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Autonomy;
using XDrishti.Application.Planning;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// Spec P7-12 (ADR 0091) on a real database: an automatic approval is a new kind that the DATABASE allows only against the newest settings row with the switch on, for a primary item, before the cutoff, as the rule, within the cap; the owner's rows
/// are unchanged; with the switch off (the default) nothing differs from before; the settings are appended by the owner's authenticated, step-up act only.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class AutoApprovalDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid AccountId = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a02");

    private static readonly DateOnly Session = new(2099, 1, 5);

    private static readonly DateTimeOffset ReviewCutoff = new(2099, 1, 5, 3, 35, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset PastCutoff = new(2026, 10, 13, 3, 35, 0, TimeSpan.Zero);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES (@id, 'probe-auto', 'Probe', 'Dhan', '1000000078', current_date, '#1A2B3C', now(), now(), false, false, false, 200)
            """,
            ("id", AccountId));
    }

    private static NewPlanItem Item(int rank, TicketGroup group, double? pwinLower = 0.7) =>
        new(
            rank, group, "NSE:RELIANCE", "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30",
            group == TicketGroup.Primary ? [] : ["rank_below_cut"],
            new PlanEvidence("ORB|15m|BUY|intraday|FIXED_RR|*", 41, "about 2.1 years, replayed history, modelled fills, no hold-out", "shrunk posterior win rate of the cell", 0.72, 0.12, [], null, pwinLower),
            [new NewAllocation(AccountId, "paper", 10)]);

    private static NewPlan Plan(string key, params NewPlanItem[] items) =>
        new(key, Session, ReviewCutoff, key.EndsWith("-b", StringComparison.Ordinal) ? 2 : 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, items);

    private async Task<TResult> WithAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private async Task<(long Plan, long[] Items)> PlanStoredAsync(string key, params NewPlanItem[] items)
    {
        var published = await WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(Plan(key, items), Ct).AsTask());
        var rows = await WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(published.Id, Ct).AsTask());
        return (published.Id, [.. rows.Select(r => r.Id)]);
    }

    private Task<Result<AutonomySettingsView>> ChangeAsync(bool on, int cap = 3, string actor = "owner:test@example.com") =>
        WithAsync(p => p.GetRequiredService<ICommandHandler<ChangeAutonomySettingsCommand, AutonomySettingsView>>().HandleAsync(new ChangeAutonomySettingsCommand(actor, "test", on, false, 0.58, cap), Ct).AsTask());

    private Task<AutonomySettings> CurrentAsync() => WithAsync(p => p.GetRequiredService<IAutonomySettingsStore>().CurrentAsync(Ct).AsTask());

    private async Task<MoveStatus> AutoAsync(long item, AutonomySettings? basis = null)
    {
        var s = basis ?? await CurrentAsync();
        return await WithAsync(p => p.GetRequiredService<IAutoApprovalStore>().AutoApproveAsync(new AutoApproveMove(item, s.Id ?? 0, s.Hash ?? new string('0', 64), AutoApprovalWording.PolicyVersion, AutoApprovalWording.Marker + ": test"), Ct).AsTask());
    }

    private async Task<string?> SqlStateAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        try
        {
            await command.ExecuteNonQueryAsync(Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    private const string InsertAuto = """
        INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, cutoff, decided_at, settings_id, settings_hash, policy_version, modifications, confirm_reason)
        SELECT p.id, @item, @kind, @decision, @actor, 'r', p.review_cutoff, now(), @sid, @sh, @pv, @mods, NULL FROM trading.plans p JOIN trading.plan_items i ON i.plan_id = p.id WHERE i.id = @item
        """;

    private async Task<string?> DirectAutoAsync(long item, string kind = "auto", string decision = "approve", string actor = "rule:autonomy", long? sid = null, string? hash = null, string? mods = null)
    {
        var s = await CurrentAsync();
        return await SqlStateAsync(
            InsertAuto,
            ("item", item), ("kind", kind), ("decision", decision), ("actor", actor),
            ("sid", (object?)(sid ?? s.Id) ?? DBNull.Value), ("sh", (object?)(hash ?? s.Hash) ?? DBNull.Value), ("pv", "auto-approval/2"), ("mods", (object?)mods ?? DBNull.Value));
    }

    [Fact]
    public async Task With_nothing_stored_the_settings_read_as_everything_off_and_the_pass_changes_nothing()
    {
        var (plan, items) = await PlanStoredAsync("auto-off", Item(1, TicketGroup.Primary), Item(2, TicketGroup.Reserve));

        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/autonomy/settings", UriKind.Relative), Ct);
        var pass = await WithAsync(p => p.GetRequiredService<ICommandHandler<AutoApprovePlanCommand, AutoApprovalOutcome>>().HandleAsync(new AutoApprovePlanCommand(plan), Ct).AsTask());

        view.GetProperty("autoApproveTickets").GetBoolean().ShouldBeFalse();
        view.GetProperty("promoteFromReserve").GetBoolean().ShouldBeFalse();
        view.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Null);
        pass.Value.SwitchOn.ShouldBeFalse();
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events WHERE item_id IS NOT NULL")).ShouldBe(0);
        items.Length.ShouldBe(2);
    }

    [Fact]
    public async Task With_the_switch_off_the_cutoff_path_is_as_before_it_lapses_and_there_are_no_auto_rows()
    {
        var (plan, _) = await PlanStoredAsync("auto-off-cutoff", Item(1, TicketGroup.Primary), Item(2, TicketGroup.Reserve));
        await ChangeAsync(on: false);
        Factory.Clock.Pin(ReviewCutoff.AddMinutes(-5));
        var pass = await WithAsync(p => p.GetRequiredService<ICommandHandler<AutoApprovePlanCommand, AutoApprovalOutcome>>().HandleAsync(new AutoApprovePlanCommand(plan), Ct).AsTask());
        Factory.Clock.Pin(ReviewCutoff.AddMinutes(1));

        var cutoff = await WithAsync(p => p.GetRequiredService<ICommandHandler<ApplyReviewCutoffCommand, PlanMoveOutcome>>().HandleAsync(new ApplyReviewCutoffCommand(plan, "system:cutoff"), Ct).AsTask());

        pass.Value.SwitchOn.ShouldBeFalse();
        cutoff.Value.ShouldBe(new PlanMoveOutcome(plan, "lapsed", 2));
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events WHERE to_state = 'approved'")).ShouldBe(0);
    }

    [Fact]
    public async Task With_the_switch_on_a_primary_ticket_is_approved_by_the_rule_with_the_snapshot_and_the_plan_is_then_approved_at_the_cutoff()
    {
        var (plan, items) = await PlanStoredAsync("auto-on", Item(1, TicketGroup.Primary), Item(2, TicketGroup.Reserve));
        await ChangeAsync(on: true);
        var settings = await CurrentAsync();

        var status = await AutoAsync(items[0]);

        status.ShouldBe(MoveStatus.Created);
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("SELECT kind, actor, settings_id, settings_hash, policy_version, decision, reason FROM workflow.approvals", connection);
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            (await reader.ReadAsync(Ct)).ShouldBeTrue();
            reader.GetString(0).ShouldBe("auto");
            reader.GetString(1).ShouldBe("rule:autonomy");
            reader.GetInt64(2).ShouldBe(settings.Id!.Value);
            reader.GetString(3).ShouldBe(settings.Hash);
            reader.GetString(4).ShouldBe("auto-approval/2");
            reader.GetString(5).ShouldBe("approve");
            reader.GetString(6).ShouldStartWith(AutoApprovalWording.Marker);
        }

        (await ScalarAsync<string>("SELECT actor FROM trading.plan_events WHERE item_id = @i AND to_state = 'approved'", ("i", items[0]))).ShouldBe("rule:autonomy");
        Factory.Clock.Pin(ReviewCutoff.AddMinutes(1));
        var cutoff = await WithAsync(p => p.GetRequiredService<ICommandHandler<ApplyReviewCutoffCommand, PlanMoveOutcome>>().HandleAsync(new ApplyReviewCutoffCommand(plan, "system:cutoff"), Ct).AsTask());
        cutoff.Value.State.ShouldBe("approved");
    }

    [Fact]
    public async Task The_pass_with_the_switch_on_but_no_checklist_source_wired_approves_nothing()
    {
        var (plan, _) = await PlanStoredAsync("auto-on-null", Item(1, TicketGroup.Primary));
        await ChangeAsync(on: true);

        var pass = await WithAsync(p => p.GetRequiredService<ICommandHandler<AutoApprovePlanCommand, AutoApprovalOutcome>>().HandleAsync(new AutoApprovePlanCommand(plan), Ct).AsTask());

        pass.Value.SwitchOn.ShouldBeTrue();
        pass.Value.Approved.ShouldBe(0);
        pass.Value.Declined.Single().Codes.ShouldContain(AutoDeclineCodes.ChecklistMissing);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
    }

    [Fact]
    public async Task An_auto_approval_against_a_row_that_is_not_the_newest_or_has_the_switch_off_is_refused_by_the_database()
    {
        var (_, items) = await PlanStoredAsync("auto-was-off", Item(1, TicketGroup.Primary), Item(2, TicketGroup.Primary));
        await ChangeAsync(on: true);
        var wasOn = await CurrentAsync();
        await ChangeAsync(on: false);
        var off = await CurrentAsync();

        (await AutoAsync(items[0], wasOn)).ShouldBe(MoveStatus.Refused);
        (await AutoAsync(items[0], off)).ShouldBe(MoveStatus.Refused);
        (await DirectAutoAsync(items[0], sid: wasOn.Id, hash: wasOn.Hash)).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await DirectAutoAsync(items[0], sid: off.Id, hash: off.Hash)).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
    }

    [Fact]
    public async Task An_auto_approval_with_no_settings_row_a_made_up_row_or_a_wrong_hash_is_refused()
    {
        var (_, items) = await PlanStoredAsync("auto-no-row", Item(1, TicketGroup.Primary));

        (await DirectAutoAsync(items[0], sid: 999_999, hash: new string('0', 64))).ShouldBe(PostgresErrorCodes.CheckViolation);

        await ChangeAsync(on: true);
        var s = await CurrentAsync();
        (await DirectAutoAsync(items[0], sid: s.Id, hash: new string('f', 64))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
    }

    [Fact]
    public async Task The_database_refuses_an_auto_approval_with_an_owner_actor_modify_reject_or_edits_and_an_owner_row_with_the_rule_or_settings()
    {
        var (_, items) = await PlanStoredAsync("auto-shapes", Item(1, TicketGroup.Primary));
        await ChangeAsync(on: true);
        var s = await CurrentAsync();

        (await DirectAutoAsync(items[0], actor: "owner:hem")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await DirectAutoAsync(items[0], actor: "rule:other")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await DirectAutoAsync(items[0], decision: "modify", mods: "size 5")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await DirectAutoAsync(items[0], decision: "reject")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await DirectAutoAsync(items[0], mods: "edit")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await DirectAutoAsync(items[0], kind: "owner", actor: "rule:autonomy")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await DirectAutoAsync(items[0], kind: "owner", actor: "owner:hem")).ShouldBe(PostgresErrorCodes.CheckViolation); // an owner row carries no settings snapshot
        (await DirectAutoAsync(items[0], kind: "system", actor: "owner:hem")).ShouldBe(PostgresErrorCodes.CheckViolation);
        s.Id.ShouldNotBeNull();
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
    }

    [Fact]
    public async Task A_reserve_item_is_never_auto_approved()
    {
        var (_, items) = await PlanStoredAsync("auto-reserve", Item(1, TicketGroup.Primary), Item(2, TicketGroup.Reserve));
        await ChangeAsync(on: true);

        (await AutoAsync(items[1])).ShouldBe(MoveStatus.Refused);
        (await DirectAutoAsync(items[1])).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
    }

    [Fact]
    public async Task An_auto_approval_at_or_after_the_cutoff_is_refused_also_by_the_database_clock()
    {
        var (_, items) = await PlanStoredAsync("auto-past", Item(1, TicketGroup.Primary));
        await ChangeAsync(on: true);
        var past = await WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(Plan("auto-past-2", Item(1, TicketGroup.Primary)) with { ForDate = new DateOnly(2026, 10, 13), ReviewCutoff = PastCutoff, Version = 2 }, Ct).AsTask());
        var pastItem = (await WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(past.Id, Ct).AsTask()))[0].Id;

        // The application clock is pinned before this cutoff, so the store goes ahead; the database clock is real time and says the cutoff has passed.
        var status = await AutoAsync(pastItem);

        status.ShouldBe(MoveStatus.AfterCutoff);
        (await DirectAutoAsync(pastItem)).ShouldBe(PostgresErrorCodes.CheckViolation);
        Factory.Clock.Pin(ReviewCutoff);
        (await AutoAsync(items[0])).ShouldBe(MoveStatus.AfterCutoff);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
    }

    [Fact]
    public async Task The_daily_cap_of_the_settings_row_is_enforced_by_the_database()
    {
        var (_, items) = await PlanStoredAsync("auto-cap", Item(1, TicketGroup.Primary), Item(2, TicketGroup.Primary));
        await ChangeAsync(on: true, cap: 1);

        (await AutoAsync(items[0])).ShouldBe(MoveStatus.Created);
        (await AutoAsync(items[1])).ShouldBe(MoveStatus.Refused);
        (await DirectAutoAsync(items[1])).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(1);
        (await WithAsync(p => p.GetRequiredService<IAutoApprovalStore>().CountAutoApprovalsAsync(Session, Ct).AsTask())).ShouldBe(1);
    }

    [Fact]
    public async Task A_ticket_the_owner_modified_is_not_auto_approved_and_the_database_agrees()
    {
        var (_, items) = await PlanStoredAsync("auto-modified", Item(1, TicketGroup.Primary));
        await ChangeAsync(on: true);
        var modified = await WithAsync(p => p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(new MoveTicketCommand(items[0], TicketState.Modified, "owner:me", "smaller", "size 5"), Ct).AsTask());
        modified.IsSuccess.ShouldBeTrue();

        (await AutoAsync(items[0])).ShouldBe(MoveStatus.Stale);

        // The database itself refuses an auto approval for an item that is not (any more) proposed.
        (await DirectAutoAsync(items[0])).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals WHERE kind = 'auto'")).ShouldBe(0);
    }

    [Fact]
    public async Task Of_an_owner_decision_and_an_auto_approval_racing_on_one_ticket_exactly_one_wins()
    {
        var (_, items) = await PlanStoredAsync("auto-race", Item(1, TicketGroup.Primary));
        await ChangeAsync(on: true);

        var tasks = Enumerable.Range(0, 8).Select(i => i % 2 == 0
            ? AutoAsync(items[0])
            : WithAsync(p => p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(new MoveTicketCommand(items[0], TicketState.Approved, "owner:me", "yes"), Ct).AsTask()).ContinueWith(t => t.Result.IsSuccess ? MoveStatus.Created : MoveStatus.Stale, TaskScheduler.Default));
        var results = await Task.WhenAll(tasks);

        results.Count(r => r == MoveStatus.Created).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events WHERE item_id = @i AND to_state = 'approved'", ("i", items[0]))).ShouldBe(1);
    }

    [Fact]
    public async Task A_second_approving_event_for_one_ticket_is_refused_by_the_chain_even_without_the_store()
    {
        var (_, items) = await PlanStoredAsync("auto-dup", Item(1, TicketGroup.Primary));
        await ChangeAsync(on: true);
        (await AutoAsync(items[0])).ShouldBe(MoveStatus.Created);
        (await DirectAutoAsync(items[0])).ShouldBe(PostgresErrorCodes.CheckViolation); // the approval trigger refuses an item that is no longer proposed ...

        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        var state = await DuplicateEventAsync(connection, transaction, items[0]);

        state.ShouldBe(PostgresErrorCodes.UniqueViolation); // ... and a second move out of proposed cannot be written either
    }

    private static async Task<string?> DuplicateEventAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long item)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, approval_id, prev_event_id, created_at)
            SELECT i.plan_id, i.id, 'proposed', 'approved', 'rule:autonomy', 'r', (SELECT max(id) FROM workflow.approvals), NULL, now() FROM trading.plan_items i WHERE i.id = @i
            """, connection, transaction);
        command.Parameters.AddWithValue("i", item);
        try
        {
            await command.ExecuteNonQueryAsync(Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    [Fact]
    public async Task A_settings_row_gets_its_time_and_hash_from_the_database_and_only_an_owner_actor_is_accepted()
    {
        var change = await ChangeAsync(on: true);
        var s = await CurrentAsync();

        s.Hash.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("autonomy:v1|auto=true|reserve=false|min_p_win=0.580|max_per_day=3"))));
        change.Value.History.Single().Actor.ShouldBe("owner:test@example.com");
        (await SqlStateAsync("INSERT INTO workflow.autonomy_settings (auto_approve_tickets, promote_from_reserve, min_p_win, max_auto_per_day, snapshot_hash, actor, reason, changed_at) VALUES (true, false, 0.58, 3, 'chosen', 'rule:autonomy', 'r', '2000-01-01')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync("INSERT INTO workflow.autonomy_settings (auto_approve_tickets, promote_from_reserve, min_p_win, max_auto_per_day, snapshot_hash, actor, reason, changed_at) VALUES (true, false, 0.3, 3, 'chosen', 'owner:a', 'r', now())")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync("INSERT INTO workflow.autonomy_settings (auto_approve_tickets, promote_from_reserve, min_p_win, max_auto_per_day, snapshot_hash, actor, reason, changed_at) VALUES (false, false, 0.58, 3, 'chosen', 'owner:a', 'r', '2000-01-01')")).ShouldBeNull();
        var newest = await CurrentAsync();
        newest.AutoApproveTickets.ShouldBeFalse();
        newest.Hash.ShouldNotBe("chosen");
        newest.Hash!.Length.ShouldBe(64);
        (await ScalarAsync<bool>("SELECT changed_at > '2020-01-01' FROM workflow.autonomy_settings WHERE id = @i", ("i", newest.Id!.Value))).ShouldBeTrue();
    }

    private static object Body(bool on, string? password, double min = 0.6, string reason = "go") =>
        new { autoApproveTickets = on, promoteFromReserve = false, minPWin = min, maxAutoPerDay = 2, reason, password, actor = "owner:someone-else" };

    private static readonly Uri SettingsUri = new("/api/autonomy/settings", UriKind.Relative);

    /// <summary>The form of the switches is off by default (spec P8-19, ADR 0130): the owner's switch is a row of the capability log, written here as the stamped insert the endpoint of the capabilities makes.</summary>
    private async Task FormOnAsync()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES ('autonomy_form', true, 'test', 'owner:test@example.test')");
    }

    [Fact]
    public async Task Every_change_needs_the_password_again_also_inside_an_open_step_up_window_and_the_actor_is_the_session()
    {
        await FormOnAsync();
        var elevated = await Factory.SignInElevatedAsync(Ct);

        using var none = await elevated.Client.PutAsJsonAsync(SettingsUri, Body(true, null), Ct);
        using var wrong = await elevated.Client.PutAsJsonAsync(SettingsUri, Body(true, "not-the-password"), Ct);
        using var ok = await elevated.Client.PutAsJsonAsync(SettingsUri, Body(true, Factory.OwnerPassword), Ct);
        using var second = await elevated.Client.PutAsJsonAsync(SettingsUri, Body(false, null), Ct);
        using var third = await elevated.Client.PutAsJsonAsync(SettingsUri, Body(false, Factory.OwnerPassword), Ct);

        none.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        third.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await ok.Content.ReadFromJsonAsync<JsonElement>(Ct);
        view.GetProperty("autoApproveTickets").GetBoolean().ShouldBeTrue();
        view.GetProperty("minPWin").GetDouble().ShouldBe(0.6);
        var actor = view.GetProperty("history")[0].GetProperty("actor").GetString();
        actor.ShouldStartWith("owner:");
        actor.ShouldNotContain("someone-else");
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.autonomy_settings")).ShouldBe(2);
    }

    [Fact]
    public async Task A_signed_in_session_that_never_stepped_up_can_change_with_the_password_and_the_change_does_not_open_the_step_up_window()
    {
        await FormOnAsync();
        var plainSession = await Factory.SignInAsync(Ct);
        using var probeBefore = await plainSession.Client.PostAsJsonAsync(new Uri("/api/risk-profiles", UriKind.Relative), new { }, Ct);

        using var changed = await plainSession.Client.PutAsJsonAsync(SettingsUri, Body(true, Factory.OwnerPassword), Ct);
        using var probeAfter = await plainSession.Client.PostAsJsonAsync(new Uri("/api/risk-profiles", UriKind.Relative), new { }, Ct);

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        probeBefore.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        probeAfter.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_open_step_up_window_is_neither_closed_nor_extended_by_a_change()
    {
        await FormOnAsync();
        var elevated = await Factory.SignInElevatedAsync(Ct);
        var before = await ScalarAsync<DateTime>("SELECT elevated_until FROM auth.sessions WHERE elevated_until IS NOT NULL ORDER BY elevated_until DESC LIMIT 1");

        using var changed = await elevated.Client.PutAsJsonAsync(SettingsUri, Body(true, Factory.OwnerPassword), Ct);
        using var probe = await elevated.Client.PostAsJsonAsync(new Uri("/api/risk-profiles", UriKind.Relative), new { }, Ct);

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        probe.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden); // still elevated for the other sensitive actions
        (await ScalarAsync<DateTime>("SELECT elevated_until FROM auth.sessions WHERE elevated_until IS NOT NULL ORDER BY elevated_until DESC LIMIT 1")).ShouldBe(before);
    }

    [Fact]
    public async Task A_bad_value_or_a_blank_reason_with_the_right_password_is_a_bad_request()
    {
        await FormOnAsync();
        var signedIn = await Factory.SignInAsync(Ct);

        using var bad = await signedIn.Client.PutAsJsonAsync(SettingsUri, Body(true, Factory.OwnerPassword, min: 0.1), Ct);
        using var noReason = await signedIn.Client.PutAsJsonAsync(SettingsUri, Body(true, Factory.OwnerPassword, reason: " "), Ct);

        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.autonomy_settings")).ShouldBe(0);
    }

    [Fact]
    public async Task Without_a_session_the_settings_cannot_be_read_or_changed()
    {
        using var read = await Factory.CreateAnonymousClient().GetAsync(new Uri("/api/autonomy/settings", UriKind.Relative), Ct);
        using var write = await Factory.CreateAnonymousClient().PutAsJsonAsync(new Uri("/api/autonomy/settings", UriKind.Relative), new { autoApproveTickets = true, promoteFromReserve = false, minPWin = 0.6, maxAutoPerDay = 2, reason = "go", password = "x" }, Ct);

        read.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        write.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_newest_row_is_the_highest_id_so_an_off_row_with_an_earlier_stamp_still_wins()
    {
        var (_, items) = await PlanStoredAsync("auto-clock", Item(1, TicketGroup.Primary));
        await ChangeAsync(on: true);
        var wasOn = await CurrentAsync();

        // Simulates a clock that stepped back: the later OFF row carries a stamp from the year 2000 (written with the triggers off, as the owner role could).
        await using (var connection = await OpenAsync())
        {
            await using var transaction = await connection.BeginTransactionAsync(Ct);
            await using var set = new NpgsqlCommand("SET LOCAL session_replication_role = replica", connection, transaction);
            await set.ExecuteNonQueryAsync(Ct);
            await using var insert = new NpgsqlCommand("INSERT INTO workflow.autonomy_settings (auto_approve_tickets, promote_from_reserve, min_p_win, max_auto_per_day, snapshot_hash, actor, reason, changed_at) VALUES (false, false, 0.58, 3, repeat('e', 64), 'owner:a', 'r', '2000-01-01')", connection, transaction);
            await insert.ExecuteNonQueryAsync(Ct);
            await transaction.CommitAsync(Ct);
        }

        var current = await CurrentAsync();

        current.AutoApproveTickets.ShouldBeFalse();
        current.Id!.Value.ShouldBeGreaterThan(wasOn.Id!.Value);
        (await AutoAsync(items[0], wasOn)).ShouldBe(MoveStatus.Refused);
        (await DirectAutoAsync(items[0], sid: wasOn.Id, hash: wasOn.Hash)).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
    }

    private async Task WaitForAdvisoryWaiterAsync()
    {
        // Polls the lock table (bounded) until a backend waits for an advisory lock.
        for (var i = 0; i < 200; i++)
        {
            if (await ScalarAsync<long>("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted") > 0)
            {
                return;
            }

            await Task.Delay(25, Ct);
        }

        throw new InvalidOperationException("Nobody waited for the advisory lock.");
    }

    [Fact]
    public async Task A_settings_change_to_off_waits_for_an_auto_approval_in_flight_and_then_both_stand_in_order()
    {
        var (_, items) = await PlanStoredAsync("auto-lock-a", Item(1, TicketGroup.Primary));
        await ChangeAsync(on: true);
        var on = await CurrentAsync();
        await using var holder = await OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync(Ct);
        await using (var insert = new NpgsqlCommand(InsertAuto, holder, transaction))
        {
            insert.Parameters.AddWithValue("item", items[0]);
            insert.Parameters.AddWithValue("kind", "auto");
            insert.Parameters.AddWithValue("decision", "approve");
            insert.Parameters.AddWithValue("actor", "rule:autonomy");
            insert.Parameters.AddWithValue("sid", on.Id!.Value);
            insert.Parameters.AddWithValue("sh", on.Hash!);
            insert.Parameters.AddWithValue("pv", "v");
            insert.Parameters.AddWithValue("mods", DBNull.Value);
            await insert.ExecuteNonQueryAsync(Ct); // accepted, not yet committed: holds the shared lock
        }

        var off = ChangeAsync(on: false);
        await WaitForAdvisoryWaiterAsync();
        off.IsCompleted.ShouldBeFalse();
        await transaction.CommitAsync(Ct);
        var result = await off;

        result.IsSuccess.ShouldBeTrue();
        (await CurrentAsync()).AutoApproveTickets.ShouldBeFalse();
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals WHERE kind = 'auto'")).ShouldBe(1);
    }

    [Fact]
    public async Task An_auto_approval_waits_for_a_settings_change_in_flight_then_sees_off_and_is_refused()
    {
        var (_, items) = await PlanStoredAsync("auto-lock-b", Item(1, TicketGroup.Primary));
        await ChangeAsync(on: true);
        var on = await CurrentAsync();
        await using var holder = await OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync(Ct);
        await using (var insert = new NpgsqlCommand("INSERT INTO workflow.autonomy_settings (auto_approve_tickets, promote_from_reserve, min_p_win, max_auto_per_day, snapshot_hash, actor, reason, changed_at) VALUES (false, false, 0.58, 3, 'x', 'owner:a', 'off', now())", holder, transaction))
        {
            await insert.ExecuteNonQueryAsync(Ct); // holds the exclusive lock, not yet committed
        }

        var auto = AutoAsync(items[0], on);
        await WaitForAdvisoryWaiterAsync();
        auto.IsCompleted.ShouldBeFalse();
        await transaction.CommitAsync(Ct);
        var status = await auto;

        status.ShouldBe(MoveStatus.Refused);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
    }

    [Fact]
    public async Task An_item_of_another_plan_a_draft_plan_and_a_closed_plan_are_refused()
    {
        var (_, items) = await PlanStoredAsync("auto-plan-a", Item(1, TicketGroup.Primary));
        var (otherPlan, _) = await PlanStoredAsync("auto-plan-b", Item(1, TicketGroup.Primary));
        await ChangeAsync(on: true);
        var s = await CurrentAsync();
        const string Direct = "INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, cutoff, decided_at, settings_id, settings_hash, policy_version) SELECT p.id, @item, 'auto', 'approve', 'rule:autonomy', 'r', p.review_cutoff, now(), @sid, @sh, 'v' FROM trading.plans p WHERE p.id = @plan";

        // the item belongs to plan A, the approval names plan B
        (await SqlStateAsync(Direct, ("item", items[0]), ("plan", otherPlan), ("sid", s.Id!.Value), ("sh", s.Hash!))).ShouldNotBeNull();

        // a draft plan: a plan row and an item with no state row at all
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at) VALUES ('auto-draft', '2099-01-07', '2099-01-07 03:35:00+00', 1, repeat('d', 64), 'b', true, 'n', 'l', 'probe', now())");
            await ExecuteAsync(connection, "INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable) SELECT id, 1, 'primary', 's', 'BUY', 'x', 'h', 't', 's', 't', 'v', '{}', 'c', 1, 'd', 'b', '{}' FROM trading.plans WHERE plan_key = 'auto-draft'");
        }

        var draftPlan = await ScalarAsync<long>("SELECT id FROM trading.plans WHERE plan_key = 'auto-draft'");
        var draftItem = await ScalarAsync<long>("SELECT id FROM trading.plan_items WHERE plan_id = @p", ("p", draftPlan));
        (await SqlStateAsync(Direct, ("item", draftItem), ("plan", draftPlan), ("sid", s.Id!.Value), ("sh", s.Hash!))).ShouldBe(PostgresErrorCodes.CheckViolation);

        // a plan that is no longer open: its items lapsed at the cutoff
        Factory.Clock.Pin(ReviewCutoff.AddMinutes(1));
        var cutoff = await WithAsync(p => p.GetRequiredService<ICommandHandler<ApplyReviewCutoffCommand, PlanMoveOutcome>>().HandleAsync(new ApplyReviewCutoffCommand(otherPlan, "system:cutoff"), Ct).AsTask());
        cutoff.Value.State.ShouldBe("lapsed");
        Factory.Clock.Pin(ReviewCutoff.AddMinutes(-5));
        var otherItem = await ScalarAsync<long>("SELECT id FROM trading.plan_items WHERE plan_id = @p", ("p", otherPlan));
        (await AutoAsync(otherItem)).ShouldNotBe(MoveStatus.Created);
        (await SqlStateAsync(Direct, ("item", otherItem), ("plan", otherPlan), ("sid", s.Id!.Value), ("sh", s.Hash!))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
    }

    [Fact]
    public async Task With_the_switch_on_but_nothing_qualifying_the_cutoff_still_lapses_everything()
    {
        var (plan, _) = await PlanStoredAsync("auto-on-lapse", Item(1, TicketGroup.Primary), Item(2, TicketGroup.Reserve));
        await ChangeAsync(on: true);
        Factory.Clock.Pin(ReviewCutoff.AddMinutes(-5));
        await WithAsync(p => p.GetRequiredService<ICommandHandler<AutoApprovePlanCommand, AutoApprovalOutcome>>().HandleAsync(new AutoApprovePlanCommand(plan), Ct).AsTask());
        Factory.Clock.Pin(ReviewCutoff.AddMinutes(1));

        var cutoff = await WithAsync(p => p.GetRequiredService<ICommandHandler<ApplyReviewCutoffCommand, PlanMoveOutcome>>().HandleAsync(new ApplyReviewCutoffCommand(plan, "system:cutoff"), Ct).AsTask());

        cutoff.Value.ShouldBe(new PlanMoveOutcome(plan, "lapsed", 2));
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
    }

    private const string InsertSetting = "INSERT INTO workflow.autonomy_settings (auto_approve_tickets, promote_from_reserve, min_p_win, max_auto_per_day, snapshot_hash, actor, reason, changed_at) VALUES (true, false, 0.6, 3, 'x', 'owner:t@example.com', 'r', now()) RETURNING id";

    [Fact]
    public async Task A_settings_row_inserted_while_another_transaction_holds_the_lock_gets_a_higher_id_than_that_one()
    {
        await using var a = await OpenAsync();
        await using var tx = await a.BeginTransactionAsync(Ct);
        await using var first = new NpgsqlCommand(InsertSetting, a, tx);
        var idA = (long)(await first.ExecuteScalarAsync(Ct))!; // A holds the settings lock, uncommitted

        var blocked = Task.Run(async () => await ScalarAsync<long>(InsertSetting)); // B waits for A's lock before it draws its id
        await Task.Delay(500, Ct);
        blocked.IsCompleted.ShouldBeFalse();
        await tx.CommitAsync(Ct);

        (await blocked).ShouldBeGreaterThan(idA);
    }

    [Fact]
    public async Task An_explicit_low_id_with_overriding_system_value_is_replaced_by_the_trigger_drawn_id()
    {
        var before = await ScalarAsync<long>(InsertSetting);

        var stored = await ScalarAsync<long>(InsertSetting.Replace("(auto_approve_tickets", "(id, auto_approve_tickets", StringComparison.Ordinal).Replace("VALUES (true", "OVERRIDING SYSTEM VALUE VALUES (1, true", StringComparison.Ordinal));

        stored.ShouldBeGreaterThan(before);
    }
}
