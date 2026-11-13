using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.ml_holdout_scores</c> behind <see cref="IMlHoldOutScores"/> (CF-10e part 3, ADR 0285): select and insert only (the runtime role has no more); the database stamps the time and refuses an automatic row while <c>ml.holdout.auto_score</c> is not ON.</summary>
internal sealed class MlHoldOutScores(NpgsqlDataSource dataSource) : IMlHoldOutScores
{
    public async ValueTask<StoredMlHoldOutScore?> FindAsync(long modelId, DateOnly blockStart, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT look_key, row_count, day_count, logloss_model, logloss_baseline, baseline_name, p_better, low, high, policy_effect_r, input_hash, setting_hash, mode, actor, scored_at, dropped, block_end, look_id, setting_id
            FROM research.ml_holdout_scores WHERE model_id = @model AND block_start = @block
            """);
        select.Parameters.AddWithValue("model", NpgsqlDbType.Bigint, modelId);
        select.Parameters.AddWithValue("block", NpgsqlDbType.Date, blockStart);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredMlHoldOutScore(
            modelId,
            blockStart,
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetDouble(3),
            reader.GetDouble(4),
            reader.GetString(5),
            Nullable(reader, 6),
            Nullable(reader, 7),
            Nullable(reader, 8),
            reader.GetDouble(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetFieldValue<DateTimeOffset>(14),
            reader.GetInt32(15),
            DateOnly.FromDateTime(reader.GetDateTime(16)),
            reader.GetGuid(17),
            reader.IsDBNull(18) ? null : reader.GetInt64(18));
    }

    public async ValueTask<bool> InsertAsync(StoredMlHoldOutScore score, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(score);
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO research.ml_holdout_scores (model_id, block_start, look_key, row_count, day_count, logloss_model, logloss_baseline, baseline_name, p_better, low, high, policy_effect_r, input_hash, setting_hash, mode, actor, dropped, block_end, look_id, setting_id)
            VALUES (@model, @block, @look, @rows, @days, @lm, @lb, @baseline, @p, @low, @high, @effect, @input, @setting, @mode, @actor, @dropped, @end, @lookid, @settingid)
            ON CONFLICT (model_id, block_start) DO NOTHING
            """);
        var p = insert.Parameters;
        p.AddWithValue("model", NpgsqlDbType.Bigint, score.ModelId);
        p.AddWithValue("block", NpgsqlDbType.Date, score.BlockStart);
        p.AddWithValue("look", NpgsqlDbType.Varchar, score.LookKey);
        p.AddWithValue("rows", NpgsqlDbType.Integer, score.Rows);
        p.AddWithValue("days", NpgsqlDbType.Integer, score.Days);
        p.AddWithValue("lm", NpgsqlDbType.Double, score.LogLossModel);
        p.AddWithValue("lb", NpgsqlDbType.Double, score.LogLossBaseline);
        p.AddWithValue("baseline", NpgsqlDbType.Varchar, score.BaselineName);
        p.AddWithValue("p", NpgsqlDbType.Double, (object?)score.ProbabilityBetter ?? DBNull.Value);
        p.AddWithValue("low", NpgsqlDbType.Double, (object?)score.GainLow ?? DBNull.Value);
        p.AddWithValue("high", NpgsqlDbType.Double, (object?)score.GainHigh ?? DBNull.Value);
        p.AddWithValue("effect", NpgsqlDbType.Double, score.PolicyEffectR);
        p.AddWithValue("input", NpgsqlDbType.Char, score.InputHash);
        p.AddWithValue("setting", NpgsqlDbType.Char, score.SettingHash);
        p.AddWithValue("mode", NpgsqlDbType.Varchar, score.Mode);
        p.AddWithValue("actor", NpgsqlDbType.Varchar, score.Actor);
        p.AddWithValue("dropped", NpgsqlDbType.Integer, score.Dropped);
        p.AddWithValue("end", NpgsqlDbType.Date, score.BlockEnd);
        p.AddWithValue("lookid", NpgsqlDbType.Uuid, score.LookId);
        p.AddWithValue("settingid", NpgsqlDbType.Bigint, (object?)score.SettingId ?? DBNull.Value);
        return await insert.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static double? Nullable(DbDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetDouble(i);
}
