using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Infrastructure.Lab;

/// <summary><c>lab.test_ledger</c> behind <see cref="ITestLedger"/> (spec P5-03, ADR 0059): raw SQL, insert and select only (the runtime role has no more). A strategy text is never read or written here.</summary>
internal sealed class TestLedger(NpgsqlDataSource dataSource) : ITestLedger
{
    public async ValueTask<bool> AppendAsync(LedgerRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO lab.test_ledger (strategy_id, version, strategy_hash, parent_hash, lab_run_id, run_key, universe_label, window_from, window_to, method, created_at)
            VALUES (@strategy_id, @version, @strategy_hash, @parent_hash, @run_id, @run_key, @universe, @from, @to, @method, @now)
            ON CONFLICT (lab_run_id) DO NOTHING
            """);
        command.Parameters.AddWithValue("strategy_id", NpgsqlDbType.Text, row.StrategyId);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, row.Version);
        command.Parameters.AddWithValue("strategy_hash", NpgsqlDbType.Char, row.StrategyHash);
        command.Parameters.AddWithValue("parent_hash", NpgsqlDbType.Char, (object?)row.ParentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Uuid, row.RunId);
        command.Parameters.AddWithValue("run_key", NpgsqlDbType.Char, row.RunKey);
        command.Parameters.AddWithValue("universe", NpgsqlDbType.Text, row.Universe);
        command.Parameters.AddWithValue("from", NpgsqlDbType.Date, row.From);
        command.Parameters.AddWithValue("to", NpgsqlDbType.Date, row.To);
        command.Parameters.AddWithValue("method", NpgsqlDbType.Text, row.Method);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, row.CreatedAt);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<int> CountVariantsAsync(string strategyId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT count(DISTINCT strategy_hash) FROM lab.test_ledger WHERE strategy_id = @id");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async ValueTask<IReadOnlyList<LedgerRow>> ListAsync(string strategyId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT lab_run_id, run_key, strategy_id, version, strategy_hash, parent_hash, universe_label, window_from, window_to, method, created_at
            FROM lab.test_ledger WHERE strategy_id = @id ORDER BY created_at DESC, id DESC
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<LedgerRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LedgerRow(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6), reader.GetFieldValue<DateOnly>(7), reader.GetFieldValue<DateOnly>(8), reader.GetString(9), reader.GetFieldValue<DateTimeOffset>(10)));
        }

        return rows;
    }
}
