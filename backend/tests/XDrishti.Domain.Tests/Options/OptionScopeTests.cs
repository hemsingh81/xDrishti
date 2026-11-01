using XDrishti.Domain.Market;
using XDrishti.Domain.Options;

namespace XDrishti.Domain.Tests.Options;

public sealed class OptionScopeTests
{
    [Theory]
    [InlineData("NSE", "D", "OPTIDX", true)]
    [InlineData("NSE", "D", "OPTSTK", true)]
    [InlineData("NSE", "D", "FUTIDX", false)]
    [InlineData("NSE", "D", "FUTSTK", false)]
    [InlineData("NSE", "E", "EQUITY", false)]
    [InlineData("NSE", "I", "INDEX", false)]
    [InlineData("NSE", "C", "OPTCUR", false)]
    [InlineData("NSE", "M", "OPTFUT", false)]
    [InlineData("BSE", "D", "OPTSTK", false)]
    [InlineData("BSE", "D", "OPTIDX", false)]
    [InlineData("MCX", "M", "OPTFUT", false)]
    public void Only_nse_index_and_stock_options_are_option_rows(string exchange, string segment, string instrument, bool expected)
    {
        OptionScope.MightBeOption(exchange, segment, instrument).ShouldBe(expected);
        OptionScope.IsOption(exchange, segment, instrument, "RELIANCE").ShouldBe(expected);
    }

    [Fact]
    public void An_exchange_test_symbol_is_never_an_option_row()
    {
        OptionScope.IsOption("NSE", "D", "OPTSTK", "NSETEST", "NSETEST-Oct2026-10-CE").ShouldBeFalse();
    }

    [Theory]
    [InlineData("OPTIDX", "NIFTY", "NSE:NIFTY")]
    [InlineData("OPTIDX", "BANKNIFTY", "NSE:BANKNIFTY")]
    [InlineData("OPTSTK", "RELIANCE", "NSE:RELIANCE")]
    [InlineData("OPTSTK", "M&M", "NSE:M&M")]
    public void The_underlying_is_the_index_or_share_symbol_of_the_master_list(string instrument, string ticker, string expected)
    {
        OptionScope.UnderlyingSymbol(instrument, ticker).ShouldBe(expected);
    }

    [Fact]
    public void The_master_list_scope_still_drops_every_option_row()
    {
        InstrumentScope.Classify("NSE", "D", "OPTIDX", "OP", "NIFTY").ShouldBeNull();
        InstrumentScope.Classify("NSE", "D", "OPTSTK", "OP", "RELIANCE").ShouldBeNull();
        InstrumentScope.MightBeInScope("NSE", "D", "OPTSTK").ShouldBeFalse();
    }
}
