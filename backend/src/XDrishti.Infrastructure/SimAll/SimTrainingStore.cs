using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.SimAll;

namespace XDrishti.Infrastructure.SimAll;

/// <summary>
/// <c>research.sim_training_runs</c> and <c>research.training_sim_cell_stats</c> behind <see cref="ISimTrainingStore"/> (spec AU-13): raw SQL, insert and select only. The cell statistics sit in their own table with a fixed basis, so no reader of <c>research.cell_stats</c> (the research and
/// forward basis) can see them. A record and its cells are written in one transaction under an advisory lock on the input hash; a repeat writes nothing.
/// </summary>
internal sealed class SimTrainingStore(NpgsqlDataSource dataSource) : ISimTrainingStore
{
    private const string Columns = "input_hash, job_id, state, not_evaluable, runs, runs_not_finished, row_count, sample_every, folds_used, folds_total, embargo_sessions, embargo_days, window_from, window_to, hold_out_start, candidate_log_loss, baseline_log_loss, baseline_name, candidate_brier, baseline_brier, verdict, model_id, training_set_id, summary, created_at";

    public async ValueTask<SimTrainingRecord?> FindByJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM research.sim_training_runs WHERE job_id = @job ORDER BY created_at DESC, id DESC LIMIT 1");
        command.Parameters.AddWithValue("job", NpgsqlDbType.Uuid, jobId);
        return await ReadOneAsync(command, cancellationToken);
    }

    public async ValueTask<SimTrainingRecord?> FindAsync(string inputHash, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM research.sim_training_runs WHERE input_hash = @hash");
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, inputHash);
        return await ReadOneAsync(command, cancellationToken);
    }

    public async ValueTask<bool> StoreAsync(SimTrainingRecord record, DateOnly cellsAsOf, IReadOnlyList<TrainingSimCell> cells, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(cells);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        long id;
        await using (var insert = new NpgsqlCommand($"""
            INSERT INTO research.sim_training_runs ({Columns})
            VALUES (@input_hash, @job_id, @state, @not_evaluable, @runs, @runs_not_finished, @row_count, @sample_every, @folds_used, @folds_total, @embargo_sessions, @embargo_days, @window_from, @window_to, @hold_out_start,
                    @candidate_log_loss, @baseline_log_loss, @baseline_name, @candidate_brier, @baseline_brier, @verdict, @model_id, @training_set_id, @summary, @created_at)
            ON CONFLICT (input_hash) DO NOTHING
            RETURNING id
            """, connection, transaction))
        {
            var p = insert.Parameters;
            p.AddWithValue("input_hash", NpgsqlDbType.Char, record.InputHash);
            p.AddWithValue("job_id", NpgsqlDbType.Uuid, record.JobId);
            p.AddWithValue("state", NpgsqlDbType.Varchar, record.State);
            Value(p, "not_evaluable", NpgsqlDbType.Text, record.NotEvaluable);
            p.AddWithValue("runs", NpgsqlDbType.Integer, record.Runs);
            p.AddWithValue("runs_not_finished", NpgsqlDbType.Integer, record.RunsNotFinished);
            p.AddWithValue("row_count", NpgsqlDbType.Integer, record.Rows);
            p.AddWithValue("sample_every", NpgsqlDbType.Integer, record.SampleEvery);
            p.AddWithValue("folds_used", NpgsqlDbType.Integer, record.FoldsUsed);
            p.AddWithValue("folds_total", NpgsqlDbType.Integer, record.FoldsTotal);
            p.AddWithValue("embargo_sessions", NpgsqlDbType.Integer, record.EmbargoSessions);
            p.AddWithValue("embargo_days", NpgsqlDbType.Integer, record.EmbargoDays);
            p.AddWithValue("window_from", NpgsqlDbType.Date, record.WindowFrom);
            p.AddWithValue("window_to", NpgsqlDbType.Date, record.WindowTo);
            p.AddWithValue("hold_out_start", NpgsqlDbType.Date, record.HoldOutStart);
            Value(p, "candidate_log_loss", NpgsqlDbType.Double, record.CandidateLogLoss);
            Value(p, "baseline_log_loss", NpgsqlDbType.Double, record.BaselineLogLoss);
            Value(p, "baseline_name", NpgsqlDbType.Varchar, record.BaselineName);
            Value(p, "candidate_brier", NpgsqlDbType.Double, record.CandidateBrier);
            Value(p, "baseline_brier", NpgsqlDbType.Double, record.BaselineBrier);
            Value(p, "verdict", NpgsqlDbType.Varchar, record.Verdict);
            Value(p, "model_id", NpgsqlDbType.Bigint, record.ModelId);
            Value(p, "training_set_id", NpgsqlDbType.Char, record.TrainingSetId);
            p.AddWithValue("summary", NpgsqlDbType.Text, record.Summary);
            p.AddWithValue("created_at", NpgsqlDbType.TimestampTz, record.CreatedAt);
            if (await insert.ExecuteScalarAsync(cancellationToken) is not long inserted)
            {
                return false;
            }

            id = inserted;
        }

        if (cells.Count > 0)
        {
            await using var import = await connection.BeginBinaryImportAsync("COPY research.training_sim_cell_stats (training_run_id, as_of, cell_key, strategy, timeframe, side, window_code, costed, n, wins, effective_n, win_rate_posterior, win_rate_lower, win_rate_upper, expectancy_r, enough) FROM STDIN (FORMAT BINARY)", cancellationToken);
            foreach (var cell in cells)
            {
                await import.StartRowAsync(cancellationToken);
                await import.WriteAsync(id, NpgsqlDbType.Bigint, cancellationToken);
                await import.WriteAsync(cellsAsOf, NpgsqlDbType.Date, cancellationToken);
                await import.WriteAsync(cell.CellKey, NpgsqlDbType.Text, cancellationToken);
                await import.WriteAsync(cell.Strategy, NpgsqlDbType.Varchar, cancellationToken);
                await import.WriteAsync(cell.Timeframe, NpgsqlDbType.Varchar, cancellationToken);
                await import.WriteAsync(cell.Side, NpgsqlDbType.Varchar, cancellationToken);
                await import.WriteAsync(cell.Window, NpgsqlDbType.Varchar, cancellationToken);
                await import.WriteAsync(cell.Costed, NpgsqlDbType.Boolean, cancellationToken);
                await import.WriteAsync(cell.N, NpgsqlDbType.Integer, cancellationToken);
                await import.WriteAsync(cell.Wins, NpgsqlDbType.Integer, cancellationToken);
                await import.WriteAsync(cell.EffectiveN, NpgsqlDbType.Double, cancellationToken);
                await import.WriteAsync(cell.WinRatePosterior, NpgsqlDbType.Double, cancellationToken);
                await import.WriteAsync(cell.WinRateLower, NpgsqlDbType.Double, cancellationToken);
                await import.WriteAsync(cell.WinRateUpper, NpgsqlDbType.Double, cancellationToken);
                if (cell.ExpectancyR is { } expectancy)
                {
                    await import.WriteAsync(expectancy, NpgsqlDbType.Double, cancellationToken);
                }
                else
                {
                    await import.WriteNullAsync(cancellationToken);
                }

                await import.WriteAsync(cell.Enough, NpgsqlDbType.Boolean, cancellationToken);
            }

            await import.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static void Value<T>(NpgsqlParameterCollection parameters, string name, NpgsqlDbType type, T? value) =>
        parameters.AddWithValue(name, type, value is null ? DBNull.Value : value);

    private static async ValueTask<SimTrainingRecord?> ReadOneAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SimTrainingRecord(
            r.GetString(0), r.GetGuid(1), r.GetString(2), Str(r, 3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7), r.GetInt32(8), r.GetInt32(9), r.GetInt32(10), r.GetInt32(11),
            r.GetFieldValue<DateOnly>(12), r.GetFieldValue<DateOnly>(13), r.GetFieldValue<DateOnly>(14), Dbl(r, 15), Dbl(r, 16), Str(r, 17), Dbl(r, 18), Dbl(r, 19), Str(r, 20),
            r.IsDBNull(21) ? null : r.GetInt64(21), Str(r, 22), r.GetString(23), r.GetFieldValue<DateTimeOffset>(24));
    }

    private static string? Str(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static double? Dbl(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
}
