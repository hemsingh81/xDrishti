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

    /// <summary>
    /// The settings now in force for a service that is described by a definition but not necessarily registered here (the live feed runs in its own host, which reads its own schedule row): the stored row, else the definition's
    /// defaults - the same rules as <see cref="ReadAllAsync"/>.
    /// </summary>
    public async ValueTask<EffectiveService> ReadAsync(XDrishti.Domain.Platform.Services.ServiceDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var row = (await store.ListAsync(cancellationToken)).FirstOrDefault(r => string.Equals(r.Id, definition.Id, StringComparison.Ordinal));
        return row is null
            ? new EffectiveService(definition, definition.Defaults, null)
            : row.ScheduleReadable
                ? new EffectiveService(definition, row.Settings, row)
                : new EffectiveService(definition, definition.Defaults with { Enabled = row.Enabled }, row, StoredInvalid: true);
    }

    /// <summary>One registered service with the settings now in force.</summary>
    public async ValueTask<EffectiveService> ReadAsync(string serviceId, CancellationToken cancellationToken) =>
        (await ReadAllAsync(cancellationToken)).First(s => s.Id == serviceId);
}
