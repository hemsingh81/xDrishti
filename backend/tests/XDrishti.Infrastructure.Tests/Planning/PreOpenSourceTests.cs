using NSubstitute;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Market.Bars;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Tests.Planning;

/// <summary>Spec P7-11: the pre-open read joins the existing quote and the stored day bars; whatever is missing stays null so that the rules invalidate; a failed quote request yields no price and a safe sentence.</summary>
public sealed class PreOpenSourceTests
{
    private static readonly Guid Instrument = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    private static readonly DateOnly Today = new(2026, 10, 26);

    private readonly IInstrumentSecurities _securities = Substitute.For<IInstrumentSecurities>();

    private readonly IQuoteSource _quotes = Substitute.For<IQuoteSource>();

    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PreOpenSource Source() => new(_securities, _quotes, _bars);

    private static BarRow Bar(int day, bool complete = true) =>
        new(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromMinutes(330)).AddDays(day), 100, 102, 98, 100, 1000, 375, complete, null);

    private void Arrange(QuoteResult quotes, int bars = 20)
    {
        _securities.FindAsync(Arg.Any<IReadOnlyCollection<string>>(), Ct).Returns(new Dictionary<string, InstrumentSecurity> { ["NSE:AAA"] = new("NSE:AAA", Instrument, "777") });
        _quotes.QuoteAsync(Arg.Any<IReadOnlyCollection<string>>(), Ct).Returns(quotes);
        _bars.ReadDailyBarsAsync(Instrument, Arg.Any<DateOnly>(), Today, Ct).Returns([.. Enumerable.Range(0, bars).Select(d => Bar(d))]);
    }

    [Fact]
    public async Task It_joins_the_quote_the_previous_close_and_the_range_of_the_closed_sessions()
    {
        Arrange(new QuoteResult([new SecurityQuote("777", 101.5m, 100m)]));

        var snapshot = await Source().ReadAsync(["NSE:AAA"], Today, Ct);

        snapshot.Problem.ShouldBeNull();
        var reading = snapshot.Readings["NSE:AAA"];
        reading.Last.ShouldBe(101.5);
        reading.PreviousClose.ShouldBe(100.0);
        reading.Atr.ShouldBe(4.0);
    }

    [Fact]
    public async Task It_reads_the_bars_before_today_only_and_asks_for_the_quote_of_the_mapped_security_only()
    {
        Arrange(new QuoteResult([new SecurityQuote("777", 101m, 100m)]));

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
    public async Task A_failed_quote_request_gives_no_price_and_the_safe_sentence()
    {
        Arrange(QuoteResult.Failed(new QuoteFailure(QuoteFailureKind.Failed, "Dhan did not answer as expected."), 1));

        var snapshot = await Source().ReadAsync(["NSE:AAA"], Today, Ct);

        snapshot.Problem.ShouldBe("Dhan did not answer as expected.");
        snapshot.Readings["NSE:AAA"].Last.ShouldBeNull();
    }

    [Fact]
    public async Task A_symbol_the_master_list_cannot_map_has_no_reading()
    {
        Arrange(new QuoteResult([new SecurityQuote("777", 101m, 100m)]));

        var snapshot = await Source().ReadAsync(["NSE:UNKNOWN"], Today, Ct);

        snapshot.Readings.ShouldBeEmpty();
    }
}
