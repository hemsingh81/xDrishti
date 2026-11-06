using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Confidence;

namespace XDrishti.Infrastructure.Confidence;

/// <summary>
/// <c>readiness.scorecard_snapshots</c> behind <see cref="IReadinessSnapshotStore"/> and <see cref="IReadinessSnapshotReader"/> (spec CF-11 D7, ADR 0221): insert-only. A <c>daily</c> snapshot is unique per subject and India day (a second insert does nothing); a <c>gate</c> snapshot
/// is one per gate use. The database stamps the time and the id; nothing is ever updated or deleted.
/// </summary>
internal sealed class ReadinessSnapshots(NpgsqlDataSource dataSource) : IReadinessSnapshotStore, IReadinessSnapshotReader
{
    public async ValueTask<long?> InsertAsync(NewReadinessSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO readiness.scorecard_snapshots (subject, strategy_id, version, taken_on, scope, score, label, body, settings_hash)
            VALUES (@subject, @strategy, @version, @day, @scope, @score, @label, @body, @hash)
            ON CONFLICT DO NOTHING
            RETURNING id
            """);
        command.Parameters.AddWithValue("subject", NpgsqlDbType.Varchar, snapshot.Subject);
        command.Parameters.Add(new NpgsqlParameter("strategy", NpgsqlDbType.Varchar) { Value = (object?)snapshot.StrategyId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("version", NpgsqlDbType.Integer) { Value = (object?)snapshot.Version ?? DBNull.Value });
        command.Parameters.AddWithValue("day", NpgsqlDbType.Date, snapshot.TakenOn);
        command.Parameters.AddWithValue("scope", NpgsqlDbType.Varchar, snapshot.Scope);
        command.Parameters.AddWithValue("score", NpgsqlDbType.Double, snapshot.Score);
        command.Parameters.AddWithValue("label", NpgsqlDbType.Varchar, snapshot.Label);
        command.Parameters.AddWithValue("body", NpgsqlDbType.Jsonb, snapshot.Body);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, snapshot.SettingsHash);
        return await command.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
    }

    public async ValueTask<IReadOnlyList<ReadinessSnapshotRow>> RecentAsync(string subject, string? strategyId, string? scope, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT id, subject, strategy_id, version, taken_on, taken_at, scope, score, label, settings_hash
            FROM readiness.scorecard_snapshots
            WHERE subject = @subject AND (@strategy::varchar IS NULL OR strategy_id = @strategy) AND (@scope::varchar IS NULL OR scope = @scope)
            ORDER BY id DESC
            LIMIT @limit
            """);
        select.Parameters.AddWithValue("subject", NpgsqlDbType.Varchar, subject);
        select.Parameters.Add(new NpgsqlParameter("strategy", NpgsqlDbType.Varchar) { Value = (object?)strategyId ?? DBNull.Value });
        select.Parameters.Add(new NpgsqlParameter("scope", NpgsqlDbType.Varchar) { Value = (object?)scope ?? DBNull.Value });
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 500));
        var rows = new List<ReadinessSnapshotRow>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ReadinessSnapshotRow(
                reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.GetFieldValue<DateOnly>(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetString(6), reader.GetDouble(7), reader.GetString(8), reader.GetString(9)));
        }

        return rows;
    }
}
