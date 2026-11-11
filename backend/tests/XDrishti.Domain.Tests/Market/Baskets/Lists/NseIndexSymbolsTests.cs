using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Domain.Tests.Market.Baskets.Lists;

public sealed class NseIndexSymbolsTests
{
    [Theory]
    [InlineData("NIFTY", "NSE:NIFTY")]
    [InlineData("banknifty", "NSE:BANKNIFTY")]
    [InlineData("FINNIFTY", "NSE:FINNIFTY")]
    [InlineData("MIDCPNIFTY", "NSE:MIDCPNIFTY")]
    public void A_known_index_ticker_maps_to_the_canonical_symbol_of_the_master(string ticker, string canonical)
    {
        NseIndexSymbols.IsIndex(ticker).ShouldBeTrue();
        NseIndexSymbols.Canonical(ticker).ShouldBe(canonical);
    }

    [Fact]
    public void An_index_without_a_verified_canonical_symbol_is_an_index_but_maps_to_nothing_and_a_share_is_not_an_index()
    {
        NseIndexSymbols.IsIndex("NIFTYNXT50").ShouldBeTrue();
        NseIndexSymbols.Canonical("NIFTYNXT50").ShouldBeNull();
        NseIndexSymbols.IsIndex("RELIANCE").ShouldBeFalse();
    }
}
