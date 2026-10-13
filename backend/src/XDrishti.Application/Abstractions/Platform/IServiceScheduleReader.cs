namespace XDrishti.Application.Abstractions.Platform;

/// <summary>
/// A service's schedule as the screens of other modules show it (the Data screen's "Automatic update" line, the instrument refresh status): whether it is on, its time of day (null when the schedule has none
/// of its own), the schedule in words and the next scheduled start (null when paused or none ahead).
/// </summary>
public sealed record ServiceScheduleInfo(bool Enabled, TimeOnly? TimeIst, string Words, DateTimeOffset? NextRunAt);

/// <summary>
/// The read side of the services framework for the modules that own a service: the stored schedule of a service, so that they show the same times as the Services screen without knowing how the schedules are
/// stored or checked. Owned by the platform module.
/// </summary>
public interface IServiceScheduleReader
{
    /// <summary>The schedule now in force for the service (stored, else its first-start settings); null when no such service is registered.</summary>
    ValueTask<ServiceScheduleInfo?> GetAsync(string serviceId, CancellationToken cancellationToken);
}
