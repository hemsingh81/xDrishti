using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary>
/// <c>research.experiments</c> behind <see cref="IExperimentLedger"/> (spec P6-11, ADR 0072): raw SQL, insert and select only (the runtime role has no more). An append takes one global advisory lock, checks the experiment key and recounts the trials inside it, so a race writes once and trial numbers are never shared.
/// </summary>
internal sealed class ExperimentLedger(NpgsqlDataSource dataSource) : IExperimentLedger
{
    // One constant key for every append: the trial count is global, so appends are serialised (a personal system, a few a day).
    private const long AppendLockKey = 7_864_011_072;

    private const string Columns = """
        id, experiment_key, knob_name, knob_kind, baseline_value, candidate_value, baseline_run_key, candidate_run_key, from_date, to_date, hold_out_start, outcome, trials, used_folds, folds_better, folds_needed, required_improvement_r,
        baseline_trades, candidate_trades, pooled_baseline_mean_r, pooled_candidate_mean_r, baseline_total_r, candidate_total_r, expected_live_gap_r, compare_ms, reasons, report_text, parameters, created_at, created_by
        """;

    private const string Light = """
        id, experiment_key, knob_name, knob_kind, baseline_value, candidate_value, baseline_run_key, candidate_run_key, from_date, to_date, hold_out_start, outcome, trials, used_folds, folds_better, folds_needed, required_improvement_r,
        baseline_trades, candidate_trades, pooled_baseline_mean_r, pooled_candidate_mean_r, baseline_total_r, candidate_total_r, expected_live_gap_r, compare_ms, reasons, ''::text AS report_text, ''::text AS parameters, created_at, created_by
        """;

