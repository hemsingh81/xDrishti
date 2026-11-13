using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.ml_holdout_scores</c> behind <see cref="IMlHoldOutScores"/> (CF-10e part 3, ADR 0285): select and insert only (the runtime role has no more); the database stamps the time, refuses an automatic scored row while <c>ml.holdout.auto_score</c> is not ON and a row of a look that was not claimed, and holds one row per look.</summary>
internal sealed class MlHoldOutScores(NpgsqlDataSource dataSource) : IMlHoldOutScores
{
    public async ValueTask<StoredMlHoldOutScore?> FindByLookAsync(string lookKey, DateOnly blockStart, Guid lookId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT model_id, outcome, outcome_code, row_count, day_count, logloss_model, logloss_baseline, baseline_name, p_better, low, high, policy_effect_r, input_hash, setting_hash, mode, actor, scored_at, dropped, block_end, look_id, setting_id, versions_planned, versions_run
            FROM research.ml_holdout_scores WHERE look_key = @key AND block_start = @block AND look_id = @look
            """);
        select.Parameters.AddWithValue("key", NpgsqlDbType.Varchar, lookKey);
        select.Parameters.AddWithValue("block", NpgsqlDbType.Date, blockStart);
        select.Parameters.AddWithValue("look", NpgsqlDbType.Uuid, lookId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredMlHoldOutScore(
            reader.GetInt64(0),
            blockStart,
            lookKey,
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Nullable(reader, 5),
            Nullable(reader, 6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            Nullable(reader, 8),
            Nullable(reader, 9),
            Nullable(reader, 10),
            Nullable(reader, 11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetFieldValue<DateTimeOffset>(16),
            reader.GetInt32(17),
            DateOnly.FromDateTime(reader.GetDateTime(18)),
            reader.GetGuid(19),
            reader.IsDBNull(20) ? null : reader.GetInt64(20),
            reader.GetInt32(21),
            reader.GetInt32(22));
    }

    public async ValueTask<bool> InsertAsync(StoredMlHoldOutScore score, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(score);
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO research.ml_holdout_scores (model_id, block_start, look_key, outcome, outcome_code, row_count, day_count, logloss_model, logloss_baseline, baseline_name, p_better, low, high, policy_effect_r, input_hash, setting_hash, mode, actor,
                dropped, block_end, look_id, setting_id, versions_planned, versions_run)
            VALUES (@model, @block, @look, @outcome, @code, @rows, @days, @lm, @lb, @baseline, @p, @low, @high, @effect, @input, @setting, @mode, @actor, @dropped, @end, @lookid, @settingid, @planned, @ran)
            ON CONFLICT DO NOTHING
            """);
        var p = insert.Parameters;
        p.AddWithValue("model", NpgsqlDbType.Bigint, score.ModelId);
        p.AddWithValue("block", NpgsqlDbType.Date, score.BlockStart);
        p.AddWithValue("look", NpgsqlDbType.Varchar, score.LookKey);
        p.AddWithValue("outcome", NpgsqlDbType.Varchar, score.Outcome);
        p.AddWithValue("code", NpgsqlDbType.Varchar, (object?)score.OutcomeCode ?? DBNull.Value);
        p.AddWithValue("rows", NpgsqlDbType.Integer, (object?)score.Rows ?? DBNull.Value);
        p.AddWithValue("days", NpgsqlDbType.Integer, (object?)score.Days ?? DBNull.Value);
        p.AddWithValue("lm", NpgsqlDbType.Double, (object?)score.LogLossModel ?? DBNull.Value);
        p.AddWithValue("lb", NpgsqlDbType.Double, (object?)score.LogLossBaseline ?? DBNull.Value);
        p.AddWithValue("baseline", NpgsqlDbType.Varchar, (object?)score.BaselineName ?? DBNull.Value);
        p.AddWithValue("p", NpgsqlDbType.Double, (object?)score.ProbabilityBetter ?? DBNull.Value);
        p.AddWithValue("low", NpgsqlDbType.Double, (object?)score.GainLow ?? DBNull.Value);
        p.AddWithValue("high", NpgsqlDbType.Double, (object?)score.GainHigh ?? DBNull.Value);
        p.AddWithValue("effect", NpgsqlDbType.Double, (object?)score.PolicyEffectR ?? DBNull.Value);
        p.AddWithValue("input", NpgsqlDbType.Char, score.InputHash);
        p.AddWithValue("setting", NpgsqlDbType.Char, score.SettingHash);
        p.AddWithValue("mode", NpgsqlDbType.Varchar, score.Mode);
        p.AddWithValue("actor", NpgsqlDbType.Varchar, score.Actor);
        p.AddWithValue("dropped", NpgsqlDbType.Integer, score.Dropped);
        p.AddWithValue("end", NpgsqlDbType.Date, score.BlockEnd);
        p.AddWithValue("lookid", NpgsqlDbType.Uuid, score.LookId);
        p.AddWithValue("settingid", NpgsqlDbType.Bigint, (object?)score.SettingId ?? DBNull.Value);
        p.AddWithValue("planned", NpgsqlDbType.Integer, score.VersionsPlanned);
        p.AddWithValue("ran", NpgsqlDbType.Integer, score.VersionsRun);
        return await insert.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static double? Nullable(DbDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetDouble(i);
}
