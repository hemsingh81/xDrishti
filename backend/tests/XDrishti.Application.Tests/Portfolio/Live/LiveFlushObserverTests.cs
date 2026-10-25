#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly MiniProvider _provider;

    public LiveFlushObserverTests()
    {
        _provider = new MiniProvider()
            .Singleton<ILivePrices>(_prices)
            .Singleton<ILiveQuotes>(new FakeLiveQuotes())
            .Singleton<ILiveNotifier>(new FakeLiveNotifier())
            .Singleton(_actions.Checker())
            .Singleton(_actions.Reconciler());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _provider.Dispose();

    private LiveFlush Flush(params ILiveTickObserver[] observers) => new(_provider, _actions.Clock, NullLogger<LiveFlush>.Instance, observers);

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
}
