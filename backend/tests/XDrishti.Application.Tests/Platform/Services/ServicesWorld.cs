using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>
/// The services framework wired as in production but over fakes: a fake clock (Monday 2026-10-12, India time), the in-memory rows and runs, a trading calendar, the test services, the scheduler, the runner and
/// the handlers of the screen. Handlers are resolved from a scope per call, like a request.
/// </summary>
internal sealed class ServicesWorld : IDisposable
{
    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private readonly MiniProvider _provider;

    public ServicesWorld(params TestService[] services)
        : this((_, _) => services, _ => { })
    {
    }

    /// <param name="create">Builds the services (given the world, for its clock, and the scope factory); the registry is made of them.</param>
    /// <param name="configure">Registers what those services need.</param>
    public ServicesWorld(Func<ServicesWorld, IServiceScopeFactory, IReadOnlyList<IScheduledService>> create, Action<MiniProvider> configure)
    {
        Clock = new SettableClock(At(12, 10, 0));
        var provider = new MiniProvider()
            .Singleton<TimeProvider>(Clock)
            .Singleton(Data)
            .Singleton<IServiceScheduleStore>(Data)
            .Singleton<IServiceRunStore>(Data)
            .Singleton<IPlatformUnitOfWork>(Data)
            .Singleton<IAccountEventLog>(Data)
            .Singleton<ITradingCalendar>(new FixedTradingCalendar(this))
            .Singleton<IApplicationInfo>(Application)
            .Singleton(NoAccountSync())
            .Singleton<ServiceRunner, ServiceRunner>()
            .Singleton<ServiceScheduler, ServiceScheduler>()
            .Scoped<ServiceSettingsReader>()
            .Scoped<ServiceViewReader>()
            .Scoped<ServiceWriter>()
            .Scoped<AccountSyncEntry>()
            .Scoped<IServiceScheduleReader, ServiceScheduleReader>()
            .Scoped<IQueryHandler<ListServicesQuery, ServiceList>, ListServicesHandler>()
            .Scoped<IQueryHandler<GetServiceQuery, Result<ServiceView>>, GetServiceHandler>()
            .Scoped<IQueryHandler<ListServiceRunsQuery, Result<ServiceRunPage>>, ListServiceRunsHandler>()
            .Scoped<IQueryHandler<GetServicesTimelineQuery, Result<ServicesTimeline>>, GetServicesTimelineHandler>()
            .Scoped<IQueryHandler<GetServiceNoticesQuery, ServiceNotices>, GetServiceNoticesHandler>()
            .Scoped<IQueryHandler<PreviewServiceScheduleQuery, Result<SchedulePreview>>, PreviewServiceScheduleHandler>()
            .Scoped<ICommandHandler<SaveServiceScheduleCommand, ServiceView>, SaveServiceScheduleHandler>()
            .Scoped<ICommandHandler<ResetServiceScheduleCommand, ServiceView>, ResetServiceScheduleHandler>()
            .Scoped<ICommandHandler<PauseServiceCommand, ServiceView>, PauseServiceHandler>()
            .Scoped<ICommandHandler<ResumeServiceCommand, ServiceView>, ResumeServiceHandler>()
            .Scoped<ICommandHandler<RunServiceNowCommand, ServiceRunView>, RunServiceNowHandler>();
        var all = create(this, provider);
        provider.Singleton(new ServiceRegistry(all));
        configure(provider);
        Services = [.. all.OfType<TestService>()];
        _provider = provider;
    }

    public IReadOnlyList<TestService> Services { get; }

    public SettableClock Clock { get; }

    public InMemoryServiceData Data { get; } = new();

    public TradingCalendar Calendar { get; set; } = new();

    public FakeApplicationInfo Application { get; } = new();

    public ServiceScheduler Scheduler => _provider.GetRequiredService<ServiceScheduler>();

    public ServiceRunner Runner => _provider.GetRequiredService<ServiceRunner>();

    public static DateTimeOffset At(int day, int hour, int minute, int second = 0, int month = 10) => new(2026, month, day, hour, minute, second, Ist);

    /// <summary>Sets the clock, also back in time (an adjusted machine clock).</summary>
    public void SetClock(DateTimeOffset instant) => Clock.Jump(instant);

    /// <summary>Runs a handler the way a request does: in its own scope.</summary>
    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, ValueTask<T>> action)
    {
        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public async Task<Result<ServiceView>> SaveAsync(string id, ServiceScheduleInput input, string actor = "owner@example.invalid") =>
        await WithScopeAsync(sp => sp.GetRequiredService<ICommandHandler<SaveServiceScheduleCommand, ServiceView>>().HandleAsync(new SaveServiceScheduleCommand(id, input, actor), Ct));

    public async Task<Result<SchedulePreview>> PreviewAsync(string id, ServiceScheduleInput input) =>
        await WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<PreviewServiceScheduleQuery, Result<SchedulePreview>>>().HandleAsync(new PreviewServiceScheduleQuery(id, input), Ct));

    public async Task<ServiceList> ListAsync() =>
        await WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<ListServicesQuery, ServiceList>>().HandleAsync(new ListServicesQuery(), Ct));

    public async Task<ServiceView> ViewAsync(string id) => (await ListAsync()).Items.Single(s => s.Id == id);

    public async Task<Result<ServiceView>> PauseAsync(string id) =>
        await WithScopeAsync(sp => sp.GetRequiredService<ICommandHandler<PauseServiceCommand, ServiceView>>().HandleAsync(new PauseServiceCommand(id, "owner@example.invalid"), Ct));

    public async Task<Result<ServiceView>> ResumeAsync(string id) =>
        await WithScopeAsync(sp => sp.GetRequiredService<ICommandHandler<ResumeServiceCommand, ServiceView>>().HandleAsync(new ResumeServiceCommand(id, "owner@example.invalid"), Ct));

    public async Task<Result<ServiceRunView>> RunNowAsync(string id) =>
        await WithScopeAsync(sp => sp.GetRequiredService<ICommandHandler<RunServiceNowCommand, ServiceRunView>>().HandleAsync(new RunServiceNowCommand(id, "owner@example.invalid"), Ct));

    public void Dispose() => _provider.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IAccountSyncSource NoAccountSync()
    {
        var source = Substitute.For<IAccountSyncSource>();
        source.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountSyncState(false, null, null));
        return source;
    }

    private sealed class FixedTradingCalendar(ServicesWorld world) : ITradingCalendar
    {
        public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(world.Calendar);

        public void Invalidate()
        {
            // nothing is kept
        }
    }
}
