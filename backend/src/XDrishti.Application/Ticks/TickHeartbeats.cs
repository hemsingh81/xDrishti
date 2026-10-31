using System.Text.Json;
using System.Text.Json.Serialization;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Domain.Platform;

namespace XDrishti.Application.Ticks;

/// <summary>Source-generated JSON of the tick heartbeat details (camelCase).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(TickHeartbeat))]
internal sealed partial class TickJson : JsonSerializerContext
{
}

/// <summary>The heartbeat row <c>xd-ticks</c>: state word and details on the existing heartbeat store. A reader treats a row without readable details as a recorder that is alive but says nothing.</summary>
internal sealed class TickHeartbeats(IServiceHeartbeatStore store, TimeProvider clock) : ITickHeartbeats
{
    /// <summary>The recorder counts as down when its heartbeat is older than this.</summary>
    internal static readonly TimeSpan AliveTolerance = TimeSpan.FromSeconds(60);

    public async ValueTask ReportAsync(string state, TickHeartbeat details, string instance, string version, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(details);
        var now = clock.GetUtcNow();
        var heartbeat = await store.FindAsync(ITickHeartbeats.ServiceName, cancellationToken);
        if (heartbeat is null)
        {
            heartbeat = ServiceHeartbeat.Start(ITickHeartbeats.ServiceName, instance, version, now);
            store.Add(heartbeat);
        }

        heartbeat.Beat(instance, version, now, state, JsonSerializer.Serialize(details, TickJson.Default.TickHeartbeat));
        await store.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<TickStatus?> GetAsync(CancellationToken cancellationToken)
    {
        var heartbeat = await store.FindAsync(ITickHeartbeats.ServiceName, cancellationToken);
        return heartbeat is null ? null : new TickStatus(heartbeat.State ?? "unknown", heartbeat.LastSeenAt, heartbeat.IsAlive(clock.GetUtcNow(), AliveTolerance), Read(heartbeat.Details));
    }

    private static TickHeartbeat? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, TickJson.Default.TickHeartbeat);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
