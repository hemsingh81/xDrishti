using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;

namespace XDrishti.SimExport.Tests.Cards;

/// <summary>Criterion 7 of P3-04c, the names: C6's table of cleaned tickers, numbers and the length bound.</summary>
public sealed class CardNamesTests
{
    private static SimTrade Trade(string symbol, Side side = Side.Buy, ExitReason exit = ExitReason.Target, int day = 4) =>
        FabricatedRun.Trade(FabricatedRun.IdA, symbol, side, new DateOnly(2025, 3, day), exit);

    public static TheoryData<string, int, int, Side, ExitReason, string> Cases => new()
    {
        { "NSE:RELIANCE", 1, 10, Side.Buy, ExitReason.Target, "cards/0001-RELIANCE-buy-2025-03-04-target.html" },
        { "NSE:M&M", 2, 10, Side.Sell, ExitReason.Stop, "cards/0002-M_M-sell-2025-03-04-stop.html" },
        { "NSE:M-M", 3, 10, Side.Buy, ExitReason.SquareOff, "cards/0003-M-M-buy-2025-03-04-square_off.html" },
        { "NSE:BAJAJ-AUTO", 12, 5000, Side.Buy, ExitReason.Target, "cards/0012-BAJAJ-AUTO-buy-2025-03-04-target.html" },
        { "BSE:20MICRONS", 9999, 9999, Side.Sell, ExitReason.Stop, "cards/9999-20MICRONS-sell-2025-03-04-stop.html" },
        { "NSE:A B", 1, 1, Side.Buy, ExitReason.Target, "cards/0001-A_B-buy-2025-03-04-target.html" },
        { "NSE:../../etc", 1, 1, Side.Buy, ExitReason.Target, "cards/0001-.._.._etc-buy-2025-03-04-target.html" },
        { "NSE:<script>", 1, 1, Side.Buy, ExitReason.Target, "cards/0001-_script_-buy-2025-03-04-target.html" },
        { "NSE:ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 1, 1, Side.Buy, ExitReason.Target, "cards/0001-ABCDEFGHIJKLMNOPQRSTUVWX-buy-2025-03-04-target.html" },
        { "NSE:RELIANCE", 126000, 126000, Side.Buy, ExitReason.MaxHold, "cards/126000-RELIANCE-buy-2025-03-04-max_hold.html" },
        { "NSE:RELIANCE", 7, 126000, Side.Buy, ExitReason.Target, "cards/000007-RELIANCE-buy-2025-03-04-target.html" },
        { "NSE:RELIANCE", 217000, 217000, Side.Sell, ExitReason.Target, "cards/217000-RELIANCE-sell-2025-03-04-target.html" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_name_is_the_number_the_cleaned_ticker_the_side_the_date_and_the_exit(string symbol, int number, int count, Side side, ExitReason exit, string expected)
    {
        var name = CardNames.Of(number, count, Trade(symbol, side, exit));

        name.ShouldBe(expected);
        name.Length.ShouldBeLessThanOrEqualTo(CardNames.MaxName);
        XDrishti.SimExport.Manifest.ExportNames.IsValid(name).ShouldBeTrue();
    }

    [Fact]
    public void Two_symbols_that_clean_to_the_same_ticker_are_told_apart_by_the_number()
    {
        var a = CardNames.Of(1, 10, Trade("NSE:M&M"));
        var b = CardNames.Of(2, 10, Trade("NSE:M_M"));

        a.ShouldNotBe(b);
        a.ToUpperInvariant().ShouldNotBe(b.ToUpperInvariant(), "the writer compares names ignoring case");
    }

    [Fact]
    public void The_longest_possible_name_is_within_the_bound()
    {
        var name = CardNames.Of(999999, 999999, Trade(new string('Z', 60), Side.Sell, ExitReason.SquareOff));

        name.Length.ShouldBeLessThanOrEqualTo(80);
    }

    [Fact]
    public void A_number_outside_the_trade_count_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CardNames.Of(0, 5, Trade("NSE:X")));
        Should.Throw<ArgumentOutOfRangeException>(() => CardNames.Of(6, 5, Trade("NSE:X")));
    }
}
