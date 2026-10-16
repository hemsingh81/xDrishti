using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Groups;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Groups.GroupFixtures;
using static XDrishti.Application.Tests.Portfolio.Groups.GroupsHarness;

namespace XDrishti.Application.Tests.Portfolio.Groups;

/// <summary>The holding groups read model over made-up holdings (see <see cref="GroupFixtures"/>): figures per group, the total, weights, sorting and validation.</summary>
public sealed class GetHoldingGroupsHandlerTests
{
    private readonly GroupsHarness _fx = new();

    private static HoldingGroup By(HoldingGroups groups, string key) => groups.Groups.Single(g => g.Key == key);

    [Fact]
    public async Task Grouping_by_account_gives_the_figures_of_each_account_and_marks_the_grouping_additive()
    {
        var groups = await _fx.OkAsync(Req("account", sort: "name"));

        (groups.By, groups.Additive, groups.StackBy).ShouldBe(("account", true, null));
        var one = groups.Groups[0];
        (one.Key, one.Name, one.Holdings, one.Unpriced).ShouldBe((AccountOne.ToString(), "Account One", 2, 0));
        (one.Invested, one.Value, one.Pnl, one.Realised).ShouldBe((7000m, 7800m, 800m, 0m));
        one.PnlPct.ShouldNotBeNull().ShouldBe(11.4286m, 0.0001m);
        var two = groups.Groups[1];
        (two.Holdings, two.Unpriced, two.Invested, two.Value, two.Pnl).ShouldBe((3, 1, 5800m, 8500m, 2700m)); // DDD has no price: left out of invested, value and P&L, counted as unpriced
    }

    [Fact]
    public async Task Weights_are_shares_of_the_scope_value_of_the_priced_holdings()
    {
        var groups = await _fx.OkAsync(Req("account", sort: "name"));

        groups.Groups[0].WeightPct.ShouldNotBeNull().ShouldBe(47.8528m, 0.0001m); // 7,800 of 16,300
        groups.Groups[1].WeightPct.ShouldNotBeNull().ShouldBe(52.1472m, 0.0001m);
        groups.Total.WeightPct.ShouldBe(100m);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("bucket")]
    [InlineData("sector")]
    public async Task For_the_additive_groupings_the_groups_add_up_to_the_total(string by)
    {
        var groups = await _fx.OkAsync(Req(by));

        groups.Additive.ShouldBeTrue();
        groups.Groups.Sum(g => g.Invested).ShouldBe(groups.Total.Invested);
        groups.Groups.Sum(g => g.Value ?? 0m).ShouldBe(groups.Total.Value!.Value);
        groups.Groups.Sum(g => g.Pnl ?? 0m).ShouldBe(groups.Total.Pnl!.Value);
        groups.Groups.Sum(g => g.Holdings).ShouldBe(groups.Total.Holdings);
        groups.Groups.Sum(g => g.Unpriced).ShouldBe(groups.Total.Unpriced);
        groups.Groups.Sum(g => g.WeightPct ?? 0m).ShouldBe(100m, 0.001m);
    }

    [Fact]
    public async Task The_total_row_has_the_scope_figures_and_the_group_is_the_same_as_the_summary_would_say()
    {
        var groups = await _fx.OkAsync(Req("bucket"));

        var scope = ReturnsCalculator.ForGroup(AllHoldings(), Valuation);
        (groups.Total.Invested, groups.Total.Value, groups.Total.Pnl).ShouldBe((scope.Invested, scope.Value, scope.Pnl));
        (groups.Total.Invested, groups.Total.Value, groups.Total.Pnl, groups.Total.Holdings, groups.Total.Unpriced).ShouldBe((12800m, 16300m, 3500m, 5, 1));
    }

    [Fact]
    public async Task A_basket_grouping_is_not_additive_counts_a_holding_in_each_of_its_baskets_and_once_in_the_total()
    {
        var groups = await _fx.OkAsync(Req("basket", sort: "name"));

        groups.Additive.ShouldBeFalse();
        groups.Groups.Select(g => g.Name).ShouldBe(["Core", "Dividend", "No basket"]);
        (By(groups, Core.ToString()).Holdings, By(groups, Core.ToString()).Value).ShouldBe((2, 7800m));
        (By(groups, DividendBasket.ToString()).Holdings, By(groups, DividendBasket.ToString()).Value).ShouldBe((1, 6000m)); // AAA is in Core and in Dividend
        (By(groups, "~none").Holdings, By(groups, "~none").Value).ShouldBe((3, 8500m));
        groups.Groups.Sum(g => g.Holdings).ShouldBeGreaterThan(groups.Total.Holdings);
        groups.Total.Holdings.ShouldBe(5);
        groups.Total.Value.ShouldBe(16300m); // each holding once
        By(groups, DividendBasket.ToString()).WeightPct.ShouldNotBeNull().ShouldBe(36.8098m, 0.0001m);
    }

