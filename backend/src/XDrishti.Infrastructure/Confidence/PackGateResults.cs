using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Confidence;

namespace XDrishti.Infrastructure.Confidence;

/// <summary>
/// The owner's records of <c>verify-pack --gate</c> results (spec CF-11 row 15, ADR 0226): <c>readiness.pack_gate_results</c>, select and insert only. The database stamps the time and the id, drawn under a lock so that the highest id is the last to commit.
/// </summary>
internal sealed class PackGateResults(NpgsqlDataSource dataSource) : IPackGateResultStore, IPackGateResultReader
{
    public async ValueTask<long> AppendAsync(string strategyId, int version, string packId, string status, int tradeCount, string actor, string reason, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "INSERT INTO readiness.pack_gate_results (strategy_id, version, pack_id, status, trade_count, actor, reason) VALUES (@strategy, @version, @pack, @status, @trades, @actor, @reason) RETURNING id");
        command.Parameters.AddWithValue("strategy", NpgsqlDbType.Varchar, strategyId);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, version);
        command.Parameters.AddWithValue("pack", NpgsqlDbType.Varchar, packId);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Varchar, status);
        command.Parameters.AddWithValue("trades", NpgsqlDbType.Integer, tradeCount);
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, actor);
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, reason);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask<PackGateResultRow?> LatestAsync(string strategyId, int version, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT id, pack_id, status, trade_count, recorded_at FROM readiness.pack_gate_results WHERE strategy_id = @strategy AND version = @version ORDER BY id DESC LIMIT 1");
        command.Parameters.AddWithValue("strategy", NpgsqlDbType.Varchar, strategyId);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PackGateResultRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetFieldValue<DateTimeOffset>(4))
            : null;
    }
}
