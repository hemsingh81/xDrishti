using Npgsql;
using NpgsqlTypes;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Persistence;

/// <summary>The insert-only alarm record (P12-04), as the role xd_exec. A raise is one INSERT that writes nothing when the same alarm (code, account, intent) is already open; a clear is one INSERT per open alarm. Nothing updates or deletes.</summary>
internal sealed class PgAlarmStore(NpgsqlDataSource data) : IAlarmStore
{
    public async Task<bool> RaiseAsync(AlarmDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!AlarmCodes.All.ContainsKey(draft.Code))
        {
            throw new ArgumentException("Unknown alarm code.", nameof(draft));
        }

        // shortcut: the de-dup is NOT EXISTS then INSERT in one statement, which is not race-safe across two writers (single watchdog assumed); upgrade: a partial unique index on open (code, account, intent) once a second writer exists.
        await using var command = data.CreateCommand(
            """
            INSERT INTO execution.alarms (at, code, severity, account_id, intent_id, detail)
            SELECT @at, @code, @severity, @account, @intent, @detail
            WHERE NOT EXISTS (
                SELECT 1 FROM execution.alarms a
                WHERE a.code = @code AND a.account_id IS NOT DISTINCT FROM @account AND a.intent_id IS NOT DISTINCT FROM @intent
                  AND NOT EXISTS (SELECT 1 FROM execution.alarm_clears c WHERE c.alarm_id = a.id))
            """);
        command.Parameters.AddWithValue("at", draft.At);
        command.Parameters.AddWithValue("code", draft.Code);
        command.Parameters.AddWithValue("severity", draft.Severity.ToString().ToLowerInvariant());
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = (object?)draft.AccountId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("intent", NpgsqlDbType.Bigint) { Value = (object?)draft.IntentId ?? DBNull.Value });
        command.Parameters.AddWithValue("detail", draft.Detail.Length > 300 ? draft.Detail[..300] : draft.Detail);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<int> ClearAsync(string code, Guid? accountId, long? intentId, string by, AlarmClearHow how, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand(
            """
            INSERT INTO execution.alarm_clears (alarm_id, at, cleared_by, how)
            SELECT a.id, @at, @by, @how FROM execution.alarms a
            WHERE a.code = @code AND a.account_id IS NOT DISTINCT FROM @account AND a.intent_id IS NOT DISTINCT FROM @intent
              AND NOT EXISTS (SELECT 1 FROM execution.alarm_clears c WHERE c.alarm_id = a.id)
            """);
        command.Parameters.AddWithValue("at", at);
        command.Parameters.AddWithValue("by", by);
        command.Parameters.AddWithValue("how", how.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("code", code);
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = (object?)accountId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("intent", NpgsqlDbType.Bigint) { Value = (object?)intentId ?? DBNull.Value });
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
