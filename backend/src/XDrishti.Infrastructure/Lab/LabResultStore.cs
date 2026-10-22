using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Lab;

namespace XDrishti.Infrastructure.Lab;

/// <summary><c>lab.lab_results</c> behind <see cref="ILabResultStore"/> (spec P5-04, ADR 0060): raw SQL, insert and select only. The document holds numbers, gate reasons and cell names, never a strategy text.</summary>
internal sealed class LabResultStore(NpgsqlDataSource dataSource) : ILabResultStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<bool> AppendAsync(Guid runId, string strategyId, string strategyHash, LabVerdictView verdict, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO lab.lab_results (lab_run_id, strategy_id, strategy_hash, method, outcome, variant_count, adjustment_factor, result, created_at)
            VALUES (@run, @strategy, @hash, 'walk_forward', @outcome, @variants, @factor, @result, @now)
            ON CONFLICT (lab_run_id) DO NOTHING
            """);
        command.Parameters.AddWithValue("run", NpgsqlDbType.Uuid, runId);
        command.Parameters.AddWithValue("strategy", NpgsqlDbType.Text, strategyId);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, strategyHash);
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, verdict.Outcome);
        command.Parameters.AddWithValue("variants", NpgsqlDbType.Integer, verdict.VariantCount);
        command.Parameters.AddWithValue("factor", NpgsqlDbType.Double, verdict.AdjustmentFactor);
        command.Parameters.AddWithValue("result", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(verdict, Json));
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<LabVerdictView?> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT result FROM lab.lab_results WHERE lab_run_id = @run");
        command.Parameters.AddWithValue("run", NpgsqlDbType.Uuid, runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? JsonSerializer.Deserialize<LabVerdictView>(reader.GetString(0), Json) : null;
    }
}
