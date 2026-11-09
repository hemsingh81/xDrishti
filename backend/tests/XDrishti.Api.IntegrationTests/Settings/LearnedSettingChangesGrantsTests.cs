using System.Globalization;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Infrastructure.LearnedSettings;

namespace XDrishti.Api.IntegrationTests.Settings;

/// <summary>
/// AU-10 (ADR 0247): what the runtime role <c>xd_app</c> may do to <c>config.learned_setting_changes</c>, and what the database refuses whatever the application does. Runs on the dedicated server of <see cref="ResearchGrantsTests"/>, where the role has the default
/// privileges of the init script. The table is insert-only except the state and decision columns; an automatic decision must be a tightening made by learning and needs <c>auto.enabled</c> stored as true; a decided row is not decided again; nothing is deleted.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LearnedSettingChangesGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string Hash = new('a', 64);

    private static string Proposal(string key, string evidence, string direction = "tighten", string source = "learning", string oldValue = "3", string newValue = "3.3") =>
        $"INSERT INTO config.learned_setting_changes (setting_key, old_value, new_value, direction, source, snapshot, snapshot_hash, evidence_ref, evidence_hash, undo_of) VALUES ('{key}', '{oldValue}', '{newValue}', '{direction}', '{source}', '{{}}', '{Hash}', 'ref', '{evidence}', {(source == "owner" ? "(SELECT min(id) FROM config.learned_setting_changes)" : "NULL")}) RETURNING id";

    private static async Task<string?> SqlStateAsync(string connectionString, string sql)
    {
        try
        {
            await StoreKit.ExecuteAsync(connectionString, sql, Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
    }

    private Task SwitchAsync(bool on) => StoreKit.ExecuteAsync(server.Owner, $"INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('auto.enabled', '{(on ? "true" : "false")}', 'owner:me@x.test', now()) ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value", Ct);

    private static async Task<List<string>> ColumnsAsync(string connectionString, string privilege)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand($"SELECT column_name FROM information_schema.columns WHERE table_schema = 'config' AND table_name = 'learned_setting_changes' AND has_column_privilege('xd_app', 'config.learned_setting_changes', column_name, '{privilege}') ORDER BY 1", connection);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Fact]
    public async Task The_runtime_role_has_no_table_level_insert_update_delete_and_only_the_named_columns()
    {
        foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", false), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
        {
            (await ScalarAsync(server.Owner, $"SELECT CASE WHEN has_table_privilege('xd_app', 'config.learned_setting_changes', '{privilege}') THEN 1 ELSE 0 END")).ShouldBe(expected ? 1 : 0, privilege);
        }

        (await ColumnsAsync(server.Owner, "INSERT")).ShouldBe(["direction", "evidence_hash", "evidence_ref", "new_value", "old_value", "setting_key", "snapshot", "snapshot_hash", "source", "undo_of"]);
        (await ColumnsAsync(server.Owner, "UPDATE")).ShouldBe(["decided_at", "decided_by", "state", "undone_at", "undone_by"]);
    }

    [Fact]
    public async Task A_proposal_is_one_per_key_and_evidence_and_cannot_name_its_own_decision()
    {
        await ScalarAsync(server.App, Proposal("K1", "e1"));

        (await SqlStateAsync(server.App, Proposal("K1", "e1"))).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await SqlStateAsync(server.App, $"INSERT INTO config.learned_setting_changes (setting_key, old_value, new_value, direction, source, snapshot, snapshot_hash, evidence_ref, evidence_hash, state, decided_by, decided_at) VALUES ('K2', '3', '3.3', 'tighten', 'learning', '{{}}', '{Hash}', 'r', 'e', 'applied', 'auto', now())")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.App, Proposal("K3", "e3", newValue: "3"))).ShouldBe(PostgresErrorCodes.CheckViolation); // old = new
        (await SqlStateAsync(server.App, Proposal("K3", "e3", direction: "upwards"))).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task What_was_proposed_cannot_be_edited_and_nothing_is_deleted()
    {
        var id = await ScalarAsync(server.App, Proposal("K4", "e4"));

        foreach (var statement in new[] { $"UPDATE config.learned_setting_changes SET new_value = '9' WHERE id = {id}", $"UPDATE config.learned_setting_changes SET direction = 'loosen' WHERE id = {id}", $"DELETE FROM config.learned_setting_changes WHERE id = {id}", "TRUNCATE config.learned_setting_changes" })
        {
            (await SqlStateAsync(server.App, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }

        // even the owner role cannot rewrite what was proposed (the guard trigger), only the runtime may move the state
        (await SqlStateAsync(server.Owner, $"UPDATE config.learned_setting_changes SET new_value = '9' WHERE id = {id}")).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task An_automatic_decision_needs_the_auto_switch_stored_as_true()
    {
        await SwitchAsync(false);
        var id = await ScalarAsync(server.App, Proposal("K5", "e5"));

        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'applied', decided_by = 'auto' WHERE id = {id}")).ShouldBe(PostgresErrorCodes.CheckViolation);

        await SwitchAsync(true);
        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'applied', decided_by = 'auto' WHERE id = {id}")).ShouldBeNull();
        (await ScalarAsync(server.Owner, $"SELECT count(*) FROM config.learned_setting_changes WHERE id = {id} AND decided_by = 'auto' AND decided_at IS NOT NULL")).ShouldBe(1);
        await SwitchAsync(false);
    }

    [Fact]
    public async Task A_loosening_can_never_be_decided_by_auto_even_with_the_switch_on_only_an_owner_applies_it()
    {
        await SwitchAsync(true);
        var id = await ScalarAsync(server.App, Proposal("K6", "e6", direction: "loosen", oldValue: "3.3", newValue: "3"));

        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'applied', decided_by = 'auto' WHERE id = {id}")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'applied', decided_by = 'someone' WHERE id = {id}")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'applied', decided_by = 'owner:me@x.test' WHERE id = {id}")).ShouldBeNull();
        await SwitchAsync(false);
    }

    [Fact]
    public async Task An_owner_undo_row_is_never_decided_by_auto_and_a_dismissal_is_never_auto()
    {
        await SwitchAsync(true);
        var first = await ScalarAsync(server.App, Proposal("K7", "e7"));
        var dismissable = await ScalarAsync(server.App, Proposal("K8", "e8"));
        var undo = await ScalarAsync(server.App, Proposal("K7", "undo:" + first, direction: "tighten", source: "owner"));

        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'applied', decided_by = 'auto' WHERE id = {undo}")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'dismissed', decided_by = 'auto' WHERE id = {dismissable}")).ShouldBe(PostgresErrorCodes.CheckViolation);
        await SwitchAsync(false);
    }

    [Fact]
    public async Task A_decided_row_is_not_decided_again_and_an_undo_is_by_an_owner_and_only_of_an_applied_row()
    {
        var id = await ScalarAsync(server.App, Proposal("K9", "e9"));

        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'undone', undone_by = 'owner:me@x.test' WHERE id = {id}")).ShouldBe(PostgresErrorCodes.CheckViolation); // never applied
        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'applied', decided_by = 'owner:me@x.test' WHERE id = {id}")).ShouldBeNull();
        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'dismissed', decided_by = 'owner:me@x.test' WHERE id = {id}")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'undone', undone_by = 'auto' WHERE id = {id}")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'undone', undone_by = 'owner:me@x.test', decided_by = 'owner:other@x.test' WHERE id = {id}")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'undone', undone_by = 'owner:me@x.test' WHERE id = {id}")).ShouldBeNull();
        (await SqlStateAsync(server.App, $"UPDATE config.learned_setting_changes SET state = 'applied' WHERE id = {id}")).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_store_applies_automatically_only_with_the_switch_on_and_undoes_in_one_step()
    {
        await using var source = NpgsqlDataSource.Create(server.App);
        var store = new LearnedSettingChanges(source);
        var change = new NewLearnedChange("Confidence:CostStress:TierABps", "3", "3.3", "tighten", "learning", "{}", Hash, "paper fills", "store-e1", null);
        await SwitchAsync(false);
        var id = (await store.InsertProposedAsync(change, Ct))!.Value;
        (await store.InsertProposedAsync(change, Ct)).ShouldBeNull("once per evidence");

        (await store.ApplyAsync(id, "auto", null, Ct)).ShouldBeFalse("the switch is off: the database refuses");
        await SwitchAsync(true);
        (await store.ApplyAsync(id, "auto", null, Ct)).ShouldBeTrue();
        await SwitchAsync(false);

        (await store.AppliedValuesAsync(Ct))["Confidence:CostStress:TierABps"].ShouldBe("3.3");
        (await store.AutoAppliedSinceAsync(DateTimeOffset.UtcNow.AddDays(-1), Ct)).ShouldBeGreaterThanOrEqualTo(1);
        store.ReadApplied()!.ShouldContain(o => o.Key == "Confidence:CostStress:TierABps" && o.Value == "3.3" && o.DecidedBy == "auto");

        var undo = new NewLearnedChange("Confidence:CostStress:TierABps", "3.3", "3", "loosen", "owner", "{}", Hash, "undo of change " + id, "undo:" + id, id);
        var undoId = (await store.UndoAsync(id, undo, "owner:me@x.test", Ct))!.Value;

        (await store.FindAsync(id, Ct))!.State.ShouldBe("undone");
        var undone = (await store.FindAsync(undoId, Ct))!;
        (undone.State, undone.DecidedBy, undone.Direction).ShouldBe(("applied", "owner:me@x.test", "loosen"));
        (await store.AppliedValuesAsync(Ct))["Confidence:CostStress:TierABps"].ShouldBe("3");
        (await store.UndoAsync(id, undo, "owner:me@x.test", Ct)).ShouldBeNull("already undone");
        (await store.UndoAsync(undoId, undo with { EvidenceHash = "undo:again", UndoOf = undoId, OldValue = "3", NewValue = "3.3", Direction = "tighten" }, "auto", Ct)).ShouldBeNull("an undo is never by auto");
    }

    [Fact]
    public async Task Of_two_proposals_for_one_key_decided_at_once_exactly_one_wins_and_only_the_newest_applied_row_is_undone()
    {
        await using var source = NpgsqlDataSource.Create(server.App);
        var store = new LearnedSettingChanges(source);
        const string key = "Confidence:Decay:Sigmas";
        NewLearnedChange Change(string to, string evidence) => new(key, "1.5", to, "tighten", "learning", "{}", Hash, "ref", evidence, null);
        var a = (await store.InsertProposedAsync(Change("1.4", "race-a"), Ct))!.Value;
        var b = (await store.InsertProposedAsync(Change("1.3", "race-b"), Ct))!.Value;

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => store.ApplyAsync(i % 2 == 0 ? a : b, "owner:me@x.test", null, Ct).AsTask()));

        results.Count(r => r).ShouldBe(1, "decisions for one key are checked and written one at a time");
        var winner = results[0] ? a : b;
        var loser = winner == a ? b : a;
        (await store.FindAsync(loser, Ct))!.State.ShouldBe("proposed");

        // the winner is the newest applied row; a later applied row makes it un-undoable until that one is undone
        var next = (await store.InsertProposedAsync(Change("1.2", "race-c") with { OldValue = (await store.FindAsync(winner, Ct))!.NewValue }, Ct))!.Value;
        (await store.ApplyAsync(next, "owner:me@x.test", (await store.FindAsync(winner, Ct))!.NewValue, Ct)).ShouldBeTrue();
        var undo = new NewLearnedChange(key, "1.3", "1.4", "loosen", "owner", "{}", Hash, "undo", "undo:x", winner);
        (await store.UndoAsync(winner, undo, "owner:me@x.test", Ct)).ShouldBeNull("it is not the newest applied row of its key");
    }

    [Fact]
    public async Task Reading_the_log_at_start_never_throws_when_it_cannot_be_read()
    {
        await using var broken = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(server.App) { Port = 1, Timeout = 2, CommandTimeout = 2 }.ConnectionString);

        new LearnedSettingChanges(broken).ReadApplied().ShouldBeNull();
    }
}
