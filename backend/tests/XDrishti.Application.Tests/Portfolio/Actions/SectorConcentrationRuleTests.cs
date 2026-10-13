using System.Text.RegularExpressions;
using XDrishti.Application.Portfolio.Actions;
using XDrishti.Application.Portfolio.Actions.Rules;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Actions.ActionFixtures;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>Sector concentration (acceptance criteria 3 and 8): one item per account and sector above 35 % of the account's priced value; Unclassified never fires; ids that are safe in a URL path whatever the sector is called.</summary>
public sealed partial class SectorConcentrationRuleTests
{
    private static readonly Dictionary<string, string> Banks = new() { ["A"] = "Banks", ["B"] = "Banks", ["C"] = "IT" };

    [Fact]
    public void A_sector_above_the_limit_gives_one_item_with_the_share_and_the_largest_holding()
    {
        // Banks 3,000 + 1,000 = 40 % of 10,000; IT 30 %; D has no sector (30 %).
        var holdings = new[] { Pos("A", 300m), Pos("B", 100m), Pos("C", 300m), Pos("D", 300m) };

        var hit = SectorConcentrationRule.Evaluate(Context(holdings, sectors: new Dictionary<string, string> { ["A"] = "Banks", ["B"] = "Banks", ["C"] = "IT" })).ShouldHaveSingleItem();

        hit.Item.Rule.ShouldBe("sector_concentration");
        (hit.Item.Severity, hit.Item.HoldingId, hit.Item.InstrumentId, hit.Item.Symbol).ShouldBe(("medium", null, null, null));
        (hit.Item.AccountId, hit.Item.AccountName).ShouldBe((AccountOne, "Account One"));
        var n = hit.Item.Numbers;
        (n.Sector, n.SectorSharePct, n.ThresholdPct, n.SectorValue, n.Positions, n.LargestSymbol, n.LargestValue).ShouldBe(("Banks", 40m, 35m, 4000m, 2, "A", 3000m));
        n.LargestHoldingId.ShouldBe(holdings[0].Id);
        hit.Item.Detail.ShouldBe("Banks is 40.0 % of Account One's value (limit 35.0 %); the largest holding is A (₹3,000)");
        hit.Item.Suggestions.ShouldBe(["snooze", "done"]);
    }

    [Fact]
    public void A_share_exactly_at_the_limit_does_not_fire()
    {
        var holdings = new[] { Pos("A", 350m), Pos("C", 650m) };

        SectorConcentrationRule.Evaluate(Context(holdings, sectors: Banks)).Count.ShouldBe(1); // IT is 65 %
        SectorConcentrationRule.Evaluate(Context([Pos("A", 350m), Pos("D", 325m), Pos("E", 325m)], sectors: new Dictionary<string, string> { ["A"] = "Banks" })).ShouldBeEmpty();
    }

    [Fact]
    public void Unclassified_never_fires_however_large()
    {
        var hits = SectorConcentrationRule.Evaluate(Context([Pos("A", 100m), Pos("B", 100m)]));

        hits.ShouldBeEmpty();
    }

    [Fact]
    public void A_holding_not_in_the_master_adds_to_unclassified_and_not_to_a_sector()
    {
        var hits = SectorConcentrationRule.Evaluate(Context([Pos("A", 100m), Pos("X", 900m, mapped: false)], sectors: new Dictionary<string, string> { ["A"] = "Banks" }));

        hits.ShouldBeEmpty(); // Banks is 10 % of the account
    }

    [Fact]
    public void An_unpriced_holding_adds_no_value_to_its_sector_or_to_the_account()
    {
        var hits = SectorConcentrationRule.Evaluate(Context([Pos("A", 100m), Pos("B", 0m, qty: 1000), Pos("C", 100m)], sectors: new Dictionary<string, string> { ["A"] = "Banks", ["B"] = "Banks", ["C"] = "IT" }));

        hits.Select(h => h.Item.Numbers.Sector).ShouldBe(["Banks", "IT"], ignoreOrder: true); // 50 % each of the priced value
    }

    [Fact]
    public void Sector_spellings_that_differ_in_case_are_one_sector()
    {
        var hits = SectorConcentrationRule.Evaluate(Context([Pos("A", 300m), Pos("B", 300m), Pos("C", 400m)], sectors: new Dictionary<string, string> { ["A"] = "Banks", ["B"] = "banks", ["C"] = "IT" }));

        hits.Count.ShouldBe(2);
        hits.Single(h => h.Item.Numbers.SectorSharePct == 60m).Item.Numbers.Positions.ShouldBe(2);
    }

    [Fact]
    public void Each_account_is_judged_on_its_own_value()
    {
        var holdings = new[] { Pos("A", 100m), Pos("B", 100m, account: AccountTwo), Pos("C", 900m, account: AccountTwo) };

        var hits = SectorConcentrationRule.Evaluate(Context(holdings, sectors: new Dictionary<string, string> { ["A"] = "Banks", ["B"] = "Banks", ["C"] = "IT" }));

        hits.Select(h => (h.Item.AccountId, h.Item.Numbers.Sector)).ShouldBe([(AccountOne, "Banks"), (AccountTwo, "IT")], ignoreOrder: true);
        hits.Select(h => h.Item.Id).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void The_concentration_limit_is_a_threshold()
    {
        var holdings = new[] { Pos("A", 200m), Pos("C", 800m) };

        var hits = SectorConcentrationRule.Evaluate(Context(holdings, ActionThresholds.Defaults with { ConcentrationPct = 15m }, Banks));

        hits.Select(h => h.Item.Numbers.Sector).ShouldBe(["Banks", "IT"], ignoreOrder: true);
    }

    [Fact]
    public void The_id_is_the_account_and_a_hash_of_the_lower_case_sector()
    {
        var holdings = new[] { Pos("A", 100m) };

        var a = SectorConcentrationRule.Evaluate(Context(holdings, sectors: new Dictionary<string, string> { ["A"] = "Banks" })).Single();
        var b = SectorConcentrationRule.Evaluate(Context(holdings, sectors: new Dictionary<string, string> { ["A"] = "BANKS" })).Single();

        a.Item.Id.ShouldBe($"sector_concentration:{AccountOne}:9b20934eac1c"); // SHA-1 of "banks"
        b.Item.Id.ShouldBe(a.Item.Id);
    }

    [Theory]
    [InlineData("Banks")]
    [InlineData("a:b/c")]
    [InlineData("इक्विटी ₹ ünï")]
    [InlineData("Oil & Gas?x=1#frag %2F ..")]
    public void The_id_is_safe_in_a_url_path_whatever_the_sector_is_called(string sector)
    {
        var hit = SectorConcentrationRule.Evaluate(Context([Pos("A", 100m)], sectors: new Dictionary<string, string> { ["A"] = sector })).Single();

        IdShape().IsMatch(hit.Item.Id).ShouldBeTrue(hit.Item.Id);
        hit.Item.Numbers.Sector.ShouldBe(sector);
        ActionKey.Parse(hit.Item.Id)!.Value.Id.ShouldBe(hit.Item.Id);
    }

    [Theory]
    [InlineData("a:b/c", "7278ea9753c7")]
    [InlineData("इक्विटी ₹ ünï", "6a7fc253d934")]
    public void The_hash_is_the_first_12_hex_characters_of_the_sha1_of_the_lower_case_name(string sector, string expected)
    {
        ActionKey.SectorHash(sector).ShouldBe(expected);
    }

    [GeneratedRegex("^sector_concentration:[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}:[0-9a-f]{12}$")]
    private static partial Regex IdShape();
}