    [Fact]
    public async Task Sectors_use_the_master_and_a_holding_that_is_not_in_it_is_unclassified()
    {
        var groups = await _fx.OkAsync(Req("sector", sort: "name"));

        groups.Groups.Select(g => (g.Key, g.Name)).ShouldBe([("Banks", "Banks"), ("IT", "IT"), ("~none", "Unclassified")]);
        By(groups, "Banks").Value.ShouldBe(8800m);
    }

    [Fact]
    public async Task A_group_of_only_unpriced_holdings_has_no_value_profit_or_xirr_and_counts_them()
    {
        var groups = await _fx.OkAsync(Req("bucket"));

        var c = By(groups, BucketC.ToString());
        (c.Holdings, c.Unpriced, c.Value, c.Pnl, c.PnlPct, c.Xirr, c.XirrState, c.WeightPct).ShouldBe((1, 1, null, null, null, null, "no_price", null));
        c.Invested.ShouldBe(0m); // as in the summary, unpriced holdings are left out of invested
    }

    [Fact]
    public async Task The_group_xirr_comes_from_the_union_of_the_cash_flows_and_equals_the_calculator_for_the_same_flows()
    {
        var groups = await _fx.OkAsync(Req("bucket"));

        var a = By(groups, BucketA.ToString());
        var members = AllHoldings().Where(h => h.BucketId == BucketA).ToList();
        var expected = Xirr.Compute(ReturnsCalculator.FlowsOf(members, Valuation));
        expected.State.ShouldBe(XirrState.Ok);
        a.XirrState.ShouldBe("ok");
        a.Xirr.ShouldBe(Math.Round((decimal)(expected.Rate!.Value * 100.0), 4, MidpointRounding.AwayFromZero));
        var average = members.Select(h => ReturnsCalculator.ForHolding(h, Valuation).Xirr.Rate!.Value).Average() * 100.0;
        Math.Abs((double)a.Xirr!.Value - average).ShouldBeGreaterThan(0.001); // not an average of the members' rates
    }

    [Fact]
    public async Task A_group_of_one_holding_has_that_holdings_figures()
    {
        var groups = await _fx.OkAsync(Req("bucket"));

        var b = By(groups, BucketB.ToString());
        var holding = ReturnsCalculator.ForHolding(Bbb(), Valuation);
        var view = HoldingViews.Returns(holding);
        (b.Invested, b.Value, b.Pnl, b.PnlPct, b.Xirr, b.XirrState).ShouldBe((view.Invested, view.Value, view.Pnl, view.PnlPct, view.Xirr, view.XirrState));
    }

    [Fact]
    public async Task The_day_change_is_summed_over_the_holdings_with_a_previous_close_and_counts_the_ones_left_out()
    {
        var groups = await _fx.OkAsync(Req("account", sort: "name"));

        var one = groups.Groups[0]; // AAA +200, BBB -100 on 5,800 + 1,900
        one.DayChange.ShouldBe(100m);
        one.DayChangePct.ShouldNotBeNull().ShouldBe(1.2987m, 0.0001m);
        one.DayChangeExcluded.ShouldBe(0);
        var two = groups.Groups[1]; // CCC has no previous close, DDD no price, EEE is flat
        two.DayChange.ShouldBe(0m);
        two.DayChangeExcluded.ShouldBe(2);
        groups.Total.DayChange.ShouldBe(100m);
        groups.Total.DayChangeExcluded.ShouldBe(2);
    }

    [Fact]
    public async Task A_group_with_no_day_change_figure_at_all_has_none_and_counts_the_exclusions()
    {
        _fx.Queries.Holdings.RemoveAll(h => h.TradingSymbol is "AAA" or "BBB" or "EEE");

        var groups = await _fx.OkAsync(Req("account"));

        (groups.Total.DayChange, groups.Total.DayChangePct, groups.Total.DayChangeExcluded).ShouldBe((null, null, 2));
    }

