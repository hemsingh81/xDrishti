using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Abstractions.Platform;

/// <summary>The stored choices per service (<c>platform.service_schedules</c>). Changes are saved by <see cref="IPlatformUnitOfWork"/>.</summary>
public interface IServiceScheduleStore
{
    /// <summary>Every row, for reading only.</summary>
    ValueTask<IReadOnlyList<ServiceSchedule>> ListAsync(CancellationToken cancellationToken);

    /// <summary>One row, tracked so that changes can be saved; null when the service has none.</summary>
    ValueTask<ServiceSchedule?> FindAsync(string serviceId, CancellationToken cancellationToken);

    void Add(ServiceSchedule row);
}
