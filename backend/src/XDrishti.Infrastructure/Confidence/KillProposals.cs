using System.Data.Common;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Confidence;

namespace XDrishti.Infrastructure.Confidence;

/// <summary>
/// <c>confidence.kill_proposals</c> behind <see cref="IKillProposalReader"/> and <see cref="IKillProposalStore"/> (spec CF-12 part 12b, ADR 0219). Select and insert, and one conditional update of four columns (state, actor, decided time, the setting row of an automatic decision);
/// the database refuses everything else (column grant, a transition trigger, and constraints that stop the proposal-only rule from ever being decided by <c>auto</c>). The time of a row is the database clock.
/// </summary>
internal sealed class KillProposals(NpgsqlDataSource dataSource) : IKillProposalReader, IKillProposalStore
{
    private const string Columns = "id, strategy, epoch_id, rule, proposal_only, value, limit_value, trades, text, cells::text, settings_hash, series_trades, proposed_on, proposed_at, state, actor, decided_at, auto_setting_id";

    public async ValueTask<long?> InsertAsync(NewKillProposal proposal, DateOnly day, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO confidence.kill_proposals (strategy, epoch_id, rule, proposal_only, value, limit_value, trades, text, cells, settings_hash, series_trades, proposed_on)
            VALUES (@strategy, @epoch, @rule, @only, @value, @limit, @trades, @text, @cells, @hash, @series, @day)
            ON CONFLICT (strategy, rule, proposed_on) DO NOTHING
            RETURNING id
            """);
        command.Parameters.AddWithValue("strategy", NpgsqlDbType.Varchar, proposal.Strategy);
        command.Parameters.Add(new NpgsqlParameter("epoch", NpgsqlDbType.Bigint) { Value = (object?)proposal.EpochId ?? DBNull.Value });
        command.Parameters.AddWithValue("rule", NpgsqlDbType.Varchar, proposal.Rule);
        command.Parameters.AddWithValue("only", NpgsqlDbType.Boolean, proposal.ProposalOnly);
        command.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Double) { Value = (object?)proposal.Value ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Double) { Value = (object?)proposal.Limit ?? DBNull.Value });
        command.Parameters.AddWithValue("trades", NpgsqlDbType.Integer, proposal.Trades);
        command.Parameters.AddWithValue("text", NpgsqlDbType.Varchar, proposal.Text);
        command.Parameters.AddWithValue("cells", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(proposal.Cells));
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, proposal.SettingsHash);
        command.Parameters.AddWithValue("series", NpgsqlDbType.Integer, proposal.SeriesTrades);
        command.Parameters.AddWithValue("day", NpgsqlDbType.Date, day);
        return await command.ExecuteScalarAsync(cancellationToken) as long?;
    }

    public async ValueTask<KillDecideResult> DecideAsync(long id, string state, string actor, long? autoSettingId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE confidence.kill_proposals SET state = @state, actor = @actor, decided_at = now(), auto_setting_id = @setting WHERE id = @id AND state = 'proposed'");
        command.Parameters.AddWithValue("state", NpgsqlDbType.Varchar, state);
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, actor);
        command.Parameters.Add(new NpgsqlParameter("setting", NpgsqlDbType.Bigint) { Value = (object?)autoSettingId ?? DBNull.Value });
        command.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? KillDecideResult.Decided : KillDecideResult.NotProposed;
        }
        catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.CheckViolation)
        {
            // the guard trigger or a check of the table: the record refuses this decision (nothing was changed)
            return KillDecideResult.Refused;
        }
    }

    public async ValueTask<KillProposalRow?> FindAsync(long id, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM confidence.kill_proposals WHERE id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<IReadOnlyList<KillProposalRow>> ListAsync(string? state, string? strategy, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"""
            SELECT {Columns} FROM confidence.kill_proposals
            WHERE (@state::text IS NULL OR state = @state::text) AND (@strategy::text IS NULL OR strategy = @strategy::text)
            ORDER BY id DESC LIMIT @limit
            """);
        select.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Text) { Value = (object?)state ?? DBNull.Value });
        select.Parameters.Add(new NpgsqlParameter("strategy", NpgsqlDbType.Text) { Value = (object?)strategy ?? DBNull.Value });
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<KillProposalRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async ValueTask<KillProposalRow?> LatestAsync(string strategy, string rule, long? epochId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM confidence.kill_proposals WHERE strategy = @strategy AND rule = @rule AND epoch_id IS NOT DISTINCT FROM @epoch ORDER BY id DESC LIMIT 1");
        select.Parameters.AddWithValue("strategy", NpgsqlDbType.Varchar, strategy);
        select.Parameters.AddWithValue("rule", NpgsqlDbType.Varchar, rule);
        select.Parameters.Add(new NpgsqlParameter("epoch", NpgsqlDbType.Bigint) { Value = (object?)epochId ?? DBNull.Value });
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static KillProposalRow Read(DbDataReader r) => new(
        r.GetInt64(0),
        new NewKillProposal(
            r.GetString(1),
            r.IsDBNull(2) ? null : r.GetInt64(2),
            r.GetString(3),
            r.GetBoolean(4),
            r.IsDBNull(5) ? null : r.GetDouble(5),
            r.IsDBNull(6) ? null : r.GetDouble(6),
            r.GetInt32(7),
            r.GetString(8),
            JsonSerializer.Deserialize<string[]>(r.GetString(9)) ?? [],
            r.GetString(10),
            r.GetInt32(11)),
        r.GetFieldValue<DateOnly>(12),
        r.GetFieldValue<DateTimeOffset>(13),
        r.GetString(14),
        r.IsDBNull(15) ? null : r.GetString(15),
        r.IsDBNull(16) ? null : r.GetFieldValue<DateTimeOffset>(16),
        r.IsDBNull(17) ? null : r.GetInt64(17));
}
