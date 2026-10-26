using System.Text.Json;
using System.Text.Json.Serialization;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform;

namespace XDrishti.Application.Depth;

/// <summary>Source-generated JSON of the depth heartbeat details (camelCase).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(DepthHeartbeat))]
internal sealed partial class DepthJson : JsonSerializerContext
{
}

/// <summary>The heartbeat row <c>xd-depth</c>: state word and details on the existing heartbeat store. A reader treats a row without readable details as a recorder that is alive but says nothing.</summary>
internal sealed class DepthHeartbeats(IServiceHeartbeatStore store, TimeProvider clock) : IDepthHeartbeats
{
    /// <summary>The recorder counts as down when its heartbeat is older than this.</summary>
    internal static readonly TimeSpan AliveTolerance = TimeSpan.FromSeconds(60);

    public async ValueTask ReportAsync(string state, DepthHeartbeat details, string instance, string version, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(details);
        var now = clock.GetUtcNow();
        var heartbeat = await store.FindAsync(IDepthHeartbeats.ServiceName, cancellationToken);
        if (heartbeat is null)
        {
            heartbeat = ServiceHeartbeat.Start(IDepthHeartbeats.ServiceName, instance, version, now);
            store.Add(heartbeat);
        }

        heartbeat.Beat(instance, version, now, state, JsonSerializer.Serialize(details, DepthJson.Default.DepthHeartbeat));
        await store.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<DepthStatus?> GetAsync(CancellationToken cancellationToken)
    {
        var heartbeat = await store.FindAsync(IDepthHeartbeats.ServiceName, cancellationToken);
        return heartbeat is null ? null : new DepthStatus(heartbeat.State ?? "unknown", heartbeat.LastSeenAt, heartbeat.IsAlive(clock.GetUtcNow(), AliveTolerance), Read(heartbeat.Details));
    }

    private static DepthHeartbeat? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, DepthJson.Default.DepthHeartbeat);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