    public async ValueTask<int> CountSinceAsync(DateTimeOffset since, string excludingKey, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT COUNT(*)::int FROM research.experiments WHERE created_at >= @since AND experiment_key <> @key");
        select.Parameters.AddWithValue("since", NpgsqlDbType.TimestampTz, since);
        select.Parameters.AddWithValue("key", NpgsqlDbType.Char, excludingKey);
        return (int)(await select.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask<StoredExperiment?> FindByKeyAsync(string experimentKey, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.experiments WHERE experiment_key = @key");
        select.Parameters.AddWithValue("key", NpgsqlDbType.Char, experimentKey);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<StoredExperiment?> FindAsync(long id, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.experiments WHERE id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<IReadOnlyList<StoredExperiment>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Light} FROM research.experiments ORDER BY id DESC LIMIT @limit");
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<StoredExperiment>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async ValueTask<RecordedExperiment> AppendAsync(ExperimentToStore experiment, int askedTrials, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@k)", connection, transaction))
        {
            advisory.Parameters.AddWithValue("k", NpgsqlDbType.Bigint, AppendLockKey);
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var exists = new NpgsqlCommand("SELECT id FROM research.experiments WHERE experiment_key = @k", connection, transaction))
        {
            exists.Parameters.AddWithValue("k", NpgsqlDbType.Char, experiment.ExperimentKey);
            if (await exists.ExecuteScalarAsync(cancellationToken) is long found)
            {
                return new RecordedExperiment(found, false);
            }
        }

        await using (var count = new NpgsqlCommand("SELECT COUNT(*)::int FROM research.experiments WHERE created_at >= @since AND experiment_key <> @k", connection, transaction))
        {
            count.Parameters.AddWithValue("since", NpgsqlDbType.TimestampTz, since);
            count.Parameters.AddWithValue("k", NpgsqlDbType.Char, experiment.ExperimentKey);
            var required = Math.Max(askedTrials, (int)(await count.ExecuteScalarAsync(cancellationToken))! + 1);
            if (required != experiment.Trials)
            {
                return new RecordedExperiment(0, false, required);
            }
        }

        long id;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO research.experiments (experiment_key, knob_name, knob_kind, baseline_value, candidate_value, baseline_run_key, candidate_run_key, from_date, to_date, hold_out_start, outcome, trials, used_folds, folds_better, folds_needed,
                required_improvement_r, baseline_trades, candidate_trades, pooled_baseline_mean_r, pooled_candidate_mean_r, baseline_total_r, candidate_total_r, expected_live_gap_r, compare_ms, reasons, report_text, parameters, created_at, created_by)
            VALUES (@key, @knob, @kind, @bvalue, @cvalue, @brun, @crun, @from, @to, @hold_out, @outcome, @trials, @used, @better, @needed,
                @required, @btrades, @ctrades, @pbase, @pcand, @btotal, @ctotal, @gap, @ms, @reasons, @report, @parameters, @created_at, @created_by)
            RETURNING id
            """, connection, transaction))
        {
            var p = insert.Parameters;
            p.AddWithValue("key", NpgsqlDbType.Char, experiment.ExperimentKey);
            p.AddWithValue("knob", NpgsqlDbType.Varchar, experiment.Knob.Name);
            p.AddWithValue("kind", NpgsqlDbType.Varchar, experiment.Knob.Kind.ToString());
            p.AddWithValue("bvalue", NpgsqlDbType.Varchar, experiment.Knob.BaselineValue);
            p.AddWithValue("cvalue", NpgsqlDbType.Varchar, experiment.Knob.CandidateValue);
            p.AddWithValue("brun", NpgsqlDbType.Char, experiment.BaselineRunKey);
            p.AddWithValue("crun", NpgsqlDbType.Char, experiment.CandidateRunKey);
            p.AddWithValue("from", NpgsqlDbType.Date, experiment.FromDate);
            p.AddWithValue("to", NpgsqlDbType.Date, experiment.ToDate);
            p.Add(new NpgsqlParameter("hold_out", NpgsqlDbType.Date) { Value = experiment.HoldOutStart is { } h ? h : DBNull.Value });
            p.AddWithValue("outcome", NpgsqlDbType.Varchar, experiment.Outcome.ToString());
            p.AddWithValue("trials", NpgsqlDbType.Integer, experiment.Trials);
            p.AddWithValue("used", NpgsqlDbType.Integer, experiment.UsedFolds);
            p.AddWithValue("better", NpgsqlDbType.Integer, experiment.FoldsBetter);
            p.AddWithValue("needed", NpgsqlDbType.Integer, experiment.FoldsNeeded);
            p.AddWithValue("required", NpgsqlDbType.Double, experiment.RequiredImprovementR);
            p.AddWithValue("btrades", NpgsqlDbType.Integer, experiment.BaselineTrades);
            p.AddWithValue("ctrades", NpgsqlDbType.Integer, experiment.CandidateTrades);
            p.Add(new NpgsqlParameter("pbase", NpgsqlDbType.Double) { Value = experiment.PooledBaselineMeanR is { } b ? b : DBNull.Value });
            p.Add(new NpgsqlParameter("pcand", NpgsqlDbType.Double) { Value = experiment.PooledCandidateMeanR is { } c ? c : DBNull.Value });
            p.AddWithValue("btotal", NpgsqlDbType.Double, experiment.BaselineTotalR);
            p.AddWithValue("ctotal", NpgsqlDbType.Double, experiment.CandidateTotalR);
            p.AddWithValue("gap", NpgsqlDbType.Double, experiment.ExpectedLiveGapR);
            p.AddWithValue("ms", NpgsqlDbType.Bigint, experiment.CompareMs);
            p.AddWithValue("reasons", NpgsqlDbType.Text, experiment.Reasons);
            p.AddWithValue("report", NpgsqlDbType.Text, experiment.ReportText);
            p.AddWithValue("parameters", NpgsqlDbType.Text, experiment.Parameters);
            p.AddWithValue("created_at", NpgsqlDbType.TimestampTz, experiment.CreatedAt);
            p.AddWithValue("created_by", NpgsqlDbType.Varchar, experiment.CreatedBy);
            id = (long)(await insert.ExecuteScalarAsync(cancellationToken))!;
        }

        await transaction.CommitAsync(cancellationToken);
        return new RecordedExperiment(id, true);
    }

    private static StoredExperiment Read(DbDataReader r) => new(
        r.GetInt64(0),
        new ExperimentToStore(
            ExperimentKey: r.GetString(1),
            Knob: new ExperimentKnob(r.GetString(2), Enum.Parse<ExperimentKnobKind>(r.GetString(3)), r.GetString(4), r.GetString(5)),
            BaselineRunKey: r.GetString(6),
            CandidateRunKey: r.GetString(7),
            FromDate: r.GetFieldValue<DateOnly>(8),
            ToDate: r.GetFieldValue<DateOnly>(9),
            HoldOutStart: r.IsDBNull(10) ? null : r.GetFieldValue<DateOnly>(10),
            Outcome: Enum.Parse<ExperimentOutcome>(r.GetString(11)),
            Trials: r.GetInt32(12),
            UsedFolds: r.GetInt32(13),
            FoldsBetter: r.GetInt32(14),
            FoldsNeeded: r.GetInt32(15),
            RequiredImprovementR: r.GetDouble(16),
            BaselineTrades: r.GetInt32(17),
            CandidateTrades: r.GetInt32(18),
            PooledBaselineMeanR: r.IsDBNull(19) ? null : r.GetDouble(19),
            PooledCandidateMeanR: r.IsDBNull(20) ? null : r.GetDouble(20),
            BaselineTotalR: r.GetDouble(21),
            CandidateTotalR: r.GetDouble(22),
            ExpectedLiveGapR: r.GetDouble(23),
            CompareMs: r.GetInt64(24),
            Reasons: r.GetString(25),
            ReportText: r.GetString(26),
            Parameters: r.GetString(27),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(28),
            CreatedBy: r.GetString(29)));
}
