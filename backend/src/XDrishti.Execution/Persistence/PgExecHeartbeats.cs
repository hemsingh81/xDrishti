using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Execution.Application;

namespace XDrishti.Execution.Persistence;

/// <summary>The service's own row in <c>platform.service_heartbeats</c> (name <c>exec</c>): the only write outside schema <c>execution</c>. The database clock stamps it, so the status read compares like with like.</summary>
internal sealed class PgExecHeartbeats(NpgsqlDataSource data) : IExecHeartbeats
{
    private static readonly string Instance = $"{Environment.MachineName}:{Environment.ProcessId}";
    private static readonly string Version = typeof(PgExecHeartbeats).Assembly.GetName().Version?.ToString() ?? "0";

    public async Task BeatAsync(ExecHeartbeat beat, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("""
            INSERT INTO platform.service_heartbeats (service_name, instance, version, started_at, last_seen_at, state, details)
            VALUES (@name, @instance, @version, now(), now(), @state, @details)
            ON CONFLICT (service_name) DO UPDATE SET
                started_at = CASE WHEN platform.service_heartbeats.instance = EXCLUDED.instance THEN platform.service_heartbeats.started_at ELSE EXCLUDED.started_at END,
                instance = EXCLUDED.instance, version = EXCLUDED.version, last_seen_at = EXCLUDED.last_seen_at, state = EXCLUDED.state, details = EXCLUDED.details
            """);
        command.Parameters.AddWithValue("name", IExecHeartbeats.ServiceName);
        command.Parameters.AddWithValue("instance", Instance);
        command.Parameters.AddWithValue("version", Version);
        command.Parameters.AddWithValue("state", beat.State);
        command.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(new { environment = beat.Environment, settingsHash = beat.SettingsHash, commandsAnswered = beat.CommandsAnswered }) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
