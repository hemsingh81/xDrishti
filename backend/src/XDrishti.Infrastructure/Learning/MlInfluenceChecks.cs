using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.ml_influence_checks</c> behind <see cref="IMlInfluenceChecks"/> (CF-10 part 10c-2, ADR 0217): raw SQL, select and insert only (the runtime role has no more).</summary>
internal sealed class MlInfluenceChecks(NpgsqlDataSource dataSource) : IMlInfluenceChecks
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<bool> AppendIfChangedAsync(MlInfluenceCheck check, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);

        // shortcut: the "unless the newest check is the same" test is not race-safe under READ COMMITTED (two gates answering at once may both insert the same evaluation); harmless, the table is append-only evidence and a duplicate row changes no answer. Take pg_advisory_xact_lock(model_id) if exact dedupe is ever needed.
        await using var command = dataSource.CreateCommand("""
            INSERT INTO research.ml_influence_checks (model_id, evaluated_at, may_influence, terms, setting_hash, inputs_hash)
            SELECT @model, @at, @may, @terms, @setting, @inputs
            WHERE NOT EXISTS (
                SELECT 1 FROM (SELECT inputs_hash FROM research.ml_influence_checks WHERE model_id = @model ORDER BY id DESC LIMIT 1) latest WHERE latest.inputs_hash = @inputs)
            """);
        command.Parameters.AddWithValue("model", NpgsqlDbType.Bigint, check.ModelId);
        command.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, check.EvaluatedAt.ToUniversalTime());
        command.Parameters.AddWithValue("may", NpgsqlDbType.Boolean, check.MayInfluence);
        command.Parameters.AddWithValue("terms", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(check.Terms.Select(t => new { t.Name, State = t.State.ToString().ToLowerInvariant(), Reasons = t.Reasons.Select(r => r.Code) }), Json));
        command.Parameters.AddWithValue("setting", NpgsqlDbType.Char, check.SettingHash);
        command.Parameters.AddWithValue("inputs", NpgsqlDbType.Char, check.InputsHash);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
