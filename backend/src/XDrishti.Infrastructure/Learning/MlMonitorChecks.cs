using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.ml_monitor_checks</c> behind <see cref="IMlMonitorChecks"/> (spec CF-10 part 10d-1): one insert-only evaluation per model and India day, with the settings hash. A second insert for the same day does nothing.</summary>
internal sealed class MlMonitorChecks(NpgsqlDataSource dataSource) : IMlMonitorChecks
{
    public async ValueTask<bool> ExistsAsync(long modelId, DateOnly day, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT 1 FROM research.ml_monitor_checks WHERE model_id = @model AND checked_on = @day");
        select.Parameters.AddWithValue("model", NpgsqlDbType.Bigint, modelId);
        select.Parameters.AddWithValue("day", NpgsqlDbType.Date, day);
        return await select.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async ValueTask<StoredMlMonitorCheck?> LatestAsync(long modelId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT checked_on, verdicts::text FROM research.ml_monitor_checks WHERE model_id = @model ORDER BY checked_on DESC LIMIT 1");
        select.Parameters.AddWithValue("model", NpgsqlDbType.Bigint, modelId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new StoredMlMonitorCheck(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetString(1)) : null;
    }

    public async ValueTask<bool> InsertAsync(NewMlMonitorCheck check, DateOnly day, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO research.ml_monitor_checks (model_id, checked_on, settings_hash, verdicts)
            VALUES (@model, @day, @hash, @verdicts)
            ON CONFLICT (model_id, checked_on) DO NOTHING
            """);
        command.Parameters.AddWithValue("model", NpgsqlDbType.Bigint, check.ModelId);
        command.Parameters.AddWithValue("day", NpgsqlDbType.Date, day);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, check.SettingsHash);
        command.Parameters.AddWithValue("verdicts", NpgsqlDbType.Jsonb, check.Verdicts);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
