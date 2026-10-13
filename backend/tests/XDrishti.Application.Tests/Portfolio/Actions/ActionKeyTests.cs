using XDrishti.Application.Portfolio.Actions;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>The id of an item (acceptance criterion 8): built from the rule and the key, read back from a request, and refusing anything else.</summary>
public sealed class ActionKeyTests
{
    private static readonly Guid Holding = Guid.Parse("11111111-2222-7333-8444-555555555555");

    private static readonly Guid Account = Guid.Parse("aaaaaaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee");

    [Fact]
    public void A_holding_rule_id_is_the_rule_and_the_holding()
    {
        var key = ActionKey.ForHolding("below_cost", Holding);

        key.Id.ShouldBe("below_cost:11111111-2222-7333-8444-555555555555");
        key.HoldingId.ShouldBe(Holding);
        key.AccountId.ShouldBeNull();
    }

    [Fact]
    public void A_concentration_id_is_the_rule_the_account_and_a_hash_of_the_sector()
    {
        var key = ActionKey.ForSector(Account, "Banks");

        key.Id.ShouldBe("sector_concentration:aaaaaaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee:9b20934eac1c");
        key.AccountId.ShouldBe(Account);
        key.HoldingId.ShouldBeNull();
    }

    [Theory]
    [InlineData("below_cost:11111111-2222-7333-8444-555555555555")]
    [InlineData("large_gain:11111111-2222-7333-8444-555555555555")]
    [InlineData("overweight:11111111-2222-7333-8444-555555555555")]
    [InlineData("long_term_soon:11111111-2222-7333-8444-555555555555")]
    [InlineData("lagging_nifty:11111111-2222-7333-8444-555555555555")]
    [InlineData("not_in_basket:11111111-2222-7333-8444-555555555555")]
    [InlineData("sector_concentration:aaaaaaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee:9b20934eac1c")]
    public void Every_id_reads_back_to_itself(string id)
    {
        ActionKey.Parse(id)!.Value.Id.ShouldBe(id);
    }

    [Fact]
    public void An_upper_case_id_is_read_in_its_canonical_lower_case_spelling()
    {
        ActionKey.Parse("BELOW_COST:11111111-2222-7333-8444-555555555555").ShouldBeNull(); // rule names are exact
        ActionKey.Parse("below_cost:11111111-2222-7333-8444-55555555555F")!.Value.Id.ShouldBe("below_cost:11111111-2222-7333-8444-55555555555f");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("below_cost")]
    [InlineData("below_cost:")]
    [InlineData(":11111111-2222-7333-8444-555555555555")]
    [InlineData("unknown_rule:11111111-2222-7333-8444-555555555555")]
    [InlineData("below_cost:not-a-guid")]
    [InlineData("below_cost:11111111-2222-7333-8444-555555555555:extra")]
    [InlineData("sector_concentration:11111111-2222-7333-8444-555555555555")]
    [InlineData("sector_concentration:11111111-2222-7333-8444-555555555555:short")]
    [InlineData("sector_concentration:11111111-2222-7333-8444-555555555555:zzzzzzzzzzzz")]
    [InlineData("sector_concentration:not-a-guid:9b20934eac1c")]
    [InlineData("below_cost:{11111111-2222-7333-8444-555555555555}")]
    public void Anything_else_is_not_an_id(string? id)
    {
        ActionKey.Parse(id).ShouldBeNull();
    }
}
