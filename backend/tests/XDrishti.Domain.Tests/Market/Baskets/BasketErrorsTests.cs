using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Domain.Tests.Market.Baskets;

public sealed class BasketErrorsTests
{
    [Fact]
    public void A_system_basket_refusal_is_a_conflict_with_the_stable_code()
    {
        BasketErrors.System.Code.ShouldBe("baskets.system");
        BasketErrors.System.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public void The_basket_kinds_are_the_wire_values()
    {
        (BasketKinds.Manual, BasketKinds.Portfolio).ShouldBe(("manual", "portfolio"));
    }
}
