using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Infrastructure.Lab;

/// <summary><c>lab.test_ledger</c> behind <see cref="ITestLedger"/> (spec P5-03, ADR 0059): raw SQL, insert and select only (the runtime role has no more). A strategy text is never read or written here.</summary>
internal sealed class TestLedger(NpgsqlDataSource dataSource) : ITestLedger
{
    public async ValueTask<bool> AppendAsync(LedgerRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO lab.test_ledger (strategy_id, version, strategy_hash, parent_hash, lab_run_id, run_key, universe_label, window_from, window_to, method, created_at, trial_kind, extra_trials)
            VALUES (@strategy_id, @version, @strategy_hash, @parent_hash, @run_id, @run_key, @universe, @from, @to, @method, @now, @kind, @extra)
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
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, row.CreatedAt.ToUniversalTime());
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Text, row.TrialKind);
        command.Parameters.AddWithValue("extra", NpgsqlDbType.Integer, row.ExtraTrials);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<int> CountVariantsAsync(string strategyId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT count(DISTINCT strategy_hash) FROM lab.test_ledger WHERE strategy_id = @id AND trial_kind <> 'training_sweep'");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async ValueTask<TrialCounts> CountTrialsAsync(string strategyId, CancellationToken cancellationToken)
    {
        // the hashes of runs and the sweep points that are not also a run's hash add up to the distinct hashes CountVariantsAsync has always counted; re-plan rows share the hash of their run
        await using var command = dataSource.CreateCommand("""
            WITH runs AS (SELECT DISTINCT strategy_hash FROM lab.test_ledger WHERE strategy_id = @id AND trial_kind = 'run' AND method <> 'sweep'),
                 sweeps AS (SELECT DISTINCT strategy_hash FROM lab.test_ledger WHERE strategy_id = @id AND trial_kind = 'run' AND method = 'sweep' AND strategy_hash NOT IN (SELECT strategy_hash FROM runs))
            SELECT (SELECT count(*) FROM runs),
                   (SELECT count(*) FROM sweeps),
                   (SELECT coalesce(sum(extra_trials), 0) FROM lab.test_ledger WHERE strategy_id = @id AND trial_kind <> 'training_sweep'),
                   (SELECT count(*) FROM lab.test_ledger WHERE strategy_id = @id AND trial_kind = 'replan'),
                   (SELECT count(*) FROM lab.test_ledger WHERE strategy_id = @id AND trial_kind = 'null'),
                   (SELECT count(*) FROM lab.test_ledger WHERE strategy_id = @id AND trial_kind = 'training_sweep'),
                   (SELECT coalesce(sum(extra_trials), 0) FROM lab.test_ledger WHERE strategy_id = @id AND trial_kind = 'training_sweep')
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new TrialCounts((int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3), (int)reader.GetInt64(4), (int)reader.GetInt64(5), (int)reader.GetInt64(6));
    }

    public async ValueTask<bool> HasFullRunAsync(string strategyId, string strategyHash, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT 1 FROM lab.test_ledger WHERE strategy_id = @id AND strategy_hash = @hash AND trial_kind = 'run' AND method IN ('full','robust') LIMIT 1");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, strategyHash);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async ValueTask<bool> HasHashAsync(string strategyId, string strategyHash, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT 1 FROM lab.test_ledger WHERE strategy_id = @id AND strategy_hash = @hash AND trial_kind <> 'training_sweep' LIMIT 1");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, strategyHash);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async ValueTask<IReadOnlyList<LedgerRow>> ListAsync(string strategyId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT lab_run_id, run_key, strategy_id, version, strategy_hash, parent_hash, universe_label, window_from, window_to, method, created_at, trial_kind, extra_trials
            FROM lab.test_ledger WHERE strategy_id = @id ORDER BY created_at DESC, id DESC
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<LedgerRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LedgerRow(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6), reader.GetFieldValue<DateOnly>(7), reader.GetFieldValue<DateOnly>(8), reader.GetString(9), reader.GetFieldValue<DateTimeOffset>(10), reader.GetString(11), reader.GetInt32(12)));
        }

        return rows;
    }
}
