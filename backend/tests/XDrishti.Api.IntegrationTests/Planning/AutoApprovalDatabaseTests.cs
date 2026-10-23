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
        new(key, Session, ReviewCutoff, 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, items);

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
            ("sid", (object?)(sid ?? s.Id) ?? DBNull.Value), ("sh", (object?)(hash ?? s.Hash) ?? DBNull.Value), ("pv", "auto-approval/1"), ("mods", (object?)mods ?? DBNull.Value));
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
            reader.GetString(4).ShouldBe("auto-approval/1");
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

        // A direct approval row would still be accepted by the approvals table; the event that would use it is what the database refuses (from modified).
        var settings = await CurrentAsync();
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (var insert = new NpgsqlCommand(InsertAuto, connection, transaction))
        {
            insert.Parameters.AddWithValue("item", items[0]);
            insert.Parameters.AddWithValue("kind", "auto");
            insert.Parameters.AddWithValue("decision", "approve");
            insert.Parameters.AddWithValue("actor", "rule:autonomy");
            insert.Parameters.AddWithValue("sid", settings.Id!.Value);
            insert.Parameters.AddWithValue("sh", settings.Hash!);
            insert.Parameters.AddWithValue("pv", "v");
            insert.Parameters.AddWithValue("mods", DBNull.Value);
            await insert.ExecuteNonQueryAsync(Ct);
        }

        var state = await StateOfEventAsync(connection, transaction, items[0]);
        state.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    private static async Task<string?> StateOfEventAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long item)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, approval_id, prev_event_id, created_at)
            SELECT i.plan_id, i.id, 'modified', 'approved', 'rule:autonomy', 'r', (SELECT max(id) FROM workflow.approvals), (SELECT max(id) FROM trading.plan_events e WHERE e.item_id = i.id), now() FROM trading.plan_items i WHERE i.id = @i
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
        (await DirectAutoAsync(items[0])).ShouldBeNull(); // the approvals table alone accepts another row ...

        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        var state = await DuplicateEventAsync(connection, transaction, items[0]);

        state.ShouldBe(PostgresErrorCodes.UniqueViolation); // ... but a second move out of proposed cannot be written
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

    [Fact]
    public async Task The_endpoint_needs_step_up_takes_the_actor_from_the_session_and_every_change_is_a_new_row()
    {
        var plain = await Client.PutAsJsonAsync(new Uri("/api/autonomy/settings", UriKind.Relative), new { autoApproveTickets = true, promoteFromReserve = false, minPWin = 0.58, maxAutoPerDay = 3, reason = "go" }, Ct);
        plain.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/autonomy/settings", UriKind.Relative), Ct)).GetProperty("autoApproveTickets").GetBoolean().ShouldBeFalse();

        var elevated = await Factory.SignInElevatedAsync(Ct);
        var body = new { autoApproveTickets = true, promoteFromReserve = false, minPWin = 0.6, maxAutoPerDay = 2, reason = "go", actor = "owner:someone-else" };
        using var ok = await elevated.Client.PutAsJsonAsync(new Uri("/api/autonomy/settings", UriKind.Relative), body, Ct);
        using var bad = await elevated.Client.PutAsJsonAsync(new Uri("/api/autonomy/settings", UriKind.Relative), new { autoApproveTickets = true, promoteFromReserve = false, minPWin = 0.1, maxAutoPerDay = 2, reason = "go" }, Ct);
        using var noReason = await elevated.Client.PutAsJsonAsync(new Uri("/api/autonomy/settings", UriKind.Relative), new { autoApproveTickets = true, promoteFromReserve = false, minPWin = 0.6, maxAutoPerDay = 2, reason = " " }, Ct);

        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var view = await ok.Content.ReadFromJsonAsync<JsonElement>(Ct);
        view.GetProperty("autoApproveTickets").GetBoolean().ShouldBeTrue();
        view.GetProperty("minPWin").GetDouble().ShouldBe(0.6);
        var actor = view.GetProperty("history")[0].GetProperty("actor").GetString();
        actor.ShouldStartWith("owner:");
        actor.ShouldNotContain("someone-else");
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.autonomy_settings")).ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_session_the_settings_cannot_be_read_or_changed()
    {
        using var read = await Factory.CreateAnonymousClient().GetAsync(new Uri("/api/autonomy/settings", UriKind.Relative), Ct);
        using var write = await Factory.CreateAnonymousClient().PutAsJsonAsync(new Uri("/api/autonomy/settings", UriKind.Relative), new { autoApproveTickets = true, promoteFromReserve = false, minPWin = 0.6, maxAutoPerDay = 2, reason = "go" }, Ct);

        read.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        write.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
