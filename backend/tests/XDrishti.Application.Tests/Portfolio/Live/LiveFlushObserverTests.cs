#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging;
using NSubstitute;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Live;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Application.Tests.Portfolio.Actions;

namespace XDrishti.Application.Tests.Portfolio.Live;

/// <summary>Spec P7-20 criterion 10: the observers of the flush get only firm ticks, none registered means no work, and an observer that throws never costs a price.</summary>
public sealed class LiveFlushObserverTests : IDisposable
{
    private readonly ActionsHarness _actions = new();
    private readonly FakeLivePrices _prices = new();
    private readonly FakeLiveNotifier _notifier = new();
    private readonly EventLog _log = new();
    private readonly MiniProvider _provider;

    public LiveFlushObserverTests()
    {
        _provider = new MiniProvider()
            .Singleton<ILivePrices>(_prices)
            .Singleton<ILiveQuotes>(new FakeLiveQuotes())
            .Singleton<ILiveNotifier>(_notifier)
            .Singleton(_actions.Checker())
            .Singleton(_actions.Reconciler());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _provider.Dispose();

    private LiveFlush Flush(params ILiveTickObserver[] observers) => new(_provider, _actions.Clock, _log, observers);

    private LivePrice Tick(string symbol, decimal price, double secondsAfterNow = -1) => new(ActionFixtures.InstrumentOf(symbol), price, _actions.Now.AddSeconds(secondsAfterNow));

    [Fact]
    public async Task An_observer_gets_the_firm_ticks_of_the_flush_and_not_the_indicative_ones()
    {
        var observer = Substitute.For<ILiveTickObserver>();
        var flush = Flush(observer);
        flush.Add(Tick("INFY", 1500m));
        flush.Add(new LivePrice(ActionFixtures.InstrumentOf("TCS"), 3000m, _actions.Now.Date.AddHours(1))); // before 09:15 India

        await flush.FlushAsync(Ct);

        await observer.Received(1).ObserveAsync(Arg.Is<IReadOnlyList<LivePrice>>(t => t.Count == 1 && t[0].Price == 1500m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_observer_that_throws_costs_no_price_and_does_not_fail_the_flush()
    {
        var observer = Substitute.For<ILiveTickObserver>();
        observer.ObserveAsync(Arg.Any<IReadOnlyList<LivePrice>>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromException(new InvalidOperationException("database down")));
        _prices.Held.Add(ActionFixtures.InstrumentOf("INFY"));
        var flush = Flush(observer);
        flush.Add(Tick("INFY", 1500m));

        var result = await flush.FlushAsync(Ct);

        result.Failed.ShouldBeFalse();
        _prices.Applied.Count.ShouldBe(1);
    }

    [Fact]
    public async Task With_no_observer_registered_the_flush_does_the_same_work_as_before()
    {
        _prices.Held.Add(ActionFixtures.InstrumentOf("INFY"));
        var flush = Flush();
        flush.Add(Tick("INFY", 1500m));

        var result = await flush.FlushAsync(Ct);

        (result.Failed, _prices.Applied.Count).ShouldBe((false, 1));
    }

    private sealed class EventLog : ILogger<LiveFlush>
    {
        public List<int> Ids { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Ids.Add(eventId.Id);
    }

    private sealed class StuckObserver : ILiveTickObserver
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public async ValueTask ObserveAsync(IReadOnlyList<LivePrice> firmTicks, CancellationToken cancellationToken)
        {
            Calls++;
            Entered.TrySetResult();
            await Release.Task; // ignores the token, like a pool wait that never ends
        }
    }

    [Fact]
    public async Task An_observer_that_ignores_its_token_is_cut_off_by_the_timeout_after_prices_and_notify_and_the_flush_is_not_failed()
    {
        var stuck = new StuckObserver();
        _prices.Held.Add(ActionFixtures.InstrumentOf("INFY"));
        var flush = Flush(stuck);
        flush.Add(Tick("INFY", 1500m));

        var running = flush.FlushAsync(Ct).AsTask();
        await stuck.Entered.Task.WaitAsync(Ct);
        (_prices.Applied.Count, _notifier.Prices).ShouldBe((1, 1)); // written and notified before the observer was even called
        _actions.Clock.Advance(TimeSpan.FromSeconds(3));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        result.Failed.ShouldBeFalse();
        result.Notified.ShouldBeTrue();
        _log.Ids.ShouldContain(4894);
        stuck.Release.SetResult();
    }

    [Fact]
    public async Task A_stuck_observer_is_not_called_again_while_it_still_runs()
    {
        var stuck = new StuckObserver();
        var flush = Flush(stuck);
        flush.Add(Tick("INFY", 1500m, -3));
        var first = flush.FlushAsync(Ct).AsTask();
        await stuck.Entered.Task.WaitAsync(Ct);
        _actions.Clock.Advance(TimeSpan.FromSeconds(3));
        await first.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        flush.Add(Tick("INFY", 1501m, -1));
        await flush.FlushAsync(Ct);

        stuck.Calls.ShouldBe(1);
        stuck.Release.SetResult();
    }

    [Fact]
    public async Task A_second_observer_still_runs_when_the_first_throws()
    {
        var first = Substitute.For<ILiveTickObserver>();
        first.ObserveAsync(Arg.Any<IReadOnlyList<LivePrice>>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromException(new InvalidOperationException("down")));
        var second = Substitute.For<ILiveTickObserver>();
        var flush = Flush(first, second);
        flush.Add(Tick("INFY", 1500m));

        var result = await flush.FlushAsync(Ct);

        result.Failed.ShouldBeFalse();
        await second.Received(1).ObserveAsync(Arg.Any<IReadOnlyList<LivePrice>>(), Arg.Any<CancellationToken>());
        _log.Ids.ShouldContain(4894);
    }
}
