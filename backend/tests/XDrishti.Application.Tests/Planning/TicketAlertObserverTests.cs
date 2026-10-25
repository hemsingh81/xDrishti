#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Planning;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Planning;

/// <summary>Spec P7-20: the observer stores crossings once, refreshes its watched list on the cache time, swallows a failing database, and the OFF notice source reads nothing; the prompt service stores nothing while OFF.</summary>
public sealed class TicketAlertObserverTests
{
    private static readonly Guid Instrument = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    // 10:00 India on Monday 2026-10-26.
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 4, 30, 0, TimeSpan.Zero);

    private readonly ITicketAlertStore _store = Substitute.For<ITicketAlertStore>();
    private readonly IInstrumentSecurities _securities = Substitute.For<IInstrumentSecurities>();
    private readonly FakeTimeProvider _time = new(Now);

    private sealed class Settings(bool enabled) : ITicketAlertSettings
    {
        public bool Enabled => enabled;

        public int PromptTradingDays => 3;

        public int WatchedCacheSeconds => 30;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MiniProvider Services() => new MiniProvider().Singleton(_store).Singleton(_securities).Singleton<ITradingCalendar>(new CalendarOf());

    private sealed class CalendarOf : ITradingCalendar
    {
        public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new TradingCalendar());

        public void Invalidate()
        {
        }
    }

    private TicketAlertObserver Observer(MiniProvider services) => new(services, new Settings(true), _time, NullLogger<TicketAlertObserver>.Instance);

    private static WatchedTicket Watched() => new(7, 3, "NSE:ALRT", "BUY", new DateOnly(2026, 10, 26), 95m, "level", 110m);

    private static LivePrice Tick(decimal price) => new(Instrument, price, Now);

    private void Arrange()
    {
        _store.ListWatchedAsync(new DateOnly(2026, 10, 26), Ct).Returns([Watched()]);
        _securities.FindAsync(Arg.Any<IReadOnlyCollection<string>>(), Ct).Returns(new Dictionary<string, InstrumentSecurity> { ["NSE:ALRT"] = new("NSE:ALRT", Instrument, "1") });
        _store.AddCrossingAsync(Arg.Any<NewCrossing>(), Ct).Returns(true);
    }

    [Fact]
    public async Task A_crossing_is_stored_once_per_kind_and_the_watched_list_is_read_once_inside_the_cache_time()
    {
        Arrange();
        using var services = Services();
        var observer = Observer(services);

        await observer.ObserveAsync([Tick(94m)], Ct);
        await observer.ObserveAsync([Tick(93m)], Ct);
        await observer.ObserveAsync([Tick(111m)], Ct);

        await _store.Received(1).AddCrossingAsync(Arg.Is<NewCrossing>(c => c.Kind == "stop_crossed" && c.Level == 95m && c.Price == 94m && c.StopAnchor == "level"), Ct);
        await _store.Received(1).AddCrossingAsync(Arg.Is<NewCrossing>(c => c.Kind == "target_crossed" && c.Level == 110m && c.StopAnchor == null), Ct);
        await _store.Received(1).ListWatchedAsync(Arg.Any<DateOnly>(), Ct);
    }

    [Fact]
    public async Task The_watched_list_is_read_again_after_the_cache_time_so_a_newly_armed_ticket_is_seen()
    {
        Arrange();
        using var services = Services();
        var observer = Observer(services);
        await observer.ObserveAsync([Tick(100m)], Ct);

        _time.Advance(TimeSpan.FromSeconds(31));
        await observer.ObserveAsync([new LivePrice(Instrument, 100m, Now.AddSeconds(31))], Ct);

        await _store.Received(2).ListWatchedAsync(Arg.Any<DateOnly>(), Ct);
    }

    [Fact]
    public async Task A_failing_database_is_swallowed_and_the_crossing_is_tried_again_by_the_next_tick()
    {
        Arrange();
        _store.AddCrossingAsync(Arg.Any<NewCrossing>(), Ct).Returns(_ => ValueTask.FromException<bool>(new InvalidOperationException("down")), _ => ValueTask.FromResult(true));
        using var services = Services();
        var observer = Observer(services);

        await observer.ObserveAsync([Tick(94m)], Ct);
        await observer.ObserveAsync([Tick(93m)], Ct);

        await _store.Received(2).AddCrossingAsync(Arg.Any<NewCrossing>(), Ct);
    }

    [Fact]
    public async Task A_tick_outside_the_session_reads_and_stores_nothing()
    {
        using var services = Services();

        await Observer(services).ObserveAsync([new LivePrice(Instrument, 90m, new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero))], Ct);

        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_setting_off_the_bell_source_answers_nothing_and_reads_nothing()
    {
        using var services = Services();
        var reader = new TicketAlertReader(_store, new CalendarOf());

        var items = await new TicketNoticeSource(reader, new Settings(false)).ListAsync(Now, Ct);

        items.ShouldBeEmpty();
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_setting_off_the_prompt_service_stores_nothing_and_says_so()
    {
        using var services = new MiniProvider().Singleton(_store).Singleton<ITicketAlertSettings>(new Settings(false));
        var service = new TicketPromptsService(services, _time, NullLogger<TicketPromptsService>.Instance);

        var outcome = await service.RunAsync(Context(), Ct);

        _store.ReceivedCalls().ShouldBeEmpty();
        outcome.Status.ShouldBe(ServiceRunStatus.Succeeded);
    }

    private static ServiceRunContext Context() =>
        new(Guid.NewGuid(), ServiceRunTrigger.Scheduled, 1, "system", new Dictionary<string, System.Text.Json.JsonElement>(), (_, _) => ValueTask.CompletedTask);

    [Fact]
    public async Task With_the_setting_on_the_prompt_service_stores_the_armed_tickets_of_the_last_three_sessions()
    {
        _store.ListArmedOnAsync(Arg.Any<DateOnly>(), Ct).Returns(call => [new NewPrompt(3, 7, Guid.Empty, call.Arg<DateOnly>())]);
        _store.AddPromptsAsync(Arg.Any<IReadOnlyList<NewPrompt>>(), Ct).Returns(1);
        using var services = new MiniProvider().Singleton(_store).Singleton<ITicketAlertSettings>(new Settings(true)).Singleton<ITradingCalendar>(new CalendarOf());
        var service = new TicketPromptsService(services, new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 11, 15, 0, TimeSpan.Zero)), NullLogger<TicketPromptsService>.Instance);

        var outcome = await service.RunAsync(Context(), Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Succeeded);
        await _store.Received(1).ListArmedOnAsync(new DateOnly(2026, 10, 26), Ct);
        await _store.Received(1).ListArmedOnAsync(new DateOnly(2026, 10, 23), Ct);
        await _store.Received(1).ListArmedOnAsync(new DateOnly(2026, 10, 22), Ct);
    }
}
