using System.Data.Common;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary>
/// <c>research.model_versions</c> and <c>research.model_designations</c> behind <see cref="IModelRegistry"/> (spec P6-07, ADR 0070): raw SQL, insert and select only (the runtime role has no more). A registration is written with its first designation
/// (challenger) in one transaction; a model's role is its latest designation.
/// </summary>
internal sealed class ModelRegistry(NpgsqlDataSource dataSource) : IModelRegistry
{
    // The distinct models (by file hash) of a feature version registered up to each row: the trials a champion's verdict must allow for (ChampionRule).
    private const string RequiredTrials = """
        WITH firsts AS (
            SELECT id, feature_version, (ROW_NUMBER() OVER (PARTITION BY feature_version, sha256 ORDER BY id) = 1)::int AS first FROM research.model_versions),
        req AS (SELECT id, (SUM(first) OVER (PARTITION BY feature_version ORDER BY id))::int AS required_trials FROM firsts)
        """;

    // Columns 0 to 32 are the registration; 33 is the role and 34 the required trials. A list leaves the heavy ones empty.
    private const string Head = """
        v.id, v.version_key, v.sha256, v.artifact_bytes, v.trainer, v.note, v.training_set_id, v.feature_version, v.feature_width, v.fit_rows, v.calibration_rows, v.train_rows, v.from_date, v.to_date, v.hold_out_start, v.verdict, v.used_folds,
        v.folds_beating_both, v.folds_needed, v.pooled_beats_both, v.pooled_ece_pp, v.pooled_log_loss, v.pooled_brier, v.reasons,
        """;

    private const string Full = Head + """
         v.report_text, v.parameters, v.calibration_scores, v.calibration_values, v.importance, v.trials, v.purged_rows, v.created_at, v.created_by
        """;

    private const string Light = Head + """
         ''::text AS report_text, ''::text AS parameters, ARRAY[]::double precision[] AS calibration_scores, ARRAY[]::double precision[] AS calibration_values, ARRAY[]::double precision[] AS importance, v.trials, v.purged_rows, v.created_at, v.created_by
        """;

