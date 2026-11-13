using Npgsql;
using XDrishti.Application.Abstractions.Shadow;

namespace XDrishti.Infrastructure.Paper;

/// <summary>
/// The switch <c>paper.shadow.enabled</c> (spec CF-08, D7): the newest row of <c>paper.shadow_settings</c>, OFF when there is none (<see cref="IShadowSettings"/>; the one seed row of ADR 0282 is written by a migration, sealed by the stamp trigger), and its append (<see cref="IShadowSwitchStore"/>). Select and insert only; the database stamps the
/// time and the id, drawn under a lock so that the highest id is the last to commit (the P7-12 pattern).
/// </summary>
internal sealed class ShadowSettings(NpgsqlDataSource dataSource) : IShadowSettings, IShadowSwitchStore
{
    public async ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT enabled FROM paper.shadow_settings ORDER BY id DESC LIMIT 1");
        return await select.ExecuteScalarAsync(cancellationToken) is true;
    }

    public async ValueTask<long> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("INSERT INTO paper.shadow_settings (enabled, actor, reason) VALUES (@enabled, @actor, @reason) RETURNING id");
        command.Parameters.AddWithValue("enabled", enabled);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("reason", reason);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
