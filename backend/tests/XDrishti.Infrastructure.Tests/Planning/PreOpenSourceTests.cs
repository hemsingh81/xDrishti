using NSubstitute;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Tests.Planning;

/// <summary>Spec P7-11: the pre-open read joins the existing quote (price, previous close, trade time) and the stored day bars; whatever is missing stays null so that the rules invalidate; a failed quote request reads no bars and gives a safe sentence.</summary>
public sealed class PreOpenSourceTests
{
    private static readonly Guid Instrument = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    /// <summary>A Monday: the previous trading session is Friday 2026-10-23.</summary>
    private static readonly DateOnly Today = new(2026, 10, 26);

    private static readonly DateTimeOffset TradeTime = new(2026, 10, 26, 9, 9, 30, TimeSpan.FromMinutes(330));

    private readonly IInstrumentSecurities _securities = Substitute.For<IInstrumentSecurities>();

    private readonly IQuoteSource _quotes = Substitute.For<IQuoteSource>();

    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();

    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PreOpenSource Source() => new(_securities, _quotes, _bars, _calendar);

    /// <summary>Day bars, the newest on <paramref name="newest"/>, one per calendar day backwards.</summary>
    private static BarRow Bar(DateOnly newest, int back) =>
        new(new DateTimeOffset(newest.AddDays(-back).ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330)), 100, 102, 98, 100, 1000, 375, true, null);

    private void Arrange(QuoteResult quotes, int bars = 20, DateOnly? newest = null)
    {
        var last = newest ?? new DateOnly(2026, 10, 23);
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        _securities.FindAsync(Arg.Any<IReadOnlyCollection<string>>(), Ct).Returns(new Dictionary<string, InstrumentSecurity> { ["NSE:AAA"] = new("NSE:AAA", Instrument, "777") });
        _quotes.QuoteAsync(Arg.Any<IReadOnlyCollection<string>>(), Ct).Returns(quotes);
        _bars.ReadDailyBarsAsync(Instrument, Arg.Any<DateOnly>(), Today, Ct).Returns([.. Enumerable.Range(0, bars).Reverse().Select(d => Bar(last, d))]);
    }

    [Fact]
    public async Task It_joins_the_quote_its_trade_time_the_previous_close_and_the_range_of_the_closed_sessions()
    {
        Arrange(new QuoteResult([new SecurityQuote("777", 101.5m, 100m, TradeTime, 5000)]));

        var snapshot = await Source().ReadAsync(["NSE:AAA"], Today, Ct);

        snapshot.Problem.ShouldBeNull();
        var reading = snapshot.Readings["NSE:AAA"];
        reading.Last.ShouldBe(101.5);
        reading.PreviousClose.ShouldBe(100.0);
        reading.Atr.ShouldBe(4.0);
        reading.QuoteTime.ShouldBe(TradeTime);
    }

    [Fact]
    public async Task A_quote_with_no_trade_time_gives_a_reading_without_one()
    {
        Arrange(new QuoteResult([new SecurityQuote("777", 101.5m, 100m)]));

        var snapshot = await Source().ReadAsync(["NSE:AAA"], Today, Ct);

        snapshot.Readings["NSE:AAA"].QuoteTime.ShouldBeNull();
    }

    [Fact]
    public async Task It_reads_the_bars_before_today_only_and_asks_for_the_quote_of_the_mapped_security_only()
    {
        Arrange(new QuoteResult([new SecurityQuote("777", 101m, 100m, TradeTime)]));

        await Source().ReadAsync(["NSE:AAA", "NSE:UNKNOWN"], Today, Ct);

        await _bars.Received(1).ReadDailyBarsAsync(Instrument, Today.AddDays(-PreOpenSource.LookbackDays), Today, Ct);
        await _quotes.Received(1).QuoteAsync(Arg.Is<IReadOnlyCollection<string>>(ids => ids.Count == 1 && ids.Contains("777")), Ct);
    }

    [Fact]
    public async Task A_security_missing_from_the_answer_and_too_few_bars_leave_those_parts_null()
    {
        Arrange(new QuoteResult([]), bars: 5);

        var snapshot = await Source().ReadAsync(["NSE:AAA"], Today, Ct);

        var reading = snapshot.Readings["NSE:AAA"];
        reading.Last.ShouldBeNull();
        reading.PreviousClose.ShouldBeNull();
        reading.Atr.ShouldBeNull();
    }

    [Fact]
    public async Task A_day_series_whose_newest_bar_is_not_the_previous_trading_session_gives_no_range()
    {
        Arrange(new QuoteResult([new SecurityQuote("777", 101m, 100m, TradeTime)]), newest: new DateOnly(2026, 10, 21));

        var snapshot = await Source().ReadAsync(["NSE:AAA"], Today, Ct);

        snapshot.Readings["NSE:AAA"].Atr.ShouldBeNull();
        snapshot.Readings["NSE:AAA"].Last.ShouldBe(101.0);
    }

    [Fact]
    public async Task A_failed_quote_request_gives_no_readings_reads_no_bars_and_the_safe_sentence()
    {
        Arrange(QuoteResult.Failed(new QuoteFailure(QuoteFailureKind.Failed, "Dhan did not answer as expected."), 1));

        var snapshot = await Source().ReadAsync(["NSE:AAA"], Today, Ct);

        snapshot.Problem.ShouldBe("Dhan did not answer as expected.");
        snapshot.Readings.ShouldBeEmpty();
        await _bars.DidNotReceiveWithAnyArgs().ReadDailyBarsAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task A_symbol_the_master_list_cannot_map_has_no_reading()
    {
        Arrange(new QuoteResult([new SecurityQuote("777", 101m, 100m, TradeTime)]));

        var snapshot = await Source().ReadAsync(["NSE:UNKNOWN"], Today, Ct);

        snapshot.Readings.ShouldBeEmpty();
    }
}
