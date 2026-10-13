using XDrishti.Application.Abstractions.Platform;

namespace XDrishti.Application.Platform.Services;

/// <summary>Reads the settings of every registered service in one go (the stored rows are re-read each time, so an edit is seen at once).</summary>
internal sealed class ServiceSettingsReader(ServiceRegistry registry, IServiceScheduleStore store)
{
    public async ValueTask<IReadOnlyList<EffectiveService>> ReadAllAsync(CancellationToken cancellationToken)
    {
        var rows = (await store.ListAsync(cancellationToken)).ToDictionary(r => r.Id, StringComparer.Ordinal);
        return [.. registry.All.Select(service => rows.TryGetValue(service.Definition.Id, out var row)
            ? row.ScheduleReadable
                ? new EffectiveService(service.Definition, row.Settings, row)
                : new EffectiveService(service.Definition, service.Definition.Defaults with { Enabled = row.Enabled }, row, StoredInvalid: true)
            : new EffectiveService(service.Definition, service.FirstStartSettings() ?? service.Definition.Defaults, null))];
    }

    /// <summary>One registered service with the settings now in force.</summary>
    public async ValueTask<EffectiveService> ReadAsync(string serviceId, CancellationToken cancellationToken) =>
        (await ReadAllAsync(cancellationToken)).First(s => s.Id == serviceId);
}
