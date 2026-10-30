using Npgsql;
using NpgsqlTypes;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Persistence;

/// <summary>
/// The insert-only halt record as the role xd_exec (P12-05). A halt is active while it has neither a <c>halt_releases</c> row (the owner's, written by xd-api) nor a <c>halt_auto_clears</c> row (written here, only for the codes that may clear themselves; the
/// database trigger checks). The record offers no update, delete or release. A read that fails throws; <see cref="HaltService"/> turns that into "unreadable", which blocks entries.
/// </summary>
internal sealed class PgHaltStore(NpgsqlDataSource data) : IHaltStore
{
    private const string Columns = "h.id, h.at, h.scope, h.account_id, h.level, h.trigger_code, h.source, h.actor, h.reason, h.broker_kill_switch";

    private const string Open = "NOT EXISTS (SELECT 1 FROM execution.halt_releases r WHERE r.halt_id = h.id) AND NOT EXISTS (SELECT 1 FROM execution.halt_auto_clears c WHERE c.halt_id = h.id)";

    public async Task<(ActiveHalt Halt, bool Created)> AppendAsync(HaltDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (HaltTriggers.Find(draft.TriggerCode) is null)
        {
            throw new ArgumentException("Unknown trigger code.", nameof(draft));
        }

        // shortcut: the de-dup is "an active halt of this trigger and scope at this level or stronger" then INSERT in one statement, which is not race-safe across two writers (the command poller and the watchdog could both insert; the effect is the same halt twice, never a weaker one);
        // upgrade: a partial unique index on active (trigger, scope, account) once the release tables can be part of one.
        await using var command = data.CreateCommand(
            $"""
            WITH existing AS (
                SELECT {Columns} FROM execution.halts h
                WHERE h.trigger_code = @code AND h.scope = @scope AND h.account_id IS NOT DISTINCT FROM @account AND h.level = ANY(@levels) AND {Open}
                ORDER BY h.id LIMIT 1),
            inserted AS (
                INSERT INTO execution.halts (at, scope, account_id, level, trigger_code, source, actor, reason, broker_kill_switch)
                SELECT @at, @scope, @account, @level, @code, @source, @actor, @reason, @kill WHERE NOT EXISTS (SELECT 1 FROM existing)
                RETURNING id, at, scope, account_id, level, trigger_code, source, actor, reason, broker_kill_switch)
            SELECT *, true FROM inserted UNION ALL SELECT *, false FROM existing
            """);
        command.Parameters.AddWithValue("at", draft.At);
        command.Parameters.AddWithValue("scope", Scope(draft.Scope));
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = (object?)draft.AccountId ?? DBNull.Value });
        command.Parameters.AddWithValue("level", Level(draft.Level));
        command.Parameters.Add(new NpgsqlParameter("levels", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = Enum.GetValues<HaltLevel>().Where(l => l >= draft.Level).Select(Level).ToArray() });
        command.Parameters.AddWithValue("code", draft.TriggerCode);
        command.Parameters.AddWithValue("source", draft.Source);
        command.Parameters.AddWithValue("actor", draft.Actor);
        command.Parameters.AddWithValue("reason", draft.Reason.Length > 300 ? draft.Reason[..300] : draft.Reason);
        command.Parameters.AddWithValue("kill", draft.BrokerKillSwitch);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return (Read(reader), reader.GetBoolean(10));
    }

    public async Task<IReadOnlyList<ActiveHalt>> ActiveAsync(CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand($"SELECT {Columns} FROM execution.halts h WHERE {Open} ORDER BY h.id");
        var rows = new List<ActiveHalt>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async Task<bool> AutoClearAsync(long haltId, string triggerCode, string detail, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (!HaltTriggers.IsAutoClearing(triggerCode))
        {
            return false; // the owner alone ends the other codes; the database refuses it as well
        }

        await using var command = data.CreateCommand(
            $"""
            INSERT INTO execution.halt_auto_clears (halt_id, at, trigger_code, detail)
            SELECT h.id, @at, h.trigger_code, @detail FROM execution.halts h WHERE h.id = @id AND h.trigger_code = @code AND {Open}
            """);
        command.Parameters.AddWithValue("id", haltId);
        command.Parameters.AddWithValue("at", at);
        command.Parameters.AddWithValue("code", triggerCode);
        command.Parameters.AddWithValue("detail", detail.Length > 100 ? detail[..100] : detail);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static ActiveHalt Read(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetFieldValue<DateTimeOffset>(1), r.GetString(2) == "system" ? HaltScope.System : HaltScope.Account, r.IsDBNull(3) ? null : r.GetGuid(3), ParseLevel(r.GetString(4)),
        r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetBoolean(9));

    private static string Scope(HaltScope scope) => scope == HaltScope.System ? "system" : "account";

    internal static string Level(HaltLevel level) => level switch
    {
        HaltLevel.Entries => "entries",
        HaltLevel.Cancel => "cancel",
        HaltLevel.ExitAll => "exit_all",
        _ => "freeze",
    };

    private static HaltLevel ParseLevel(string word) => word switch
    {
        "entries" => HaltLevel.Entries,
        "cancel" => HaltLevel.Cancel,
        "exit_all" => HaltLevel.ExitAll,
        _ => HaltLevel.Freeze, // an unknown word reads as the strongest, never the mildest
    };
}
