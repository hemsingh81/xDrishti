using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Confidence;

namespace XDrishti.Infrastructure.Confidence;

/// <summary>
/// The switch <c>confidence.kill.auto</c> (spec CF-12 D3, part 12b): the newest row of <c>confidence.kill_settings</c>, OFF when there is none (<see cref="IKillSettings"/>), and its append (<see cref="IKillSwitchStore"/>). Select and insert only; the database stamps the
/// time and the id, drawn under a lock so that the highest id is the last to commit (the pattern of <c>paper.shadow_settings</c>).
/// </summary>
internal sealed class KillSettings(NpgsqlDataSource dataSource) : IKillSettings, IKillSwitchStore
{
    public async ValueTask<KillSwitch> CurrentAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, auto, actor FROM confidence.kill_settings ORDER BY id DESC LIMIT 1");
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new KillSwitch(reader.GetInt64(0), reader.GetBoolean(1), reader.GetString(2)) : new KillSwitch(null, false);
    }

    public async ValueTask<long> AppendAsync(bool auto, string actor, string reason, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("INSERT INTO confidence.kill_settings (auto, actor, reason) VALUES (@auto, @actor, @reason) RETURNING id");
        command.Parameters.AddWithValue("auto", NpgsqlDbType.Boolean, auto);
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, actor);
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, reason);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
