using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.trade_features_ext</c> behind <see cref="IExtensionFeatureStore"/> (spec P13-05): raw SQL, insert and select only (the runtime role has no more). Rows are written in one transaction; a row stored already is skipped.</summary>
internal sealed class ExtensionFeatureStore(NpgsqlDataSource dataSource) : IExtensionFeatureStore
{
    public async ValueTask<int> AppendAsync(IReadOnlyList<ExtensionFeatureRow> rows, DateTimeOffset createdAt, CancellationToken cancellationToken)
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
                INSERT INTO research.trade_features_ext (run_key, trade_key, extension, version, decision_at, feature_values, created_at)
                VALUES (@run_key, @trade_key, @extension, @version, @decision_at, @values, @created_at)
                ON CONFLICT (run_key, trade_key, extension, version) DO NOTHING
                """, connection, transaction);
            var p = insert.Parameters;
            p.AddWithValue("run_key", NpgsqlDbType.Char, row.RunKey);
            p.AddWithValue("trade_key", NpgsqlDbType.Text, row.TradeKey);
            p.AddWithValue("extension", NpgsqlDbType.Varchar, row.Extension);
            p.AddWithValue("version", NpgsqlDbType.Varchar, row.Version);
            p.AddWithValue("decision_at", NpgsqlDbType.TimestampTz, row.DecisionAt);
            p.AddWithValue("values", NpgsqlDbType.Array | NpgsqlDbType.Double, row.Values.ToArray());
            p.AddWithValue("created_at", NpgsqlDbType.TimestampTz, createdAt);
            added += await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return added;
    }

    public async ValueTask<IReadOnlyList<ExtensionFeatureRow>> ReadAsync(IReadOnlyList<string> runKeys, string version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runKeys);
        await using var command = dataSource.CreateCommand("""
            SELECT run_key, trade_key, extension, version, decision_at, feature_values FROM research.trade_features_ext
            WHERE version = @version AND run_key = ANY(@keys)
            ORDER BY run_key, trade_key
            """);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Varchar, version);
        command.Parameters.AddWithValue("keys", NpgsqlDbType.Array | NpgsqlDbType.Char, runKeys.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<ExtensionFeatureRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ExtensionFeatureRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), reader.GetFieldValue<double[]>(5)));
        }

        return list;
    }
}
