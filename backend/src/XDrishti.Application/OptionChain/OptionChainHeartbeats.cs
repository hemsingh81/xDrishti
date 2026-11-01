using System.Text.Json;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform;

namespace XDrishti.Application.OptionChain;

/// <summary>The heartbeat row <c>xd-options</c>: state word and details on the existing heartbeat store. A reader treats a row without readable details as a recorder that is alive but says nothing.</summary>
internal sealed class OptionChainHeartbeats(IServiceHeartbeatStore store, TimeProvider clock) : IOptionChainHeartbeats
{
    /// <summary>The recorder counts as down when its heartbeat is older than this.</summary>
    internal static readonly TimeSpan AliveTolerance = TimeSpan.FromSeconds(60);

    public async ValueTask ReportAsync(string state, ChainHeartbeat details, string instance, string version, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(details);
        var now = clock.GetUtcNow();
        var heartbeat = await store.FindAsync(IOptionChainHeartbeats.ServiceName, cancellationToken);
        if (heartbeat is null)
        {
            heartbeat = ServiceHeartbeat.Start(IOptionChainHeartbeats.ServiceName, instance, version, now);
            store.Add(heartbeat);
        }

        heartbeat.Beat(instance, version, now, state, JsonSerializer.Serialize(details, OptionChainJson.Default.ChainHeartbeat));
        await store.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<ChainStatus?> GetAsync(CancellationToken cancellationToken)
    {
        var heartbeat = await store.FindAsync(IOptionChainHeartbeats.ServiceName, cancellationToken);
        return heartbeat is null ? null : new ChainStatus(heartbeat.State ?? "unknown", heartbeat.LastSeenAt, heartbeat.IsAlive(clock.GetUtcNow(), AliveTolerance), Read(heartbeat.Details));
    }

    private static ChainHeartbeat? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, OptionChainJson.Default.ChainHeartbeat);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
