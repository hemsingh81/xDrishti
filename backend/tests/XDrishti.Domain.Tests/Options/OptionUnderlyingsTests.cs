using XDrishti.Domain.Options;

namespace XDrishti.Domain.Tests.Options;

public sealed class OptionUnderlyingsTests
{
    [Fact]
    public void A_stored_list_is_read_bare_tickers_get_the_exchange_and_case_is_folded()
    {
        var listed = OptionUnderlyings.Parse("""["NSE:NIFTY", "banknifty", " NSE:RELIANCE ", "M&M"]""");

        listed.Symbols.ShouldBe(["NSE:BANKNIFTY", "NSE:M&M", "NSE:NIFTY", "NSE:RELIANCE"]);
        listed.Rejected.ShouldBeEmpty();
        listed.Contains("NSE:NIFTY").ShouldBeTrue();
        listed.Contains("NSE:TCS").ShouldBeFalse();
    }

    [Theory]
    [InlineData("""["", "NSE:", "NIFTY 50", "A;B"]""", 4)]
    [InlineData("""[1, null, "NIFTY"]""", 2)]
    public void Entries_that_are_not_a_symbol_are_dropped_and_reported_never_a_crash(string stored, int rejected)
    {
        var listed = OptionUnderlyings.Parse(stored);

        listed.Rejected.Count.ShouldBe(rejected);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"a": 1}""")]
    [InlineData("\"NIFTY\"")]
    public void A_value_that_is_not_a_list_is_the_empty_list_with_a_rejection(string stored)
    {
        var listed = OptionUnderlyings.Parse(stored);

        listed.IsEmpty.ShouldBeTrue();
        listed.Rejected.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    public void An_empty_setting_lists_nothing(string? stored)
    {
        OptionUnderlyings.Parse(stored).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void The_cheap_look_matches_the_raw_ticker_of_a_listed_symbol_only()
    {
        var listed = OptionUnderlyings.From(["NSE:NIFTY", "NSE:RELIANCE"]);

        listed.MightList("NIFTY").ShouldBeTrue();
        listed.MightList("RELIANCE").ShouldBeTrue();
        listed.MightList("NIFTYNXT50").ShouldBeFalse();
        listed.MightList("TCS").ShouldBeFalse();
    }

    [Fact]
    public void The_stored_form_round_trips()
    {
        var listed = OptionUnderlyings.From([.. OptionUnderlyings.DefaultIndices, "NSE:SBIN"]);

        OptionUnderlyings.Parse(listed.ToStored()).Symbols.ShouldBe(listed.Symbols);
        listed.Symbols.Count.ShouldBe(5);
    }
}
