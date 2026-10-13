using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Refresh;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Tests.Market;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Application.Tests.Market.Calendar;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>The settings of the two real services (end-of-day run, instrument master refresh) as the handlers of the Data and Instruments screens read them: stored rows, else the first-start settings of the configuration.</summary>
internal static class ServiceReaders
{
    public static ServiceRegistry Registry(IDataSettings? data = null, IInstrumentRefreshSettings? refresh = null)
    {
        var scopes = Substitute.For<IServiceScopeFactory>();
        IScheduledService[] services =
        [
            new EodService(scopes, data ?? new FakeDataSettings(), new DataJobRecovery(), TimeProvider.System, NullLogger<EodService>.Instance),
            new InstrumentMasterService(scopes, refresh ?? new FakeRefreshSettings(), TimeProvider.System),
        ];
        return new ServiceRegistry(services);
    }

    /// <summary>A reader over the given rows (none: every service on its configuration).</summary>
    public static ServiceSettingsReader For(IDataSettings? data = null, IInstrumentRefreshSettings? refresh = null, InMemoryServiceData? rows = null) =>
        new(Registry(data, refresh), rows ?? new InMemoryServiceData());

    /// <summary>The schedule port the Data and Instruments handlers read, over the same rows, with the given calendar and clock.</summary>
    public static ServiceScheduleReader Schedules(TimeProvider clock, IDataSettings? data = null, IInstrumentRefreshSettings? refresh = null, InMemoryServiceData? rows = null, ITradingCalendar? calendar = null)
    {
        var registry = Registry(data, refresh);
        return new ServiceScheduleReader(registry, new ServiceSettingsReader(registry, rows ?? new InMemoryServiceData()), calendar ?? new FixedCalendar(), clock);
    }
}
