using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.extension_evaluations</c> behind <see cref="IExtensionEvaluationStore"/> (spec P13-05): raw SQL, insert and select only. A second record of the same id writes nothing; a second hold-out reading of a family and version key is refused by the partial unique index.</summary>
internal sealed class ExtensionEvaluationStore(NpgsqlDataSource dataSource) : IExtensionEvaluationStore
{
    private const string HoldOutIndex = "ux_extension_evaluations_holdout";

    private const string Columns = "evaluation_id, family, protocol, extension_version, version_key, base_version, recipe_hash, training_data_id, trial_hash, trials, factor, fold_unit, folds_total, folds_evaluable, rows, flips, estimates, verdict, missing, limits, evidence, inputs, holdout_read";

    public async ValueTask<Result<bool>> AppendAsync(ExtensionEvaluationRecord record, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var insert = dataSource.CreateCommand($"""
            INSERT INTO research.extension_evaluations ({Columns}, created_at)
            VALUES (@id, @family, @protocol, @extension_version, @version_key, @base_version, @recipe_hash, @training_data_id, @trial_hash, @trials, @factor, @fold_unit, @folds_total, @folds_evaluable, @rows, @flips, @estimates, @verdict, @missing, @limits, @evidence, @inputs, @holdout_read, @created_at)
            ON CONFLICT (evaluation_id) DO NOTHING
            """);
        var p = insert.Parameters;
        p.AddWithValue("id", NpgsqlDbType.Char, record.EvaluationId);
        p.AddWithValue("family", NpgsqlDbType.Varchar, record.Family);
        p.AddWithValue("protocol", NpgsqlDbType.Varchar, record.Protocol);
        p.Add(new NpgsqlParameter("extension_version", NpgsqlDbType.Varchar) { Value = (object?)record.ExtensionVersion ?? DBNull.Value });
        p.AddWithValue("version_key", NpgsqlDbType.Varchar, record.VersionKey);
        p.AddWithValue("base_version", NpgsqlDbType.Varchar, record.BaseVersion);
        p.AddWithValue("recipe_hash", NpgsqlDbType.Char, record.RecipeHash);
        p.Add(new NpgsqlParameter("training_data_id", NpgsqlDbType.Char) { Value = (object?)record.TrainingDataId ?? DBNull.Value });
        p.Add(new NpgsqlParameter("trial_hash", NpgsqlDbType.Char) { Value = (object?)record.TrialHash ?? DBNull.Value });
        p.AddWithValue("trials", NpgsqlDbType.Integer, record.Trials);
        p.AddWithValue("factor", NpgsqlDbType.Double, record.Factor);
        p.AddWithValue("fold_unit", NpgsqlDbType.Varchar, record.FoldUnit);
        p.AddWithValue("folds_total", NpgsqlDbType.Integer, record.FoldsTotal);
        p.AddWithValue("folds_evaluable", NpgsqlDbType.Integer, record.FoldsEvaluable);
        p.AddWithValue("rows", NpgsqlDbType.Integer, record.Rows);
        p.Add(new NpgsqlParameter("flips", NpgsqlDbType.Integer) { Value = (object?)record.Flips ?? DBNull.Value });
        p.AddWithValue("estimates", NpgsqlDbType.Jsonb, record.EstimatesJson);
        p.AddWithValue("verdict", NpgsqlDbType.Varchar, record.Verdict);
        p.AddWithValue("missing", NpgsqlDbType.Array | NpgsqlDbType.Text, record.Missing.ToArray());
        p.AddWithValue("limits", NpgsqlDbType.Array | NpgsqlDbType.Text, record.Limits.ToArray());
        p.AddWithValue("evidence", NpgsqlDbType.Jsonb, record.EvidenceJson);
        p.AddWithValue("inputs", NpgsqlDbType.Jsonb, record.InputsJson);
        p.AddWithValue("holdout_read", NpgsqlDbType.Boolean, record.HoldOutRead);
        p.AddWithValue("created_at", NpgsqlDbType.TimestampTz, createdAt);
        try
        {
            return Result.Success(await insert.ExecuteNonQueryAsync(cancellationToken) == 1);
        }
        catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.UniqueViolation && failure.ConstraintName == HoldOutIndex)
        {
            return Result.Failure<bool>(Error.Conflict("extensions.holdout_used", "The hold-out of " + record.Family + " for " + record.VersionKey + " has been read already; it is read once."));
        }
    }

    public async ValueTask<ExtensionEvaluationRecord?> GetAsync(string evaluationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evaluationId);
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM research.extension_evaluations WHERE evaluation_id = @id");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Char, evaluationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<IReadOnlyList<ExtensionEvaluationRecord>> ListAsync(string family, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(family);
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM research.extension_evaluations WHERE family = @family ORDER BY created_at DESC, evaluation_id LIMIT @limit");
        command.Parameters.AddWithValue("family", NpgsqlDbType.Varchar, family);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 1000));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<ExtensionEvaluationRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(Read(reader));
        }

        return list;
    }

    public async ValueTask<bool> HoldOutUsedAsync(string family, string versionKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(versionKey);
        await using var command = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM research.extension_evaluations WHERE family = @family AND version_key = @key AND holdout_read)");
        command.Parameters.AddWithValue("family", NpgsqlDbType.Varchar, family);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Varchar, versionKey);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static ExtensionEvaluationRecord Read(NpgsqlDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
        r.GetInt32(9), r.GetDouble(10), r.GetString(11), r.GetInt32(12), r.GetInt32(13), r.GetInt32(14), r.IsDBNull(15) ? null : r.GetInt32(15), r.GetString(16), r.GetString(17), r.GetFieldValue<string[]>(18), r.GetFieldValue<string[]>(19), r.GetString(20), r.GetString(21), r.GetBoolean(22));
}
