using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>The schedule now in force for a service, for the modules that show it next to their own state (<see cref="IServiceScheduleReader"/>).</summary>
internal sealed class ServiceScheduleReader(ServiceRegistry registry, ServiceSettingsReader settings, ITradingCalendar calendar, TimeProvider clock) : IServiceScheduleReader
{
    public async ValueTask<ServiceScheduleInfo?> GetAsync(string serviceId, CancellationToken cancellationToken)
    {
        if (registry.Find(serviceId) is null)
        {
            return null;
        }

        var service = await settings.ReadAsync(serviceId, cancellationToken);
        var schedule = service.Settings.Schedule;
        var next = service.Settings.Enabled
            ? ScheduleCalculator.NextAfter(schedule, clock.GetUtcNow(), await calendar.GetAsync(cancellationToken), service.NotBeforeSessionEnd)
            : null;
        return new ServiceScheduleInfo(service.Settings.Enabled, schedule.StartTime, ScheduleWords.Describe(schedule, registry.NameOf, service.NotBeforeSessionEnd), next);
    }
}
