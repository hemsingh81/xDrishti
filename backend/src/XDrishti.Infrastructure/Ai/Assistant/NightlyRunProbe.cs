using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>Whether the service <c>nightly</c> has a run in state running that started within the window (spec P9-00 D13): one indexed read of <c>platform.service_runs</c>.</summary>
internal sealed class NightlyRunProbe(NpgsqlDataSource dataSource, TimeProvider time) : INightlyRunProbe
{
    public const string ServiceId = "nightly";

    public async ValueTask<bool> IsRunningAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM platform.service_runs WHERE service_id = @service AND status = 'running' AND started_at > @since)");
        select.Parameters.AddWithValue("service", NpgsqlDbType.Varchar, ServiceId);
        select.Parameters.AddWithValue("since", NpgsqlDbType.TimestampTz, time.GetUtcNow() - window);
        return await select.ExecuteScalarAsync(cancellationToken) is true;
    }
}
