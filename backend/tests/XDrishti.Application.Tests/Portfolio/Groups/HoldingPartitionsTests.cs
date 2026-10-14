using XDrishti.Application.Portfolio.Groups;
using static XDrishti.Application.Tests.Portfolio.Groups.GroupFixtures;

namespace XDrishti.Application.Tests.Portfolio.Groups;

/// <summary>Splitting holdings into account, bucket, sector and basket groups (ADR 0021).</summary>
public sealed class HoldingPartitionsTests
{
    private static GroupDimension Dimension(string name) => GroupDimensions.TryParse(name, out var by) ? by : throw new ArgumentException(name);

    private static Dictionary<string, string[]> SymbolsByGroup(GroupDimension by) =>
        HoldingPartitions.Partition(AllHoldings(), by, Classes()).ToDictionary(g => g.Key, g => g.Holdings.Select(h => h.TradingSymbol).Order().ToArray(), StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("account")]
    [InlineData("bucket")]
    [InlineData("sector")]
    public void An_additive_grouping_puts_every_holding_in_exactly_one_group(string by)
    {
        var groups = HoldingPartitions.Partition(AllHoldings(), Dimension(by), Classes());

        groups.SelectMany(g => g.Holdings).Select(h => h.TradingSymbol).Order().ShouldBe(["AAA", "BBB", "CCC", "DDD", "EEE"]);
    }

    [Fact]
    public void Accounts_are_keyed_by_account_id_and_named_by_the_account()
    {
        var groups = HoldingPartitions.Partition(AllHoldings(), GroupDimension.Account, Classes());

        groups.Select(g => (g.Key, g.Name)).ShouldBe([(AccountOne.ToString(), "Account One"), (AccountTwo.ToString(), "Account Two")]);
        SymbolsByGroup(GroupDimension.Account)[AccountTwo.ToString()].ShouldBe(["CCC", "DDD", "EEE"]);
    }

    [Fact]
    public void Buckets_are_keyed_by_bucket_id()
    {
        var bySymbols = SymbolsByGroup(GroupDimension.Bucket);

        bySymbols[BucketA.ToString()].ShouldBe(["AAA", "CCC", "EEE"]);
        bySymbols[BucketB.ToString()].ShouldBe(["BBB"]);
        bySymbols[BucketC.ToString()].ShouldBe(["DDD"]);
    }

    [Fact]
    public void Sectors_come_from_the_master_and_a_holding_without_one_is_unclassified_under_the_reserved_key()
    {
        var groups = HoldingPartitions.Partition(AllHoldings(), GroupDimension.Sector, Classes());

        groups.Select(g => (g.Key, g.Name)).ShouldBe([("Banks", "Banks"), ("IT", "IT"), ("~none", "Unclassified")], ignoreOrder: true);
        SymbolsByGroup(GroupDimension.Sector)["~none"].ShouldBe(["DDD"]);
    }

    [Fact]
    public void A_mapped_instrument_without_a_sector_in_the_master_is_unclassified_too()
    {
        var classes = new Classification(new Dictionary<Guid, string>(), new Dictionary<Guid, IReadOnlyList<XDrishti.Application.Abstractions.Portfolio.BasketRef>>());

        var groups = HoldingPartitions.Partition(AllHoldings(), GroupDimension.Sector, classes);

        groups.Select(g => g.Key).ShouldBe(["~none"]);
    }

    [Fact]
    public void A_holding_is_in_every_basket_that_holds_its_instrument_and_in_no_basket_otherwise()
    {
        var bySymbols = SymbolsByGroup(GroupDimension.Basket);

        bySymbols[Core.ToString()].ShouldBe(["AAA", "BBB"]);
        bySymbols[DividendBasket.ToString()].ShouldBe(["AAA"]);
        bySymbols["~none"].ShouldBe(["CCC", "DDD", "EEE"]);
        HoldingPartitions.Partition(AllHoldings(), GroupDimension.Basket, Classes()).Single(g => g.Key == "~none").Name.ShouldBe("No basket");
    }

    [Fact]
    public void Group_order_is_the_order_in_which_the_holdings_first_appear()
    {
        HoldingPartitions.Partition(AllHoldings(), GroupDimension.Bucket, Classes()).Select(g => g.Key).ShouldBe([BucketA.ToString(), BucketB.ToString(), BucketC.ToString()]);
    }

    [Theory]
    [InlineData("sector", "banks", true)]
    [InlineData("sector", "IT", false)]
    [InlineData("sector", "~none", false)]
    [InlineData("basket", "~none", false)]
    public void Membership_of_a_key_compares_ignoring_case_and_unknown_keys_match_nothing(string by, string key, bool expected)
    {
        HoldingPartitions.InGroup(Aaa(), Dimension(by), key, Classes()).ShouldBe(expected);
    }

    [Fact]
    public void A_holding_matches_its_account_bucket_and_each_of_its_baskets()
    {
        var aaa = Aaa();
        var classes = Classes();

        HoldingPartitions.InGroup(aaa, GroupDimension.Account, AccountOne.ToString(), classes).ShouldBeTrue();
        HoldingPartitions.InGroup(aaa, GroupDimension.Account, AccountTwo.ToString(), classes).ShouldBeFalse();
        HoldingPartitions.InGroup(aaa, GroupDimension.Bucket, BucketA.ToString(), classes).ShouldBeTrue();
        HoldingPartitions.InGroup(aaa, GroupDimension.Basket, Core.ToString(), classes).ShouldBeTrue();
        HoldingPartitions.InGroup(aaa, GroupDimension.Basket, DividendBasket.ToString(), classes).ShouldBeTrue();
        HoldingPartitions.InGroup(Ccc(), GroupDimension.Basket, "~none", classes).ShouldBeTrue();
    }
}
