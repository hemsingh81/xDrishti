using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Domain.Execution;
using XDrishti.Execution.Application.Modes;

namespace XDrishti.Execution.Persistence;

/// <summary>The mode trail as the role xd_exec: reads through <c>execution.effective_mode</c> and the newest settings row, inserts system demotions only (the trigger refuses a promotion and anything not lower).</summary>
internal sealed class PgModeTrail(NpgsqlDataSource data) : IModeTrail
{
    public async Task<ModeRecord?> ReadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = data.CreateCommand("SELECT execution.effective_mode(@a), COALESCE((SELECT s.execution_enabled FROM execution.settings s ORDER BY s.id DESC LIMIT 1), false)");
            command.Parameters.AddWithValue("a", accountId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && !reader.IsDBNull(0) ? new ModeRecord(reader.GetString(0), reader.GetBoolean(1)) : null;
        }
        catch (NpgsqlException)
        {
            return null;
        }
    }

    public async Task<bool> DemoteAsync(Guid accountId, LadderMode from, LadderMode to, string code, CancellationToken cancellationToken)
    {
        var snapshot = JsonSerializer.Serialize(new Dictionary<string, object> { ["policy"] = ModeGate.PolicyVersion, ["from"] = ExecutionModes.Name(from), ["to"] = ExecutionModes.Name(to), ["code"] = code, ["by"] = "mode-guard" });
        try
        {
            await using var command = data.CreateCommand(
                "INSERT INTO execution.mode_trail (account_id, from_mode, to_mode, kind, actor, reason, gate_snapshot, gate_hash, effective_from) VALUES (@a, @f, @t, 'demotion', 'system:mode-guard', @r, @s, @h, now())");
            command.Parameters.AddWithValue("a", accountId);
            command.Parameters.AddWithValue("f", ExecutionModes.Name(from));
            command.Parameters.AddWithValue("t", ExecutionModes.Name(to));
            command.Parameters.AddWithValue("r", code);
            command.Parameters.Add(new NpgsqlParameter("s", NpgsqlDbType.Jsonb) { Value = snapshot });
            command.Parameters.Add(new NpgsqlParameter("h", NpgsqlDbType.Char) { Value = ModeGate.HashOf([]) });
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (PostgresException)
        {
            return false;
        }
    }
}