    [Fact]
    public async Task Closed_holdings_follow_the_status_filter()
    {
        _fx.Queries.Holdings.Add(Held("ZZZ", 10m, 0, 10m, TwoYearsAgo, AccountOne, BucketA, null, status: "closed") with { Lots = [HoldingFixtures.Closed(10, 10m, TwoYearsAgo, new DateOnly(2026, 1, 5), 15m)] });

        var open = await _fx.OkAsync(Req("account"));
        var closed = await _fx.OkAsync(Req("account", status: "closed"));
        var all = await _fx.OkAsync(Req("account", status: "all"));

        open.Total.Holdings.ShouldBe(5);
        (closed.Total.Holdings, closed.Total.Realised, closed.Total.Invested).ShouldBe((1, 50m, 0m));
        all.Total.Holdings.ShouldBe(6);
    }

    [Fact]
    public async Task The_search_and_account_filters_narrow_the_scope_before_grouping()
    {
        var byText = await _fx.OkAsync(Req("sector", q: "aa"));
        var byAccount = await _fx.OkAsync(Req("sector", account: AccountTwo));

        byText.Groups.Select(g => g.Key).ShouldBe(["Banks"]);
        (byText.Total.Holdings, byText.Groups[0].WeightPct).ShouldBe((1, 100m));
        byAccount.Total.Holdings.ShouldBe(3);
        _fx.Queries.LastFilter!.AccountId.ShouldBe(AccountTwo);
    }

    [Fact]
    public async Task Without_an_account_only_the_accounts_that_count_in_totals_are_read_like_the_summary()
    {
        await _fx.OkAsync(Req("account"));

        _fx.Queries.LastFilter!.TotalsOnly.ShouldBeTrue();
        await _fx.OkAsync(Req("account", account: AccountOne));
        _fx.Queries.LastFilter!.TotalsOnly.ShouldBeFalse();
    }

    [Fact]
    public async Task An_empty_scope_has_no_groups_and_an_empty_total()
    {
        _fx.Queries.Holdings.Clear();

        var groups = await _fx.OkAsync(Req("sector"));

        groups.Groups.ShouldBeEmpty();
        (groups.Total.Holdings, groups.Total.Value, groups.Total.WeightPct, groups.Total.Invested).ShouldBe((0, 0m, null, 0m));
    }

    [Theory]
    [InlineData("value", new[] { "Account One", "Account Two" })] // 7,800 / 8,500 ascending
    [InlineData("-value", new[] { "Account Two", "Account One" })]
    [InlineData(null, new[] { "Account Two", "Account One" })] // the default: the largest first
    [InlineData("invested", new[] { "Account Two", "Account One" })] // 5,800 / 7,000
    [InlineData("-invested", new[] { "Account One", "Account Two" })]
    [InlineData("pnl", new[] { "Account One", "Account Two" })] // 800 / 2,700
    [InlineData("-pnl", new[] { "Account Two", "Account One" })]
    [InlineData("pnlPct", new[] { "Account One", "Account Two" })] // 11.4 % / 46.6 %
    [InlineData("-name", new[] { "Account Two", "Account One" })]
    [InlineData("name", new[] { "Account One", "Account Two" })]
    [InlineData("-dayChange", new[] { "Account One", "Account Two" })] // 100 / 0
    [InlineData("xirr", new[] { "Account One", "Account Two" })]
    public async Task The_sort_whitelist_orders_the_groups(string? sort, string[] expected)
    {
        var groups = await _fx.OkAsync(Req("account", sort: sort));

        groups.Groups.Select(g => g.Name).ShouldBe(expected);
    }

    [Theory]
    [InlineData("value")]
    [InlineData("-value")]
    [InlineData("pnl")]
    [InlineData("-xirr")]
    public async Task Groups_without_a_figure_come_last_in_both_directions(string sort)
    {
        var groups = await _fx.OkAsync(Req("bucket", sort: sort)); // bucket C holds only an unpriced holding: no value, P&L or XIRR

        groups.Groups[^1].Key.ShouldBe(BucketC.ToString());
    }

