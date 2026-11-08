using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
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
/// ADR 0223 and 0224: the liquidity tier and capacity counts of the out-of-sample trades, from stored day bars (one bounded read per instrument) and from the stored minute bar of each trade's fill minute (a bounded batched read per instrument), nothing read when the stress is off.
/// A fill minute that cannot be read is unavailable and never a pass; a breach of either cap counts; a failed or timed-out read does not stop the run.
/// </summary>
public sealed class LabTiersTests
{
    private const decimal Crore = LiquidityTiers.Crore;

    private static readonly DateOnly Entry = new(2026, 3, 2);

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    /// <summary>9:20 IST on the entry date, the first fill minute of the tests; each further trade fills one minute later.</summary>
    private static readonly long FirstFill = new DateTimeOffset(2026, 3, 2, 9, 20, 0, Ist).ToUnixTimeSeconds();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 7, 6, 0, 0, TimeSpan.Zero));

    private LabTiers Tiers(CostStressOptions? options = null) => new(_bars, options ?? new CostStressOptions(), _time, NullLogger<LabTiers>.Instance);

    private static BarRow Row(DateOnly date, decimal value) => new(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), Ist), 100, 100, 100, 100, (long)(value / 100m), 375, true, null);

    private static IReadOnlyList<BarRow> Rows(decimal median, decimal entryDay) =>
        [.. Enumerable.Range(1, 25).Reverse().Select(i => Row(Entry.AddDays(-i), median)).Append(Row(Entry, entryDay))];

    /// <summary>A trade of the instrument filled <paramref name="minute"/> minutes after 9:20.</summary>
    private static SimTrade Trade(Guid instrument, decimal? orderValue, int minute = 0) => CostTrades.W1(Entry) with
    {
        InstrumentId = instrument,
        EntryEpochSeconds = FirstFill + (60L * minute),
        Costing = orderValue is { } v ? new TradeCosting(CostSegment.IntradayEquity, "s", 1, 1m, v, v, LegCharges.Zero, LegCharges.Zero, 0m, 0, 0) : null,
    };

    private void Series(Guid instrument, IReadOnlyList<BarRow> rows) =>
        _bars.ReadDailyBarsAsync(instrument, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(rows);

    /// <summary>The stored fill minutes of an instrument: (minute after 9:20, close, volume); the traded value of a minute is close x volume.</summary>
    private void Minutes(Guid instrument, params (int Minute, double Close, long Volume)[] stored) =>
        _bars.ReadMinuteCloseVolumesAsync(instrument, Arg.Any<IReadOnlyList<DateTimeOffset>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<MinuteCloseVolume>)[.. stored
                .Select(s => new MinuteCloseVolume(DateTimeOffset.FromUnixTimeSeconds(FirstFill + (60L * s.Minute)), s.Close, s.Volume))
                .Where(m => call.Arg<IReadOnlyList<DateTimeOffset>>().Contains(m.Start))]);

    [Fact]
    public async Task Trades_are_counted_into_the_tiers_and_a_breach_of_either_cap_is_a_breach_and_every_reading_that_could_not_be_made_is_unavailable()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var none = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1000 * Crore));
        Series(b, Rows(60 * Crore, 1000 * Crore));
        Series(c, Rows(12 * Crore, 1000 * Crore));
        Series(none, []);
        Minutes(a, (0, 100, 200_000)); // Rs 2 crore minute
        Minutes(b, (0, 100, 200_000), (1, 100, 200_000));
        Minutes(c, (0, 100, 200_000));
        Minutes(none, (0, 100, 200_000));
        var sources = new[]
        {
            Trade(a, 100_000m),          // tier A, Rs 1 lakh in a Rs 2 crore minute (0.5 %): within both caps
            Trade(b, 3_000_000m),        // tier B, exactly 0.5 % of Rs 60 crore: within the median cap, but 15 % of its Rs 2 crore minute: breach of the minute cap
            Trade(b, 3_000_001m, 1),     // tier B, one rupee over the median cap: breach
            Trade(c, 50_000m),           // tier C (Rs 12 crore), within its median cap (Rs 60,000) and 0.25 % of the minute: within both caps
            Trade(none, 100_000m),       // no day bars: tier and capacity unavailable
            Trade(a, null, 1),           // tier A, order value unknown: unavailable
        };

        var view = (await Tiers().ComputeAsync(sources, Ct)).ShouldNotBeNull();

        view.Trades.ShouldBe(6);
        (view.TierA, view.TierB, view.TierC, view.TierUnavailable).ShouldBe((2, 2, 1, 1));
        view.CapacityBreaches.ShouldBe(2);
        view.CapacityUnavailable.ShouldBe(2);
        view.MinuteReads.ShouldBe(5, "every trade with an order value and day bars read (even an empty series) has its minute read; only the trade with no order value is not asked for");
        view.MinuteUnavailable.ShouldBe(1);
        view.MinuteReadLimit.ShouldBe(5000);
        view.Settings.ShouldBe(new CostStressOptions().TierKeyText());
        view.SettingsHash.ShouldBe(new CostStressOptions().TierSnapshotHash());
        view.Basis.ShouldContain("minute bar of its fill minute");
    }

    [Theory]
    [InlineData(40_000, 4_000_000, false)] // exactly 1 % of Rs 40 lakh
    [InlineData(40_001, 4_000_000, true)]  // one rupee over
    [InlineData(100_000, 4_000_000, true)] // AC6: Rs 1 lakh in a Rs 40 lakh minute is 2.5 %
    [InlineData(100_000, 20_000_000, false)] // Rs 1 lakh in a Rs 2 crore minute is 0.5 %
    public async Task The_one_percent_cap_of_the_fill_minute_is_a_breach_one_rupee_over_and_within_at_the_exact_limit(decimal order, long minuteValue, bool breach)
    {
        var a = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1000 * Crore)); // median cap Rs 2.25 crore, far above every order here
        Minutes(a, (0, 100, minuteValue / 100));

        var view = await Tiers().ComputeAsync([Trade(a, order)], Ct);

        view!.CapacityBreaches.ShouldBe(breach ? 1 : 0);
        view.CapacityUnavailable.ShouldBe(0);
        view.MinuteReads.ShouldBe(1);
        view.MinuteUnavailable.ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_minute_with_no_volume_is_unavailable_never_a_pass(long volume)
    {
        var a = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1000 * Crore));
        Minutes(a, (0, 100, volume));

        var view = await Tiers().ComputeAsync([Trade(a, 1m)], Ct);

        (view!.CapacityBreaches, view.CapacityUnavailable, view.MinuteReads, view.MinuteUnavailable).ShouldBe((0, 1, 0, 1));
    }

    [Fact]
    public async Task A_fill_minute_with_no_stored_bar_is_unavailable_and_the_others_of_the_instrument_are_read()
    {
        var a = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1000 * Crore));
        Minutes(a, (1, 100, 200_000)); // minute 0 has no bar

        var view = await Tiers().ComputeAsync([Trade(a, 100_000m), Trade(a, 100_000m, 1)], Ct);

        (view!.CapacityBreaches, view.CapacityUnavailable, view.MinuteReads, view.MinuteUnavailable).ShouldBe((0, 1, 1, 1));
    }

    [Fact]
    public async Task The_entry_day_share_is_counted_as_information_and_changes_neither_a_breach_nor_an_unavailable_reading()
    {
        var a = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1 * Crore)); // median cap Rs 22.5 lakh; 1 % of the entry day Rs 1 lakh

        var view = await Tiers().ComputeAsync([Trade(a, 2 * 100_000m), Trade(a, 100_000m, 1)], Ct);

        view!.OrdersOverEntryDayShare.ShouldBe(1, "Rs 2 lakh is over 1 % of the entry day, Rs 1 lakh exactly at it");
        view.CapacityBreaches.ShouldBe(0);
        view.CapacityUnavailable.ShouldBe(2, "the minutes are not stored here");
    }

    [Fact]
    public async Task One_bounded_day_bar_read_and_one_batched_minute_read_are_made_per_instrument_and_no_other_bar_read()
    {
        var a = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1000 * Crore));
        Minutes(a, (0, 100, 200_000));

        await Tiers().ComputeAsync([Trade(a, 1m), Trade(a, 2m, 1), Trade(a, 3m, 1)], Ct);

        await _bars.Received(1).ReadDailyBarsAsync(a, Entry.AddDays(-60), Entry.AddDays(1), Ct);
        await _bars.Received(1).ReadMinuteCloseVolumesAsync(a, Arg.Is<IReadOnlyList<DateTimeOffset>>(s => s.Count == 2), Arg.Any<CancellationToken>());
        await _bars.DidNotReceiveWithAnyArgs().ReadBarsAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task Trades_beyond_the_bound_are_not_read_and_stay_unavailable_in_a_fixed_order()
    {
        var first = new Guid("00000000-0000-0000-0000-000000000001");
        var second = new Guid("00000000-0000-0000-0000-000000000002");
        Series(first, Rows(450 * Crore, 1000 * Crore));
        Series(second, Rows(450 * Crore, 1000 * Crore));
        Minutes(first, (0, 100, 200_000), (1, 100, 200_000), (2, 100, 200_000));
        Minutes(second, (0, 100, 200_000));
        var sources = new[] { Trade(second, 1m), Trade(first, 1m, 2), Trade(first, 1m), Trade(first, 1m, 1) };

        var view = await Tiers(new CostStressOptions(FillMinuteMaxTrades: 2)).ComputeAsync(sources, Ct);

        (view!.MinuteReads, view.MinuteUnavailable, view.MinuteReadLimit).ShouldBe((2, 2, 2));
        view.CapacityUnavailable.ShouldBe(2);
        await _bars.Received(1).ReadMinuteCloseVolumesAsync(first, Arg.Is<IReadOnlyList<DateTimeOffset>>(s => s.Count == 2), Arg.Any<CancellationToken>());
        await _bars.DidNotReceive().ReadMinuteCloseVolumesAsync(second, Arg.Any<IReadOnlyList<DateTimeOffset>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_bound_of_zero_reads_no_minute_and_every_capacity_reading_is_unavailable()
    {
        var a = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1000 * Crore));
        Minutes(a, (0, 100, 200_000));

        var view = await Tiers(new CostStressOptions(FillMinuteMaxTrades: 0)).ComputeAsync([Trade(a, 1m)], Ct);

        (view!.MinuteReads, view.CapacityUnavailable).ShouldBe((0, 1));
        await _bars.DidNotReceiveWithAnyArgs().ReadMinuteCloseVolumesAsync(default, default!, Ct);
    }

    [Fact]
    public async Task More_instants_than_one_read_takes_are_read_in_batches()
    {
        var a = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1000 * Crore));
        var stored = Enumerable.Range(0, IBarQueries.MaxMinuteStartsPerRead + 5).Select(i => (i, 100d, 200_000L)).ToArray();
        Minutes(a, stored);
        var sources = Enumerable.Range(0, IBarQueries.MaxMinuteStartsPerRead + 5).Select(i => Trade(a, 1m, i)).ToList();

        var view = await Tiers(new CostStressOptions(FillMinuteMaxTrades: 20_000)).ComputeAsync(sources, Ct);

        view!.MinuteReads.ShouldBe(sources.Count);
        await _bars.Received(2).ReadMinuteCloseVolumesAsync(a, Arg.Any<IReadOnlyList<DateTimeOffset>>(), Arg.Any<CancellationToken>());
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
        Minutes(good, (0, 100, 200_000));
        _bars.ReadDailyBarsAsync(broken, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));
        var logger = new CaptureLogger();

        var view = await new LabTiers(_bars, new CostStressOptions(), _time, logger).ComputeAsync([Trade(good, 3_000_000m), Trade(broken, 1m), Trade(broken, 2m, 1)], Ct);

        view!.Trades.ShouldBe(3);
        (view.TierA, view.TierUnavailable).ShouldBe((1, 2));
        view.CapacityUnavailable.ShouldBe(2);
        view.CapacityBreaches.ShouldBe(1, "Rs 30 lakh in a Rs 2 crore minute is 15 %");
        logger.Events.ShouldBe([(LogLevel.Warning, 7096)]);
        await _bars.DidNotReceive().ReadMinuteCloseVolumesAsync(broken, Arg.Any<IReadOnlyList<DateTimeOffset>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_minute_read_of_one_instrument_makes_its_trades_unavailable_logs_it_and_does_not_stop_the_others()
    {
        var first = new Guid("00000000-0000-0000-0000-000000000001");
        var second = new Guid("00000000-0000-0000-0000-000000000002");
        Series(first, Rows(450 * Crore, 1000 * Crore));
        Series(second, Rows(450 * Crore, 1000 * Crore));
        _bars.ReadMinuteCloseVolumesAsync(first, Arg.Any<IReadOnlyList<DateTimeOffset>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));
        Minutes(second, (0, 100, 200_000));
        var logger = new CaptureLogger();

        var view = await new LabTiers(_bars, new CostStressOptions(), _time, logger).ComputeAsync([Trade(first, 1m), Trade(second, 1m)], Ct);

        (view!.MinuteReads, view.MinuteUnavailable, view.CapacityUnavailable, view.CapacityBreaches).ShouldBe((1, 1, 1, 0));
        view.TierA.ShouldBe(2, "the tier does not depend on the minute read");
        logger.Events.ShouldBe([(LogLevel.Warning, 7097)]);
    }

    [Fact]
    public async Task When_the_time_for_the_minute_reads_runs_out_the_instruments_not_yet_read_stay_unavailable_and_the_run_goes_on()
    {
        var first = new Guid("00000000-0000-0000-0000-000000000001");
        var second = new Guid("00000000-0000-0000-0000-000000000002");
        Series(first, Rows(450 * Crore, 1000 * Crore));
        Series(second, Rows(450 * Crore, 1000 * Crore));
        _bars.ReadMinuteCloseVolumesAsync(first, Arg.Any<IReadOnlyList<DateTimeOffset>>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _time.Advance(TimeSpan.FromSeconds(61)); // the read of the first instrument took longer than the 60 seconds
            return (IReadOnlyList<MinuteCloseVolume>)[new MinuteCloseVolume(DateTimeOffset.FromUnixTimeSeconds(FirstFill), 100, 200_000)];
        });
        Minutes(second, (0, 100, 200_000));
        var logger = new CaptureLogger();

        var view = await new LabTiers(_bars, new CostStressOptions(), _time, logger).ComputeAsync([Trade(first, 1m), Trade(second, 1m)], Ct);

        (view!.MinuteReads, view.MinuteUnavailable).ShouldBe((1, 1));
        logger.Events.ShouldBe([(LogLevel.Warning, 7098)]);
        await _bars.DidNotReceive().ReadMinuteCloseVolumesAsync(second, Arg.Any<IReadOnlyList<DateTimeOffset>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_read_cancelled_by_the_timeout_leaves_that_instrument_unavailable()
    {
        var a = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1000 * Crore));
        _bars.ReadMinuteCloseVolumesAsync(a, Arg.Any<IReadOnlyList<DateTimeOffset>>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<MinuteCloseVolume>>(call =>
        {
            _time.Advance(TimeSpan.FromSeconds(61));
            call.Arg<CancellationToken>().ThrowIfCancellationRequested();
            return [];
        });

        var view = await Tiers().ComputeAsync([Trade(a, 1m)], Ct);

        (view!.MinuteReads, view.MinuteUnavailable).ShouldBe((0, 1));
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
    public async Task A_minute_read_cancelled_by_the_owner_is_not_swallowed()
    {
        var a = Guid.NewGuid();
        Series(a, Rows(450 * Crore, 1000 * Crore));
        _bars.ReadMinuteCloseVolumesAsync(a, Arg.Any<IReadOnlyList<DateTimeOffset>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        using var owner = new CancellationTokenSource();
        await owner.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await Tiers().ComputeAsync([Trade(a, 1m)], owner.Token));
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
