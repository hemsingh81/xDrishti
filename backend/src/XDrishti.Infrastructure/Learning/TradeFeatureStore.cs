using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.trade_features</c> behind <see cref="ITradeFeatureStore"/> (spec P6-04, ADR 0066): raw SQL, insert and select only (the runtime role has no more). Rows are written in one transaction; a row that is stored already is skipped.</summary>
internal sealed class TradeFeatureStore(NpgsqlDataSource dataSource) : ITradeFeatureStore
{
    public async ValueTask<int> StoreAsync(IReadOnlyList<TradeFeatureRow> rows, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return 0;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var added = 0;
        // Shortcut: one INSERT per row in a single transaction is fine below ~100k rows; beyond that COPY into a temp table and INSERT .. SELECT .. ON CONFLICT DO NOTHING.
        foreach (var row in rows)
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO research.trade_features (run_key, trade_key, feature_version, strategy, timeframe, side, trade_date, decision_epoch_seconds, feature_values, created_at)
                VALUES (@run_key, @trade_key, @version, @strategy, @timeframe, @side, @trade_date, @decision, @values, @created_at)
                ON CONFLICT (run_key, trade_key, feature_version) DO NOTHING
                """, connection, transaction);
            var p = insert.Parameters;
            p.AddWithValue("run_key", NpgsqlDbType.Char, row.RunKey);
            p.AddWithValue("trade_key", NpgsqlDbType.Text, row.TradeKey);
            p.AddWithValue("version", NpgsqlDbType.Varchar, TradeFeatureCatalog.Version);
            p.AddWithValue("strategy", NpgsqlDbType.Varchar, row.Strategy);
            p.AddWithValue("timeframe", NpgsqlDbType.Varchar, row.Timeframe);
            p.AddWithValue("side", NpgsqlDbType.Varchar, row.Side);
            p.AddWithValue("trade_date", NpgsqlDbType.Date, row.TradeDate);
            p.AddWithValue("decision", NpgsqlDbType.Bigint, row.DecisionEpochSeconds);
            p.AddWithValue("values", NpgsqlDbType.Array | NpgsqlDbType.Double, row.Values.ToArray());
            p.AddWithValue("created_at", NpgsqlDbType.TimestampTz, createdAt);
            added += await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return added;
    }

    public async ValueTask<IReadOnlyList<TradeFeatureRow>> ReadAsync(IReadOnlyList<string> runKeys, string featureVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runKeys);
        await using var command = dataSource.CreateCommand("""
            SELECT run_key, trade_key, strategy, timeframe, side, trade_date, decision_epoch_seconds, feature_values FROM research.trade_features
            WHERE feature_version = @version AND run_key = ANY(@keys)
            ORDER BY run_key, trade_key
            """);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Varchar, featureVersion);
        command.Parameters.AddWithValue("keys", NpgsqlDbType.Array | NpgsqlDbType.Char, runKeys.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<TradeFeatureRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new TradeFeatureRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateOnly>(5), reader.GetInt64(6), reader.GetFieldValue<double[]>(7)));
        }

        return list;
    }
}
