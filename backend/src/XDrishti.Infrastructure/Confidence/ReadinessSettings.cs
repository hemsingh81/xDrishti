using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Confidence;

namespace XDrishti.Infrastructure.Confidence;

/// <summary>
/// The switch <c>confidence.readiness.enforce</c> (spec CF-11 D6, ADR 0221): the newest row of <c>readiness.settings</c>, OFF when there is none (<see cref="IReadinessSettings"/>), and its append (<see cref="IReadinessSwitchStore"/>). Select and insert only; the database stamps
/// the time and the id, drawn under a lock so that the highest id is the last to commit (the pattern of <c>confidence.kill_settings</c>). A failed read throws: the gate reads that as ON.
/// </summary>
internal sealed class ReadinessSettings(NpgsqlDataSource dataSource) : IReadinessSettings, IReadinessSwitchStore
{
    public async ValueTask<ReadinessSwitch> CurrentAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, enforce FROM readiness.settings ORDER BY id DESC LIMIT 1");
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new ReadinessSwitch(reader.GetInt64(0), reader.GetBoolean(1)) : new ReadinessSwitch(null, false);
    }

    public async ValueTask<long> AppendAsync(bool enforce, string actor, string reason, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("INSERT INTO readiness.settings (enforce, actor, reason) VALUES (@enforce, @actor, @reason) RETURNING id");
        command.Parameters.AddWithValue("enforce", NpgsqlDbType.Boolean, enforce);
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, actor);
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, reason);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