    public async ValueTask<RegisteredModel> RegisterAsync(ModelToRegister model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@k))", connection, transaction))
        {
            advisory.Parameters.AddWithValue("k", NpgsqlDbType.Text, model.VersionKey);
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var exists = new NpgsqlCommand("SELECT id FROM research.model_versions WHERE version_key = @k", connection, transaction))
        {
            exists.Parameters.AddWithValue("k", NpgsqlDbType.Char, model.VersionKey);
            if (await exists.ExecuteScalarAsync(cancellationToken) is long found)
            {
                return new RegisteredModel(found, false);
            }
        }

        long id;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO research.model_versions (version_key, sha256, artifact_bytes, trainer, note, training_set_id, feature_version, feature_width, fit_rows, calibration_rows, train_rows, from_date, to_date, hold_out_start, verdict, used_folds,
                folds_beating_both, folds_needed, pooled_beats_both, pooled_ece_pp, pooled_log_loss, pooled_brier, reasons, report_text, parameters, calibration_scores, calibration_values, importance, trials, purged_rows, created_at, created_by)
            VALUES (@version_key, @sha256, @bytes, @trainer, @note, @set, @feature_version, @width, @fit, @calibration, @train, @from, @to, @hold_out, @verdict, @used,
                @beating, @needed, @pooled_beats, @ece, @log_loss, @brier, @reasons, @report, @parameters, @cal_scores, @cal_values, @importance, @trials, @purged, @created_at, @created_by)
            RETURNING id
            """, connection, transaction))
        {
            var p = insert.Parameters;
            p.AddWithValue("version_key", NpgsqlDbType.Char, model.VersionKey);
            p.AddWithValue("sha256", NpgsqlDbType.Char, model.Sha256);
            p.AddWithValue("bytes", NpgsqlDbType.Integer, model.ArtifactBytes);
            p.AddWithValue("trainer", NpgsqlDbType.Varchar, model.Trainer);
            p.Add(new NpgsqlParameter("note", NpgsqlDbType.Text) { Value = (object?)model.Note ?? DBNull.Value });
            p.AddWithValue("set", NpgsqlDbType.Char, model.TrainingSetId);
            p.AddWithValue("feature_version", NpgsqlDbType.Varchar, model.FeatureVersion);
            p.AddWithValue("width", NpgsqlDbType.Integer, model.FeatureWidth);
            p.AddWithValue("fit", NpgsqlDbType.Integer, model.FitRows);
            p.AddWithValue("calibration", NpgsqlDbType.Integer, model.CalibrationRows);
            p.AddWithValue("train", NpgsqlDbType.Integer, model.TrainRows);
            p.AddWithValue("from", NpgsqlDbType.Date, model.FromDate);
            p.AddWithValue("to", NpgsqlDbType.Date, model.ToDate);
            p.Add(new NpgsqlParameter("hold_out", NpgsqlDbType.Date) { Value = model.HoldOutStart is { } h ? h : DBNull.Value });
            p.AddWithValue("verdict", NpgsqlDbType.Varchar, model.Verdict.ToString());
            p.AddWithValue("used", NpgsqlDbType.Integer, model.UsedFolds);
            p.AddWithValue("beating", NpgsqlDbType.Integer, model.FoldsBeatingBoth);
            p.AddWithValue("needed", NpgsqlDbType.Integer, model.FoldsNeeded);
            p.AddWithValue("pooled_beats", NpgsqlDbType.Boolean, model.PooledBeatsBoth);
            p.AddWithValue("ece", NpgsqlDbType.Double, model.PooledEcePp);
            p.AddWithValue("log_loss", NpgsqlDbType.Double, model.PooledLogLoss);
            p.AddWithValue("brier", NpgsqlDbType.Double, model.PooledBrier);
            p.AddWithValue("reasons", NpgsqlDbType.Text, model.Reasons);
            p.AddWithValue("report", NpgsqlDbType.Text, model.ReportText);
            p.AddWithValue("parameters", NpgsqlDbType.Text, model.Parameters);
            p.AddWithValue("cal_scores", NpgsqlDbType.Array | NpgsqlDbType.Double, model.CalibrationScores.ToArray());
            p.AddWithValue("cal_values", NpgsqlDbType.Array | NpgsqlDbType.Double, model.CalibrationValues.ToArray());
            p.AddWithValue("importance", NpgsqlDbType.Array | NpgsqlDbType.Double, model.Importance.ToArray());
            p.AddWithValue("trials", NpgsqlDbType.Integer, model.Trials);
            p.AddWithValue("purged", NpgsqlDbType.Integer, model.PurgedRows);
            p.AddWithValue("created_at", NpgsqlDbType.TimestampTz, model.CreatedAt);
            p.AddWithValue("created_by", NpgsqlDbType.Varchar, model.CreatedBy);
            id = (long)(await insert.ExecuteScalarAsync(cancellationToken))!;
        }

        await AppendAsync(connection, transaction, id, ModelRole.Challenger, "registered", model.CreatedBy, model.CreatedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RegisteredModel(id, true);
    }

    public async ValueTask<int> CountOtherModelsAsync(string featureVersion, string sha256, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT COUNT(DISTINCT sha256)::int FROM research.model_versions WHERE feature_version = @f AND sha256 <> @s");
        select.Parameters.AddWithValue("f", NpgsqlDbType.Varchar, featureVersion);
        select.Parameters.AddWithValue("s", NpgsqlDbType.Char, sha256);
        return (int)(await select.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask<StoredModel?> FindAsync(long id, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"{RequiredTrials} SELECT {Full}, COALESCE((SELECT d.role FROM research.model_designations d WHERE d.model_id = v.id ORDER BY d.id DESC LIMIT 1), 'challenger') AS current_role, r.required_trials FROM research.model_versions v JOIN req r ON r.id = v.id WHERE v.id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<IReadOnlyList<StoredModel>> ListAsync(ModelRole? role, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"""
            {RequiredTrials}
            SELECT {Light}, COALESCE(c.role, 'challenger') AS current_role, r.required_trials
            FROM research.model_versions v
            JOIN req r ON r.id = v.id
            LEFT JOIN (SELECT DISTINCT ON (model_id) model_id, role FROM research.model_designations ORDER BY model_id, id DESC) c ON c.model_id = v.id
            WHERE (@role::text IS NULL OR COALESCE(c.role, 'challenger') = @role::text)
            ORDER BY v.id DESC LIMIT @limit
            """);
        select.Parameters.Add(new NpgsqlParameter("role", NpgsqlDbType.Text) { Value = role is { } r ? r.Code() : DBNull.Value });
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var models = new List<StoredModel>();
        while (await reader.ReadAsync(cancellationToken))
        {
            models.Add(Read(reader));
        }

        return models;
    }

    public async ValueTask<IReadOnlyList<StoredDesignation>> DesignationsAsync(long id, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, model_id, role, reason, created_at, created_by FROM research.model_designations WHERE model_id = @id ORDER BY id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<StoredDesignation>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new StoredDesignation(reader.GetInt64(0), reader.GetInt64(1), ModelRoleExtensions.Parse(reader.GetString(2)) ?? ModelRole.Challenger, reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), reader.GetString(5)));
        }

        return rows;
    }

    public async ValueTask<bool> DesignateAsync(long id, ModelRole role, string reason, string actor, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('research.model_designations'))", connection, transaction))
        {
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        // Checked again under the lock: a second designation of the same role that raced the first writes nothing.
        await using (var current = new NpgsqlCommand("SELECT role FROM research.model_designations WHERE model_id = @id ORDER BY id DESC LIMIT 1", connection, transaction))
        {
            current.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
            var now = await current.ExecuteScalarAsync(cancellationToken) as string ?? ModelRole.Challenger.Code();
            if (now == role.Code())
            {
                return false;
            }
        }

        if (role == ModelRole.Champion)
        {
            await using var retire = new NpgsqlCommand("""
                INSERT INTO research.model_designations (model_id, role, reason, created_at, created_by)
                SELECT d.model_id, 'retired', @reason, @at, @by
                FROM (SELECT DISTINCT ON (model_id) model_id, role FROM research.model_designations ORDER BY model_id, id DESC) d
                WHERE d.role = 'champion' AND d.model_id <> @id
                """, connection, transaction);
            retire.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, string.Create(CultureInfo.InvariantCulture, $"replaced by model {id}"));
            retire.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, at);
            retire.Parameters.AddWithValue("by", NpgsqlDbType.Varchar, actor);
            retire.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
            await retire.ExecuteNonQueryAsync(cancellationToken);
        }

        await AppendAsync(connection, transaction, id, role, reason, actor, at, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task AppendAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long id, ModelRole role, string reason, string actor, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var insert = new NpgsqlCommand("INSERT INTO research.model_designations (model_id, role, reason, created_at, created_by) VALUES (@id, @role, @reason, @at, @by)", connection, transaction);
        insert.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        insert.Parameters.AddWithValue("role", NpgsqlDbType.Varchar, role.Code());
        insert.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, reason);
        insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, at);
        insert.Parameters.AddWithValue("by", NpgsqlDbType.Varchar, actor);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static StoredModel Read(DbDataReader r)
    {
        var model = new ModelToRegister(
            VersionKey: r.GetString(1),
            Sha256: r.GetString(2),
            ArtifactBytes: r.GetInt32(3),
            Trainer: r.GetString(4),
            Note: r.IsDBNull(5) ? null : r.GetString(5),
            TrainingSetId: r.GetString(6),
            FeatureVersion: r.GetString(7),
            FeatureWidth: r.GetInt32(8),
            FitRows: r.GetInt32(9),
            CalibrationRows: r.GetInt32(10),
            TrainRows: r.GetInt32(11),
            FromDate: r.GetFieldValue<DateOnly>(12),
            ToDate: r.GetFieldValue<DateOnly>(13),
            HoldOutStart: r.IsDBNull(14) ? null : r.GetFieldValue<DateOnly>(14),
            Verdict: Enum.Parse<ValidationOutcome>(r.GetString(15)),
            UsedFolds: r.GetInt32(16),
            FoldsBeatingBoth: r.GetInt32(17),
            FoldsNeeded: r.GetInt32(18),
            PooledBeatsBoth: r.GetBoolean(19),
            PooledEcePp: r.GetDouble(20),
            PooledLogLoss: r.GetDouble(21),
            PooledBrier: r.GetDouble(22),
            Reasons: r.GetString(23),
            ReportText: r.GetString(24),
            Parameters: r.GetString(25),
            CalibrationScores: r.GetFieldValue<double[]>(26),
            CalibrationValues: r.GetFieldValue<double[]>(27),
            Importance: r.GetFieldValue<double[]>(28),
            Trials: r.GetInt32(29),
            PurgedRows: r.GetInt32(30),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(31),
            CreatedBy: r.GetString(32));
        return new StoredModel(r.GetInt64(0), ModelRoleExtensions.Parse(r.GetString(33)) ?? ModelRole.Challenger, model, r.GetInt32(34));
    }
}
