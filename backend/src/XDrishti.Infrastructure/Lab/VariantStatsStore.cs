using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Infrastructure.Lab;

/// <summary><c>lab.variant_stats</c> behind <see cref="IVariantStatsStore"/> (CF-03, ADR 0205): raw SQL, insert and select only (the runtime role has no more). A strategy text is never read or written here.</summary>
internal sealed class VariantStatsStore(NpgsqlDataSource dataSource) : IVariantStatsStore
{
    public async ValueTask<int> AppendAsync(IReadOnlyList<VariantStatsRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var written = 0;
        foreach (var row in rows)
        {
            await using var command = dataSource.CreateCommand("""
                INSERT INTO lab.variant_stats (strategy_id, run_id, variant_hash, n, mean_r, sd_r, skew, kurt, sr_per_trade, method, created_at)
                VALUES (@strategy, @run, @hash, @n, @mean, @sd, @skew, @kurt, @sr, @method, @now)
                ON CONFLICT (run_id, variant_hash) DO NOTHING
                """);
            command.Parameters.AddWithValue("strategy", NpgsqlDbType.Text, row.StrategyId);
            command.Parameters.AddWithValue("run", NpgsqlDbType.Uuid, row.RunId);
            command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, row.VariantHash);
            command.Parameters.AddWithValue("n", NpgsqlDbType.Integer, row.N);
            command.Parameters.AddWithValue("mean", NpgsqlDbType.Double, (object?)row.MeanR ?? DBNull.Value);
            command.Parameters.AddWithValue("sd", NpgsqlDbType.Double, (object?)row.SdR ?? DBNull.Value);
            command.Parameters.AddWithValue("skew", NpgsqlDbType.Double, (object?)row.Skew ?? DBNull.Value);
            command.Parameters.AddWithValue("kurt", NpgsqlDbType.Double, (object?)row.Kurt ?? DBNull.Value);
            command.Parameters.AddWithValue("sr", NpgsqlDbType.Double, (object?)row.SrPerTrade ?? DBNull.Value);
            command.Parameters.AddWithValue("method", NpgsqlDbType.Text, row.Method);
            command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, row.CreatedAt.ToUniversalTime());
            written += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return written;
    }

    public async ValueTask<IReadOnlyList<VariantStatsRow>> ListAsync(string strategyId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT DISTINCT ON (variant_hash) strategy_id, run_id, variant_hash, n, mean_r, sd_r, skew, kurt, sr_per_trade, method, created_at
            FROM lab.variant_stats WHERE strategy_id = @id ORDER BY variant_hash, created_at DESC, id DESC
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<VariantStatsRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new VariantStatsRow(
                reader.GetString(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3), Double(reader, 4), Double(reader, 5), Double(reader, 6), Double(reader, 7), Double(reader, 8), reader.GetString(9), reader.GetFieldValue<DateTimeOffset>(10)));
        }

        return rows;
    }

    private static double? Double(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
