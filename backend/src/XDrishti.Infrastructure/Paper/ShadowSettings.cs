using Npgsql;
using XDrishti.Application.Abstractions.Shadow;

namespace XDrishti.Infrastructure.Paper;

/// <summary>The switch <c>paper.shadow.enabled</c> (spec CF-08, D7): the newest row of <c>paper.shadow_settings</c>, OFF when there is none. Select only; the owner's act that appends a row is a later slice.</summary>
internal sealed class ShadowSettings(NpgsqlDataSource dataSource) : IShadowSettings
{
    public async ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT enabled FROM paper.shadow_settings ORDER BY id DESC LIMIT 1");
        return await select.ExecuteScalarAsync(cancellationToken) is true;
    }
}
