using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.training_sets</c> behind <see cref="ITrainingSetStore"/> (spec P6-04, ADR 0066): raw SQL, insert only (the runtime role has no more). The set id is the idempotency key.</summary>
internal sealed class TrainingSetStore(NpgsqlDataSource dataSource) : ITrainingSetStore
{
    public async ValueTask<bool> StoreAsync(TrainingSetToStore record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var d = record.Data;
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO research.training_sets (set_id, label_set_id, rule_version, feature_version, basis, sources, required_features, half_life_days, prior_strength, min_trades, credible_level, from_date, to_date, labelled, row_count,
                positives, negatives, unresolved, no_target, dropped_no_features, dropped_unknown, created_at, created_by)
            VALUES (@set_id, @label_set_id, @rule, @version, @basis, @sources, @required, @half_life, @strength, @min_trades, @level, @from, @to, @labelled, @rows,
                @positives, @negatives, @unresolved, @no_target, @no_features, @unknown, @created_at, @created_by)
            ON CONFLICT (set_id) DO NOTHING
            """);
        var p = insert.Parameters;
        p.AddWithValue("set_id", NpgsqlDbType.Char, d.Id);
        p.AddWithValue("label_set_id", NpgsqlDbType.Char, d.LabelSetId);
        p.AddWithValue("rule", NpgsqlDbType.Varchar, d.RuleVersion);
        p.AddWithValue("version", NpgsqlDbType.Varchar, d.FeatureVersion);
        p.AddWithValue("basis", NpgsqlDbType.Varchar, d.Basis.ToString());
        p.AddWithValue("sources", NpgsqlDbType.Text, record.Sources);
        p.AddWithValue("required", NpgsqlDbType.Text, string.Join(',', d.Required));
        p.AddWithValue("half_life", NpgsqlDbType.Double, record.Options.RecencyHalfLifeDays);
        p.AddWithValue("strength", NpgsqlDbType.Double, record.Options.PriorStrength);
        p.AddWithValue("min_trades", NpgsqlDbType.Integer, record.Options.MinTrades);
        p.AddWithValue("level", NpgsqlDbType.Double, record.Options.CredibleLevel);
        p.AddWithValue("from", NpgsqlDbType.Date, d.FromDate);
        p.AddWithValue("to", NpgsqlDbType.Date, d.ToDate);
        p.AddWithValue("labelled", NpgsqlDbType.Integer, d.Labelled);
        p.AddWithValue("rows", NpgsqlDbType.Integer, d.Rows.Count);
        p.AddWithValue("positives", NpgsqlDbType.Integer, d.Positives);
        p.AddWithValue("negatives", NpgsqlDbType.Integer, d.Negatives);
        p.AddWithValue("unresolved", NpgsqlDbType.Integer, record.Unresolved);
        p.AddWithValue("no_target", NpgsqlDbType.Integer, record.NoTarget);
        p.AddWithValue("no_features", NpgsqlDbType.Integer, d.DroppedNoFeatures);
        p.AddWithValue("unknown", NpgsqlDbType.Integer, d.DroppedUnknown);
        p.AddWithValue("created_at", NpgsqlDbType.TimestampTz, record.CreatedAt);
        p.AddWithValue("created_by", NpgsqlDbType.Varchar, record.CreatedBy);
        return await insert.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