    [Fact]
    public async Task Ties_are_broken_by_name_then_key_so_the_order_never_changes()
    {
        var one = Held("AAA", 10m, 10, 10m, TwoYearsAgo, AccountOne, BucketA, null);
        var two = Held("BBB", 10m, 10, 10m, TwoYearsAgo, AccountTwo, BucketB, null);
        var harness = new GroupsHarness(two, one);

        var ascending = await harness.OkAsync(Req("account", sort: "value"));
        var descending = await harness.OkAsync(Req("account", sort: "-value"));

        ascending.Groups.Select(g => g.Name).ShouldBe(["Account One", "Account Two"]);
        descending.Groups.Select(g => g.Name).ShouldBe(["Account One", "Account Two"]);
    }

    [Fact]
    public async Task At_most_200_groups_are_returned_but_the_total_covers_the_whole_scope()
    {
        var many = Enumerable.Range(0, 250)
            .Select(i => Held("S" + i, 10m, 1, 10m, TwoYearsAgo, AccountOne, Guid.CreateVersion7(), null))
            .ToArray();
        var harness = new GroupsHarness(many);

        var groups = await harness.OkAsync(Req("bucket"));

        groups.Groups.Count.ShouldBe(200);
        groups.Total.Holdings.ShouldBe(250);
    }

    [Fact]
    public async Task Stacking_sectors_by_account_gives_parts_that_sum_to_the_group_value()
    {
        var groups = await _fx.OkAsync(Req("sector", stackBy: "account", sort: "name"));

        groups.StackBy.ShouldBe("account");
        var banks = By(groups, "Banks");
        banks.Parts!.Select(p => (p.Name, p.Value)).ShouldBe([("Account One", 7800m), ("Account Two", 1000m)]);
        banks.Parts!.Sum(p => p.Value).ShouldBe(banks.Value!.Value);
        groups.Groups.ShouldAllBe(g => g.Parts != null && g.Parts.Sum(p => p.Value) == (g.Value ?? 0m));
        By(groups, "~none").Parts.ShouldBeEmpty(); // only an unpriced holding
    }

    [Fact]
    public async Task Stacking_accounts_by_sector_names_the_sectors_and_reads_the_master_once()
    {
        var groups = await _fx.OkAsync(Req("account", stackBy: "sector", sort: "name"));

        groups.Groups[1].Parts!.Select(p => (p.Key, p.Value)).ShouldBe([("IT", 7500m), ("Banks", 1000m)]);
        _fx.Classifications.SectorReads.ShouldBe(1);
        _fx.Classifications.BasketReads.ShouldBe(0);
    }

    [Fact]
    public async Task Without_stack_by_the_groups_have_no_parts()
    {
        var groups = await _fx.OkAsync(Req("sector"));

        groups.Groups.ShouldAllBe(g => g.Parts == null);
        groups.StackBy.ShouldBeNull();
    }

