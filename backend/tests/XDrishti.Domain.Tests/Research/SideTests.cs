using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

public sealed class SideTests
{
    [Fact]
    public void A_side_is_buy_or_sell_with_the_values_zero_and_one()
    {
        Enum.GetNames<Side>().ShouldBe(["Buy", "Sell"]);
        ((int)Side.Buy).ShouldBe(0);
        ((int)Side.Sell).ShouldBe(1);
    }

    [Fact]
    public void A_research_side_is_not_the_stored_ledger_side_although_the_names_agree()
    {
        // the two enums are mapped by name where a simulated trade meets a ledger row (ADR 0036)
        typeof(Side).ShouldNotBe(typeof(XDrishti.Domain.Portfolio.TradeSide));
        Enum.GetNames<Side>().ShouldBe(Enum.GetNames<XDrishti.Domain.Portfolio.TradeSide>());
    }
}
