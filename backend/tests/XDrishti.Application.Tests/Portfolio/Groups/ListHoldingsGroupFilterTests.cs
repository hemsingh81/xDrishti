using XDrishti.Application.Portfolio;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Groups.GroupFixtures;

namespace XDrishti.Application.Tests.Portfolio.Groups;

/// <summary>The holdings list narrowed to one group with <c>groupBy</c> and <c>groupKey</c> (ADR 0021 §4): the children of an expanded group row.</summary>
public sealed class ListHoldingsGroupFilterTests
{
    private readonly GroupsHarness _fx = new();

    private async Task<string[]> SymbolsAsync(string? groupBy, string? groupKey, string? sort = "symbol")
    {
        var result = await _fx.ListAsync(groupBy, groupKey, sort);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return [.. result.Value.Items.Select(i => i.Symbol)];
    }

    [Theory]
    [InlineData("account", "00000000-0000-7000-8000-0000000000a1", new[] { "AAA", "BBB" })]
    [InlineData("account", "00000000-0000-7000-8000-0000000000a2", new[] { "CCC", "DDD", "EEE" })]
    [InlineData("bucket", "00000000-0000-7000-8000-0000000000b1", new[] { "AAA", "CCC", "EEE" })]
    [InlineData("bucket", "00000000-0000-7000-8000-0000000000b3", new[] { "DDD" })]
    [InlineData("sector", "Banks", new[] { "AAA", "BBB", "EEE" })]
    [InlineData("sector", "banks", new[] { "AAA", "BBB", "EEE" })]
    [InlineData("sector", "IT", new[] { "CCC" })]
    [InlineData("sector", "~none", new[] { "DDD" })]
    [InlineData("basket", "00000000-0000-7000-8000-0000000000c1", new[] { "AAA", "BBB" })]
    [InlineData("basket", "00000000-0000-7000-8000-0000000000c2", new[] { "AAA" })]
    [InlineData("basket", "~none", new[] { "CCC", "DDD", "EEE" })]
    [InlineData("BASKET", "~none", new[] { "CCC", "DDD", "EEE" })]
    public async Task A_group_key_keeps_only_the_holdings_of_that_group(string groupBy, string groupKey, string[] expected)
    {
        (await SymbolsAsync(groupBy, groupKey)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("sector", "Energy")]
    [InlineData("sector", "none")]
    [InlineData("basket", "00000000-0000-7000-8000-0000000000c9")]
    [InlineData("basket", "not-a-guid")]
    [InlineData("account", "not-a-guid")]
    [InlineData("bucket", "00000000-0000-7000-8000-0000000000a1")]
    public async Task An_unknown_key_gives_an_empty_page_not_an_error(string groupBy, string groupKey)
    {
        var result = await _fx.ListAsync(groupBy, groupKey);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.Items.Count, result.Value.Total).ShouldBe((0, 0));
    }

    [Fact]
    public async Task Paging_and_sorting_work_inside_the_group()
    {
        var first = await _fx.Lister().HandleAsync(new ListHoldingsQuery(new HoldingsRequest(null, null, null, null, "-value", 1, 2, "sector", "Banks")), CancellationToken.None);
        var second = await _fx.Lister().HandleAsync(new ListHoldingsQuery(new HoldingsRequest(null, null, null, null, "-value", 2, 2, "sector", "Banks")), CancellationToken.None);

        first.Value.Items.Select(i => i.Symbol).ShouldBe(["AAA", "BBB"]); // 6,000 / 1,800
        second.Value.Items.Select(i => i.Symbol).ShouldBe(["EEE"]);
        (first.Value.Total, second.Value.Total).ShouldBe((3, 3));
    }

    [Fact]
    public async Task The_other_filters_still_apply_with_a_group()
    {
        var byAccount = await _fx.ListAsync("sector", "Banks", account: AccountOne);
        var byText = await _fx.Lister().HandleAsync(new ListHoldingsQuery(new HoldingsRequest(null, null, null, "ee", null, null, null, "sector", "Banks")), CancellationToken.None);

        byAccount.Value.Items.Select(i => i.Symbol).ShouldBe(["AAA", "BBB"], ignoreOrder: true);
        byText.Value.Items.Select(i => i.Symbol).ShouldBe(["EEE"]);
    }

    [Fact]
    public async Task The_children_of_a_group_are_the_holdings_the_group_counts()
    {
        var groups = await _fx.OkAsync(GroupsHarness.Req("basket"));

        foreach (var group in groups.Groups)
        {
            var children = await _fx.ListAsync("basket", group.Key);
            children.Value.Total.ShouldBe(group.Holdings, group.Name);
        }
    }

    [Fact]
    public async Task With_a_group_and_no_account_only_the_accounts_that_count_in_totals_are_listed_like_the_groups()
    {
        await _fx.ListAsync("sector", "Banks");
        _fx.Queries.LastFilter!.TotalsOnly.ShouldBeTrue();

        await _fx.ListAsync("sector", "Banks", account: AccountOne);
        _fx.Queries.LastFilter!.TotalsOnly.ShouldBeFalse();

        await _fx.ListAsync(null, null);
        _fx.Queries.LastFilter!.TotalsOnly.ShouldBeFalse(); // the flat list is unchanged
    }

    [Fact]
    public async Task Only_the_classification_the_group_needs_is_read()
    {
        await _fx.ListAsync("account", AccountOne.ToString());
        (_fx.Classifications.SectorReads, _fx.Classifications.BasketReads).ShouldBe((0, 0));

        await _fx.ListAsync("sector", "Banks");
        (_fx.Classifications.SectorReads, _fx.Classifications.BasketReads).ShouldBe((1, 0));

        await _fx.ListAsync("basket", "~none");
        (_fx.Classifications.SectorReads, _fx.Classifications.BasketReads).ShouldBe((1, 1));
    }

    [Fact]
    public async Task Without_a_group_nothing_is_read_from_the_master_or_the_baskets()
    {
        var result = await _fx.ListAsync(null, null);

        result.Value.Total.ShouldBe(5);
        (_fx.Classifications.SectorReads, _fx.Classifications.BasketReads).ShouldBe((0, 0));
    }

    [Theory]
    [InlineData("bogus", "x", "groupBy")]
    [InlineData("none", "x", "groupBy")]
    [InlineData("sector", null, "groupKey")]
    [InlineData("sector", "  ", "groupKey")]
    [InlineData(null, "Banks", "groupBy")]
    public async Task A_bad_or_incomplete_group_is_a_400_naming_the_field(string? groupBy, string? groupKey, string field)
    {
        var result = await _fx.ListAsync(groupBy, groupKey);

        result.Error.Code.ShouldBe(PortfolioErrors.InvalidCode);
        PortfolioFixture.FieldsOf(result).ShouldContain(field);
    }

    [Fact]
    public async Task A_group_key_that_is_too_long_or_has_control_characters_is_a_400()
    {
        PortfolioFixture.FieldsOf(await _fx.ListAsync("sector", new string('x', 101))).ShouldBe(["groupKey"]);
        PortfolioFixture.FieldsOf(await _fx.ListAsync("sector", "Ba\nnks")).ShouldBe(["groupKey"]);
    }
}
