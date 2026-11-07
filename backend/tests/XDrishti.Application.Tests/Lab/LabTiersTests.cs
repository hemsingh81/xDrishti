using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Lab;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Lab;

/// <summary>
/// ADR 0223: the liquidity tier and capacity counts of the out-of-sample trades, from stored day bars, one bounded read per instrument, nothing read when the stress is off. The fill minute is not read, so the 1 % cap reads unavailable and no order is within capacity
/// from day bars; a breach of the measured 0.5 % of the median still counts; a failed read of one instrument does not stop the run.
/// </summary>
public sealed class LabTiersTests
{
    private const decimal Crore = LiquidityTiers.Crore;

    private static readonly DateOnly Entry = new(2026, 3, 2);

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();

    private LabTiers Tiers(CostStressOptions? options = null) => new(_bars, options ?? new CostStressOptions(), NullLogger<LabTiers>.Instance);

    private static BarRow Row(DateOnly date, decimal value) => new(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), Ist), 100, 100, 100, 100, (long)(value / 100m), 375, true, null);

    private static IReadOnlyList<BarRow> Rows(decimal median, decimal entryDay) =>
        [.. Enumerable.Range(1, 25).Reverse().Select(i => Row(Entry.AddDays(-i), median)).Append(Row(Entry, entryDay))];

    private static SimTrade Trade(Guid instrument, decimal? orderValue) => CostTrades.W1(Entry) with
    {
        InstrumentId = instrument,
        Costing = orderValue is { } v ? new TradeCosting(CostSegment.IntradayEquity, "s", 1, 1m, v, v, LegCharges.Zero, LegCharges.Zero, 0m, 0, 0) : null,
    };

    private void Series(Guid instrument, IReadOnlyList<BarRow> rows) =>
        _bars.ReadDailyBarsAsync(instrument, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(rows);

    [Fact]
    public async Task Trades_are_counted_into_the_tiers_and_only_a_measured_breach_is_a_breach_every_other_capacity_reading_is_unavailable()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var none = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1000 * Crore));
        Series(b, Rows(60 * Crore, 1000 * Crore));
        Series(c, Rows(12 * Crore, 1000 * Crore));
        Series(none, []);
        var sources = new[]
        {
            Trade(a, 100_000m),          // tier A, far inside the median cap: unavailable, the fill minute is not read
            Trade(b, 3_000_000m),        // tier B, exactly 0.5 % of Rs 60 crore: within that cap, still unavailable
            Trade(b, 3_000_001m),        // tier B, one rupee over: breach
            Trade(c, 50_000m),           // tier C (Rs 12 crore), within its median cap (Rs 60,000): unavailable
            Trade(none, 100_000m),       // no bars: tier and capacity unavailable
            Trade(a, null),              // tier A, order value unknown: unavailable
        };

        var view = (await Tiers().ComputeAsync(sources, Ct)).ShouldNotBeNull();

        view.Trades.ShouldBe(6);
        (view.TierA, view.TierB, view.TierC, view.TierUnavailable).ShouldBe((2, 2, 1, 1));
        view.CapacityBreaches.ShouldBe(1);
        view.CapacityUnavailable.ShouldBe(5, "no trade is within capacity from day bars");
        view.Settings.ShouldBe(new CostStressOptions().TierKeyText());
        view.SettingsHash.ShouldBe(new CostStressOptions().TierSnapshotHash());
        view.Basis.ShouldContain("NOT read: the fill minute's traded value");
    }

    [Fact]
    public async Task The_entry_day_share_is_counted_as_information_and_changes_neither_a_breach_nor_an_unavailable_reading()
    {
        var a = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1 * Crore)); // median cap Rs 22.5 lakh; 1 % of the entry day Rs 1 lakh

        var view = await Tiers().ComputeAsync([Trade(a, 2 * 100_000m), Trade(a, 100_000m)], Ct);

        view!.OrdersOverEntryDayShare.ShouldBe(1, "Rs 2 lakh is over 1 % of the entry day, Rs 1 lakh exactly at it");
        view.CapacityBreaches.ShouldBe(0);
        view.CapacityUnavailable.ShouldBe(2);
    }

    [Fact]
    public async Task One_bounded_day_bar_read_is_made_per_instrument_and_never_a_minute_bar()
    {
        var a = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1000 * Crore));

        await Tiers().ComputeAsync([Trade(a, 1m), Trade(a, 2m), Trade(a, 3m)], Ct);

        await _bars.Received(1).ReadDailyBarsAsync(a, Entry.AddDays(-60), Entry.AddDays(1), Ct);
        await _bars.DidNotReceiveWithAnyArgs().ReadBarsAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task The_cuts_come_from_the_settings()
    {
        var a = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1000 * Crore));

        var view = await Tiers(new CostStressOptions(TierACutCrore: 500m, TierBCutCrore: 400m)).ComputeAsync([Trade(a, 1m)], Ct);

        (view!.TierA, view.TierB).ShouldBe((0, 1));
    }

    [Fact]
    public async Task A_failed_read_of_one_instruments_bars_makes_its_trades_unavailable_logs_it_and_does_not_stop_the_others()
    {
        var good = Guid.NewGuid();
        var broken = Guid.NewGuid();
        Series(good, Rows(450 * Crore, 1000 * Crore));
        _bars.ReadDailyBarsAsync(broken, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));
        var logger = new CaptureLogger();

        var view = await new LabTiers(_bars, new CostStressOptions(), logger).ComputeAsync([Trade(good, 3_000_000m), Trade(broken, 1m), Trade(broken, 2m)], Ct);

        view!.Trades.ShouldBe(3);
        (view.TierA, view.TierUnavailable).ShouldBe((1, 2));
        view.CapacityUnavailable.ShouldBe(3);
        logger.Events.ShouldBe([(LogLevel.Warning, 7096)]);
    }

    private sealed class CaptureLogger : ILogger<LabTiers>
    {
        public List<(LogLevel Level, int Id)> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Events.Add((logLevel, eventId.Id));
    }

    [Fact]
    public async Task A_cancelled_read_is_not_swallowed()
    {
        var a = Guid.NewGuid();
        _bars.ReadDailyBarsAsync(a, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(async () => await Tiers().ComputeAsync([Trade(a, 1m)], Ct));
    }

    [Fact]
    public async Task With_the_stress_off_or_no_sources_nothing_is_read_and_there_is_no_block()
    {
        var a = Guid.NewGuid();

        (await Tiers(new CostStressOptions(ConfidenceMode.Off)).ComputeAsync([Trade(a, 1m)], Ct)).ShouldBeNull();
        (await Tiers().ComputeAsync(null, Ct)).ShouldBeNull();

        _bars.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task No_trade_is_a_block_of_zero_trades_that_row_14_reads_as_unavailable()
    {
        var view = await Tiers().ComputeAsync([], Ct);

        view!.Trades.ShouldBe(0);
    }
}