    [Theory]
    [InlineData("bucket", "account")]
    [InlineData("basket", "sector")]
    [InlineData("sector", "sector")]
    [InlineData("account", "account")]
    [InlineData("account", "basket")]
    [InlineData("sector", "bogus")]
    public async Task Any_other_stack_by_is_rejected(string by, string stackBy)
    {
        var result = await _fx.GroupsAsync(Req(by, stackBy: stackBy));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PortfolioErrors.InvalidCode);
        PortfolioFixture.FieldsOf(result).ShouldBe(["stackBy"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("strategy")]
    [InlineData("0")]
    public async Task A_missing_or_unknown_by_is_rejected(string? by)
    {
        var result = await _fx.GroupsAsync(Req(by));

        result.Error.Code.ShouldBe(PortfolioErrors.InvalidCode);
        PortfolioFixture.FieldsOf(result).ShouldBe(["by"]);
        _fx.Queries.Reads.ShouldBe(0);
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("-bogus")]
    [InlineData("symbol")]
    public async Task An_unknown_sort_is_rejected(string sort)
    {
        var result = await _fx.GroupsAsync(Req("account", sort: sort));

        PortfolioFixture.FieldsOf(result).ShouldBe(["sort"]);
    }

    [Fact]
    public async Task All_the_problems_are_reported_together_with_the_scope_filters()
    {
        var result = await _fx.GroupsAsync(Req("nope", sort: "x", stackBy: "account", status: "weird", q: new string('a', 101)));

        PortfolioFixture.FieldsOf(result).ShouldBe(["q", "status", "by", "stackBy", "sort"], ignoreOrder: true);
    }

    [Fact]
    public async Task One_request_reads_the_holdings_once_and_the_baskets_only_for_a_basket_grouping()
    {
        await _fx.OkAsync(Req("basket"));

        (_fx.Queries.Reads, _fx.Classifications.BasketReads, _fx.Classifications.SectorReads).ShouldBe((1, 1, 0));
        await _fx.OkAsync(Req("account"));
        (_fx.Queries.Reads, _fx.Classifications.BasketReads, _fx.Classifications.SectorReads).ShouldBe((2, 1, 0));
    }

    [Fact]
    public async Task Basket_membership_is_read_on_every_request_so_a_change_shows_at_once()
    {
        var before = await _fx.OkAsync(Req("basket"));
        _fx.Classifications.Baskets[InstrumentCcc] = [new BasketRef(DividendBasket, "Dividend")];

        var after = await _fx.OkAsync(Req("basket"));

        By(before, DividendBasket.ToString()).Holdings.ShouldBe(1);
        By(after, DividendBasket.ToString()).Holdings.ShouldBe(2);
        By(after, "~none").Holdings.ShouldBe(2);
    }

    [Fact]
    public async Task The_groups_agree_with_the_calculators_figures_for_every_holding_of_an_account()
    {
        var groups = await _fx.OkAsync(Req("account", sort: "name"));

        var members = AllHoldings().Where(h => h.AccountId == AccountTwo).ToList();
        var expected = HoldingViews.Returns(ReturnsCalculator.ForGroup(members, Valuation));
        var two = groups.Groups[1];
        (two.Invested, two.Value, two.Pnl, two.PnlPct, two.Xirr, two.XirrState, two.Realised)
            .ShouldBe((expected.Invested, expected.Value, expected.Pnl, expected.PnlPct, expected.Xirr, expected.XirrState, expected.RealisedPnl));
    }

    [Fact]
    public async Task Five_hundred_holdings_are_grouped_by_every_dimension_well_inside_the_budget()
    {
        var instruments = Enumerable.Range(0, 500).Select(_ => Guid.CreateVersion7()).ToArray();
        var holdings = instruments
            .Select((id, i) => Held("S" + i, 100m + i, 10 + i, 90m, TwoYearsAgo.AddDays(i), i % 2 == 0 ? AccountOne : AccountTwo, i % 3 == 0 ? BucketA : BucketB, id, 99m + i))
            .ToArray();
        var harness = new GroupsHarness(holdings);
        for (var i = 0; i < instruments.Length; i++)
        {
            harness.Classifications.Sectors[instruments[i]] = "Sector " + (i % 40);
            harness.Classifications.Baskets[instruments[i]] = [new BasketRef(Core, "Core"), new BasketRef(Guid.CreateVersion7(), "Basket " + i % 25)];
        }

        await harness.OkAsync(Req("basket")); // warm-up: JIT and the first allocations are not what the budget is about

        foreach (var (by, stackBy) in new (string, string?)[] { ("account", "sector"), ("bucket", null), ("sector", "account"), ("basket", null) })
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var groups = await harness.OkAsync(Req(by, stackBy: stackBy));
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);

            groups.Total.Holdings.ShouldBe(500);
            // The spec asks for under 300 ms for 500 holdings including the database read; the computation alone takes a few milliseconds, so this bound is generous on purpose (machines and CI runners vary).
            elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(500), by);
        }
    }

    [Fact]
    public async Task Sector_spellings_that_differ_only_in_case_are_one_group_named_by_the_ordinal_smallest_spelling_whatever_the_read_order()
    {
        _fx.Classifications.Sectors[InstrumentBbb] = "banks";
        _fx.Classifications.Sectors[InstrumentEee] = "BANKS";
        var original = _fx.Queries.Holdings.ToArray();

        foreach (var order in new[] { original, [.. original.Reverse()], [original[1], original[4], original[0], original[3], original[2]], [original[4], original[2], original[1], original[0], original[3]] })
        {
            _fx.Queries.Holdings.Clear();
            _fx.Queries.Holdings.AddRange(order);

            var groups = await _fx.OkAsync(Req("sector"));

            var banks = groups.Groups.Single(g => string.Equals(g.Key, "banks", StringComparison.OrdinalIgnoreCase));
            (banks.Key, banks.Name, banks.Holdings).ShouldBe(("BANKS", "BANKS", 3));
            groups.Groups.Count.ShouldBe(3);
        }
    }

    [Theory]
    [InlineData("~none")]
    [InlineData("~NONE")]
    public async Task A_master_sector_spelled_like_the_reserved_key_is_unclassified(string sector)
    {
        _fx.Classifications.Sectors[InstrumentCcc] = sector;

        var groups = await _fx.OkAsync(Req("sector"));

        groups.Groups.Select(g => g.Key).Order().ShouldBe(["Banks", "~none"]);
        var none = By(groups, "~none");
        (none.Name, none.Holdings).ShouldBe(("Unclassified", 2)); // CCC and DDD
        (await _fx.ListAsync("sector", "~none")).Value.Items.Select(i => i.Symbol).Order().ShouldBe(["CCC", "DDD"]);
    }

    [Fact]
    public async Task A_scope_of_only_unpriced_holdings_has_no_value_profit_or_xirr_in_the_groups_and_the_total_unlike_the_summary()
    {
        var harness = new GroupsHarness(Ddd(), Held("FFF", 0m, 4, 25m, TwoYearsAgo, AccountOne, BucketA, null));

        var groups = await harness.OkAsync(Req("account"));

        var total = groups.Total;
        (total.Holdings, total.Unpriced, total.Invested, total.Value, total.Pnl, total.PnlPct, total.Xirr, total.XirrState, total.WeightPct).ShouldBe((2, 2, 0m, null, null, null, null, "no_price", null));
        groups.Groups.ShouldAllBe(g => g.Value == null && g.Pnl == null && g.XirrState == "no_price" && g.Invested == 0m && g.WeightPct == null && g.Unpriced == 1);
    }

    [Fact]
    public async Task Parts_add_up_to_the_group_value_up_to_the_rounding_with_fractional_prices_and_quantities()
    {
        var holdings = Enumerable.Range(0, 40)
            .Select(i => Held("P" + i, 10.33333m + (i * 0.00007m), 7 + i, 9.5m, TwoYearsAgo, i % 2 == 0 ? AccountOne : AccountTwo, BucketA, Guid.CreateVersion7()))
            .ToArray();
        var harness = new GroupsHarness(holdings);
        foreach (var holding in holdings)
        {
            harness.Classifications.Sectors[holding.InstrumentId!.Value] = "Sector " + (holding.Qty % 3);
        }

        foreach (var (by, stack) in new[] { ("sector", "account"), ("account", "sector") })
        {
            var groups = await harness.OkAsync(Req(by, stackBy: stack));

            foreach (var group in groups.Groups)
            {
                var difference = Math.Abs(group.Parts!.Sum(p => p.Value) - group.Value!.Value);
                difference.ShouldBeLessThanOrEqualTo(0.0001m * group.Parts!.Count, $"{by}/{group.Name}");
            }

            groups.Groups.Sum(g => g.Value!.Value).ShouldBe(groups.Total.Value!.Value, 0.0003m);
        }
    }

    [Fact]
    public void The_value_only_helper_equals_the_value_of_the_returns_calculator()
    {
        var holdings = Enumerable.Range(0, 25).Select(i => Held("V" + i, 10.33333m + (i * 0.00007m), 7 + i, 9.5m, TwoYearsAgo, AccountOne, BucketA, null)).ToList();
        holdings.Add(Ddd());
        holdings.Add(Held("CLOSED", 12.5m, 0, 10m, TwoYearsAgo, AccountOne, BucketA, null, status: "closed") with { Lots = [HoldingFixtures.Closed(3, 10m, TwoYearsAgo, Valuation, 12.5m)] });

        GroupCalculator.ValueOf(holdings).ShouldBe(ReturnsCalculator.ForGroup(holdings, Valuation).Value);
        GroupCalculator.ValueOf([Ddd()]).ShouldBeNull();
        GroupCalculator.ValueOf([]).ShouldBe(0m);
    }

    [Fact]
    public async Task An_account_that_does_not_exist_gives_empty_groups_not_an_error_like_the_holdings_list()
    {
        var groups = await _fx.OkAsync(Req("account", account: Guid.NewGuid()));

        groups.Groups.ShouldBeEmpty();
        groups.Total.Holdings.ShouldBe(0);
    }
}
