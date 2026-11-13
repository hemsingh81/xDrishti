using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Infrastructure.Lab;

/// <summary><c>lab.plan_freezes</c> behind <see cref="IPlanFreezeStore"/> (CF-06, ADR 0207): raw SQL, insert and select only.</summary>
internal sealed class PlanFreezeStore(NpgsqlDataSource dataSource) : IPlanFreezeStore
{
    public async ValueTask<PlanFreezeOutcome> FreezeAsync(string strategyId, int version, string planHash, Guid firstRunId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // one statement: the other plans of the version are counted as they stood before this insert
        await using var command = dataSource.CreateCommand("""
            WITH prior AS (SELECT count(*) AS c FROM lab.plan_freezes WHERE strategy_id = @id AND version = @v AND plan_hash <> @hash),
                 ins AS (INSERT INTO lab.plan_freezes (strategy_id, version, plan_hash, first_run_id, created_at) VALUES (@id, @v, @hash, @run, @now) ON CONFLICT (strategy_id, version, plan_hash) DO NOTHING RETURNING 1)
            SELECT (SELECT count(*) FROM ins), (SELECT c FROM prior)
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        command.Parameters.AddWithValue("v", NpgsqlDbType.Integer, version);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, planHash);
        command.Parameters.AddWithValue("run", NpgsqlDbType.Uuid, firstRunId);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new PlanFreezeOutcome(reader.GetInt64(0) == 1, (int)reader.GetInt64(1));
    }

    public async ValueTask<string?> FirstAsync(string strategyId, int version, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT plan_hash FROM lab.plan_freezes WHERE strategy_id = @id AND version = @v ORDER BY created_at, id LIMIT 1");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        command.Parameters.AddWithValue("v", NpgsqlDbType.Integer, version);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async ValueTask<DateTimeOffset?> FirstFrozenAtAsync(string strategyId, int version, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT min(created_at) FROM lab.plan_freezes WHERE strategy_id = @id AND version = @v");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        command.Parameters.AddWithValue("v", NpgsqlDbType.Integer, version);
        return await command.ExecuteScalarAsync(cancellationToken) is DateTime at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;
    }
}
