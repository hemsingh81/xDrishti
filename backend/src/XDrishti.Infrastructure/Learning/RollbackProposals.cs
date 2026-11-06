using System.Data.Common;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary>
/// <c>research.rollback_proposals</c> behind <see cref="IRollbackProposalReader"/> and <see cref="IRollbackProposalStore"/> (spec CF-10 D8, part 10d-1, ADR 0220). Select and insert, and one conditional update of four columns (state, actor, decided time, the switch row of an automatic
/// decision); the database refuses everything else (column grant, a transition trigger, and constraints that stop <c>auto</c> from ever deciding what only an owner may). The time of a row is the database clock.
/// </summary>
internal sealed class RollbackProposals(NpgsqlDataSource dataSource) : IRollbackProposalReader, IRollbackProposalStore
{
    private const string Columns = "id, model_id, trigger, value, limit_value, text, cells::text, previous_champion_id, previous_qualifies, settings_hash, proposed_on, proposed_at, state, actor, decided_at, auto_setting_id";

    public async ValueTask<long?> InsertAsync(NewRollbackProposal proposal, DateOnly day, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO research.rollback_proposals (model_id, trigger, value, limit_value, text, cells, previous_champion_id, previous_qualifies, settings_hash, proposed_on)
            VALUES (@model, @trigger, @value, @limit, @text, @cells, @previous, @qualifies, @hash, @day)
            ON CONFLICT (model_id, trigger, proposed_on) DO NOTHING
            RETURNING id
            """);
        command.Parameters.AddWithValue("model", NpgsqlDbType.Bigint, proposal.ModelId);
        command.Parameters.AddWithValue("trigger", NpgsqlDbType.Varchar, proposal.Trigger);
        command.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Double) { Value = (object?)proposal.Value ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Double) { Value = (object?)proposal.Limit ?? DBNull.Value });
        command.Parameters.AddWithValue("text", NpgsqlDbType.Varchar, proposal.Text);
        command.Parameters.AddWithValue("cells", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(proposal.Cells));
        command.Parameters.Add(new NpgsqlParameter("previous", NpgsqlDbType.Bigint) { Value = (object?)proposal.PreviousChampionId ?? DBNull.Value });
        command.Parameters.AddWithValue("qualifies", NpgsqlDbType.Boolean, proposal.PreviousQualifies);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, proposal.SettingsHash);
        command.Parameters.AddWithValue("day", NpgsqlDbType.Date, day);
        return await command.ExecuteScalarAsync(cancellationToken) as long?;
    }

    public async ValueTask<RollbackDecideResult> DecideAsync(long id, string state, string actor, long? autoSettingId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE research.rollback_proposals SET state = @state, actor = @actor, decided_at = now(), auto_setting_id = @setting WHERE id = @id AND state = 'proposed'");
        command.Parameters.AddWithValue("state", NpgsqlDbType.Varchar, state);
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, actor);
        command.Parameters.Add(new NpgsqlParameter("setting", NpgsqlDbType.Bigint) { Value = (object?)autoSettingId ?? DBNull.Value });
        command.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? RollbackDecideResult.Decided : RollbackDecideResult.NotProposed;
        }
        catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.CheckViolation)
        {
            // the guard trigger or a check of the table: the record refuses this decision (nothing was changed)
            return RollbackDecideResult.Refused;
        }
    }

    public async ValueTask<RollbackProposalRow?> FindAsync(long id, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.rollback_proposals WHERE id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<IReadOnlyList<RollbackProposalRow>> ListAsync(string? state, long? modelId, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"""
            SELECT {Columns} FROM research.rollback_proposals
            WHERE (@state::text IS NULL OR state = @state::text) AND (@model::bigint IS NULL OR model_id = @model::bigint)
            ORDER BY id DESC LIMIT @limit
            """);
        select.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Text) { Value = (object?)state ?? DBNull.Value });
        select.Parameters.Add(new NpgsqlParameter("model", NpgsqlDbType.Bigint) { Value = (object?)modelId ?? DBNull.Value });
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<RollbackProposalRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    private static RollbackProposalRow Read(DbDataReader r) => new(
        r.GetInt64(0),
        new NewRollbackProposal(
            r.GetInt64(1),
            r.GetString(2),
            r.IsDBNull(3) ? null : r.GetDouble(3),
            r.IsDBNull(4) ? null : r.GetDouble(4),
            r.GetString(5),
            JsonSerializer.Deserialize<string[]>(r.GetString(6)) ?? [],
            r.IsDBNull(7) ? null : r.GetInt64(7),
            r.GetBoolean(8),
            r.GetString(9)),
        r.GetFieldValue<DateOnly>(10),
        r.GetFieldValue<DateTimeOffset>(11),
        r.GetString(12),
        r.IsDBNull(13) ? null : r.GetString(13),
        r.IsDBNull(14) ? null : r.GetFieldValue<DateTimeOffset>(14),
        r.IsDBNull(15) ? null : r.GetInt64(15));
}
