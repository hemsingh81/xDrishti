using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// A registered service with the settings it runs on now: the stored row, else the first-start settings of its configuration, else the definition's defaults. <see cref="StoredInvalid"/>: the row's schedule could
/// not be read, so the service runs on its defaults (kept paused if the row was paused) until a new schedule is saved.
/// </summary>
internal sealed record EffectiveService(ServiceDefinition Definition, ServiceSettings Settings, ServiceSchedule? Row, bool StoredInvalid = false)
{
    public string Id => Definition.Id;

    /// <summary>The row version the next save must carry; 0 while the service has no row.</summary>
    public long Revision => Row?.Revision ?? 0;

    public bool NotBeforeSessionEnd => Definition.Constraints.NotBeforeSessionEnd;
}
