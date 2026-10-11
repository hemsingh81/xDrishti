using XDrishti.Domain.Market;

namespace XDrishti.Domain.Tests.Market;

public sealed class CanonicalSymbolTests
{
    [Theory]
    [InlineData("RELIANCE", "NSE:RELIANCE")]
    [InlineData("M&M", "NSE:M&M")]
    [InlineData(" TCS ", "NSE:TCS")]
    public void A_share_or_ETF_is_NSE_and_its_ticker(string ticker, string expected) =>
        CanonicalSymbol.ForListed(ticker).ShouldBe(expected);

    [Theory]
    [InlineData("NIFTY", "NSE:NIFTY")]
    [InlineData("BANKNIFTY", "NSE:BANKNIFTY")]
    [InlineData("INDIA VIX", "NSE:INDIA_VIX")]
    [InlineData("NIFTY MIDCAP 150", "NSE:NIFTY_MIDCAP_150")]
    [InlineData("Nifty  Next 50", "NSE:NIFTY_NEXT_50")]
    [InlineData("  nifty it ", "NSE:NIFTY_IT")]
    public void An_index_is_the_upper_case_name_with_underscores(string name, string expected) =>
        CanonicalSymbol.ForIndex(name).ShouldBe(expected);

    [Theory]
    [InlineData("RELIANCE", 2026, 10, 27, "NSE:RELIANCE-2026-10-FUT")]
    [InlineData("360ONE", 2026, 1, 29, "NSE:360ONE-2026-01-FUT")]
    [InlineData("NIFTY", 2027, 12, 30, "NSE:NIFTY-2027-12-FUT")]
    public void A_future_is_the_underlying_and_the_expiry_month(string underlying, int year, int month, int day, string expected) =>
        CanonicalSymbol.ForFuture(underlying, new DateOnly(year, month, day)).ShouldBe(expected);
}
