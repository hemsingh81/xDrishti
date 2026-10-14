using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Tests.Portfolio.Groups;
using XDrishti.Application.Tests.Portfolio.History;
using static XDrishti.Application.Tests.Portfolio.Groups.GroupFixtures;
using static XDrishti.Application.Tests.Portfolio.Groups.GroupsHarness;
using static XDrishti.Application.Tests.Portfolio.Income.IncomeFixtures;

namespace XDrishti.Application.Tests.Portfolio.Income;

/// <summary>
/// The income figures on the holding rows, the holding groups and the summary (ADR 0026, P2-14 criteria 3 to 7 and 10) over the made-up portfolio of <see cref="GroupFixtures"/>, valued on Monday 2026-10-12. Recorded:
/// dividends AAA 200 + 50, CCC 100, DDD (unpriced) 5, account one unallocated 30; trade charges AAA 10, BBB 5, and 1 of a security (ZZZ) of account two no holding has; the ledgers' costs 100 (account one) and 40 (account two).
/// </summary>
public sealed class IncomeReadsTests
{
    private readonly GroupsHarness _fx = new();

    public IncomeReadsTests() => Record(_fx.Income, _fx.Queries.Holdings);

    private static void Record(FakeIncomeQueries income, IReadOnlyList<HoldingData> holdings)
    {
        HoldingData Of(string symbol) => holdings.Single(h => h.TradingSymbol == symbol);
        income.Dividends.AddRange(
        [
            Dividend(Of("AAA"), D(2026, 1, 1), 200m),
            Dividend(Of("AAA"), D(2026, 6, 1), 50m),
            Dividend(Of("CCC"), D(2026, 3, 3), 100m),
            Dividend(Of("DDD"), D(2026, 4, 4), 5m),
            Dividend(null, D(2025, 12, 12), 30m, AccountOne),
        ]);
        income.TradeCharges.AddRange(
        [
            new IncomeTradeCharge(AccountOne, null, "AAA", D(2024, 10, 12), 10m),
            new IncomeTradeCharge(AccountOne, null, "BBB", D(2025, 10, 12), 5m),
            new IncomeTradeCharge(AccountTwo, null, "ZZZ", D(2026, 1, 1), 1m),
        ]);
        income.LedgerCharges.AddRange([Ledger(D(2026, 3, 31), 100m, AccountOne), Ledger(D(2026, 2, 2), 40m, AccountTwo)]);
        income.LedgerStarts.AddRange([new IncomeLedgerStart(AccountOne, D(2025, 1, 2)), new IncomeLedgerStart(AccountTwo, D(2025, 6, 1))]);
    }

    private GetHoldingsSummaryHandler SummaryHandler() => new(
        _fx.Queries,
        Substitute.For<IHoldingSyncStore>(),
        Substitute.For<IAccountStore>(),
        Substitute.For<IPortfolioBenchmark>(),
        Substitute.For<IPortfolioPrices>(),
        _fx.Income,
        new FakeTradingCalendar(HistoryData.Weekdays),
        _fx.Clock);

    private async Task<HoldingsSummary> SummaryAsync(Guid? bucket = null) =>
        (await SummaryHandler().HandleAsync(new GetHoldingsSummaryQuery(null, bucket), CancellationToken.None)).Value;

    private async Task<HoldingRow> RowAsync(string symbol)
    {
        var page = (await _fx.ListAsync(null, null, status: "all")).Value;
        return page.Items.Single(r => r.Symbol == symbol);
    }

    [Fact]
    public async Task A_row_has_the_dividends_and_trade_charges_of_its_holding_and_the_returns_that_include_them()
    {
        var row = await RowAsync("AAA");

        var income = row.Income.ShouldNotBeNull();
        (income.Dividends, income.Charges, income.Unallocated).ShouldBe((250m, 10m, null));
        (income.TotalReturnPct, income.NetTotalReturnPct).ShouldBe((25m, 24.8m)); // (1,000 + 250) and (1,250 - 10) over 5,000
        (income.DividendsFrom, income.ChargesFrom, income.CoverageFrom).ShouldBe((D(2026, 1, 1), D(2024, 10, 12), D(2026, 1, 1))); // the later start of the sources: no dividend before 2026 was entered
        (income.LedgerCharges, income.TradeChargesFrom, income.LedgerChargesFrom).ShouldBe((null, D(2024, 10, 12), null));
        income.NetXirrState.ShouldBe("ok");
    }

    [Fact]
    public async Task A_row_with_nothing_recorded_has_no_income_and_its_gross_figures_are_as_before()
    {
        var row = await RowAsync("EEE");

        row.Income.ShouldBeNull();
        (row.Returns.Pnl, row.Returns.PnlPct).ShouldBe((200m, 25m));
    }

    [Fact]
    public async Task A_row_never_carries_what_belongs_to_the_account()
    {
        var rows = (await _fx.ListAsync(null, null, status: "all")).Value.Items;

        rows.Sum(r => r.Income?.Dividends ?? 0m).ShouldBe(355m); // 385 less the unallocated 30
        rows.Sum(r => r.Income?.Charges ?? 0m).ShouldBe(15m); // 156 less the ledgers' 140 and the 1 of ZZZ
        rows.ShouldAllBe(r => r.Income == null || r.Income.Unallocated == null);
    }

    [Fact]
    public async Task The_income_is_read_once_for_a_page_and_not_at_all_when_no_holding_is_listed()
    {
        await _fx.ListAsync(null, null, status: "all");
        var reads = _fx.Income.Reads;
        await _fx.ListAsync("sector", "no-such-sector");

        reads.ShouldBe(1);
        _fx.Income.Reads.ShouldBe(1);
    }

    [Fact]
    public async Task Rows_can_be_sorted_by_dividends_with_the_rows_without_any_last()
    {
        var page = (await _fx.ListAsync(null, null, sort: "-dividends", status: "all")).Value;

        page.Items.Select(r => r.Symbol).ShouldBe(["AAA", "CCC", "DDD", "BBB", "EEE"]);
    }

    [Fact]
    public async Task Grouped_by_account_each_account_holds_its_own_dividends_and_costs_and_no_extra_row_is_needed()
    {
        var groups = await _fx.OkAsync(Req("account", sort: "name"));

        groups.Groups.Count.ShouldBe(2);
        var one = groups.Groups[0].Income.ShouldNotBeNull();
        var two = groups.Groups[1].Income.ShouldNotBeNull();
        (one.Dividends, one.Charges, one.Unallocated).ShouldBe((280m, 115m, 30m));
        (one.TotalReturnPct, one.NetTotalReturnPct).ShouldBe((15.4286m, 13.7857m)); // (800 + 280) and (1,080 - 115) over 7,000
        (two.Dividends, two.Charges, two.Unallocated).ShouldBe((105m, 41m, 0m)); // DDD's 5 counts; ZZZ's 1 and the ledger's 40 are account two's
        (groups.Groups.Sum(g => g.Income!.Dividends), groups.Groups.Sum(g => g.Income!.Charges)).ShouldBe((groups.Total.Income!.Dividends, groups.Total.Income.Charges));
    }

    [Fact]
    public async Task Grouped_by_anything_else_an_extra_last_row_holds_what_belongs_to_no_group_so_that_the_columns_add_up()
    {
        foreach (var by in new[] { "bucket", "sector" })
        {
            var groups = await _fx.OkAsync(Req(by));

            var last = groups.Groups[^1];
            (last.Key, last.Name, last.Holdings, last.Value, last.XirrState).ShouldBe(("~unallocated", "Unallocated", 0, null, "no_data"));
            (last.Income!.Dividends, last.Income.Charges, last.Income.Unallocated).ShouldBe((30m, 141m, 30m)); // the unallocated dividend; the ledgers' 140 and ZZZ's 1
            groups.Groups.Sum(g => g.Income?.Dividends ?? 0m).ShouldBe(groups.Total.Income!.Dividends, by);
            groups.Groups.Sum(g => g.Income?.Charges ?? 0m).ShouldBe(groups.Total.Income.Charges, by);
            groups.Groups.Take(groups.Groups.Count - 1).ShouldAllBe(g => g.Income == null || g.Income.Unallocated == null);
        }
    }

    [Fact]
    public async Task Baskets_overlap_but_the_total_and_the_extra_row_are_still_those_of_the_scope()
    {
        var groups = await _fx.OkAsync(Req("basket"));

        groups.Additive.ShouldBeFalse();
        groups.Groups[^1].Key.ShouldBe("~unallocated");
        (groups.Total.Income!.Dividends, groups.Total.Income.Charges).ShouldBe((385m, 156m));
    }

    [Fact]
    public async Task The_total_has_everything_recorded_and_its_returns_use_the_scope_totals()
    {
        var groups = await _fx.OkAsync(Req("account"));

        var income = groups.Total.Income.ShouldNotBeNull();
        (income.Dividends, income.Charges, income.Unallocated).ShouldBe((385m, 156m, 30m));
        (income.TotalReturnPct, income.NetTotalReturnPct).ShouldBe((30.3516m, 29.1328m)); // (3,500 + 385) and (3,885 - 156) over 12,800
        (income.DividendsFrom, income.ChargesFrom, income.CoverageFrom).ShouldBe((D(2025, 12, 12), D(2025, 1, 2), D(2025, 12, 12)));
        (income.TradeChargesFrom, income.LedgerChargesFrom, income.LedgerCharges).ShouldBe((D(2024, 10, 12), D(2025, 1, 2), 140m));
    }

    [Fact]
    public async Task A_bucket_or_a_text_narrows_the_scope_to_holdings_and_leaves_what_belongs_to_the_account_out()
    {
        var bucket = await _fx.OkAsync(Req("sector", bucket: BucketA));
        var text = await _fx.OkAsync(Req("sector", q: "AAA"));

        (bucket.Total.Income!.Dividends, bucket.Total.Income.Charges, bucket.Total.Income.Unallocated).ShouldBe((350m, 10m, null));
        bucket.Groups.ShouldNotContain(g => g.Key == "~unallocated");
        (text.Total.Income!.Dividends, text.Total.Income.Charges, text.Total.Income.Unallocated).ShouldBe((250m, 10m, null));
        text.Groups.ShouldNotContain(g => g.Key == "~unallocated");
    }

    [Fact]
    public async Task Showing_only_open_holdings_leaves_the_dividends_of_closed_ones_out_but_keeps_what_belongs_to_the_account()
    {
        var closed = Held("OLD", 100m, 10, 50m, TwoYearsAgo, AccountOne, BucketA, null, status: "closed") with { Lots = [HoldingFixtures.Closed(10, 50m, TwoYearsAgo, D(2026, 1, 1), 80m)] };
        _fx.Queries.Holdings.Add(closed);
        _fx.Income.Dividends.Add(Dividend(closed, D(2026, 2, 2), 77m));

        var open = await _fx.OkAsync(Req("account", status: "open"));
        var all = await _fx.OkAsync(Req("account", status: "all"));

        open.Total.Income!.Dividends.ShouldBe(385m);
        all.Total.Income!.Dividends.ShouldBe(462m);
        open.Groups.Single(g => g.Key == AccountOne.ToString()).Income!.Unallocated.ShouldBe(30m);
    }

    [Fact]
    public async Task An_account_with_no_group_left_puts_what_it_has_into_the_extra_row_when_grouping_by_account()
    {
        var alone = Guid.Parse("00000000-0000-7000-8000-0000000000a9");
        _fx.Income.ExtraAccounts.Add(alone);
        _fx.Income.Dividends.Add(Dividend(null, D(2026, 5, 5), 12m, alone));

        var groups = await _fx.OkAsync(Req("account"));

        var last = groups.Groups[^1];
        (last.Key, last.Income!.Dividends).ShouldBe(("~unallocated", 12m));
        groups.Groups.Sum(g => g.Income!.Dividends).ShouldBe(groups.Total.Income!.Dividends);
    }

    [Fact]
    public async Task Nothing_recorded_means_no_income_anywhere_and_no_extra_row()
    {
        var plain = new GroupsHarness();

        var groups = await plain.OkAsync(Req("account"));

        groups.Groups.ShouldAllBe(g => g.Income == null);
        groups.Total.Income.ShouldBeNull();
        groups.Groups.ShouldNotContain(g => g.Key == "~unallocated");
    }

    [Fact]
    public async Task No_dividend_entered_is_no_value_in_either_direction_not_a_zero_that_sorts_first()
    {
        var rows = (await _fx.ListAsync(null, null, sort: "dividends", status: "all")).Value;
        var groups = await _fx.OkAsync(Req("bucket", sort: "dividends"));

        rows.Items.Select(r => r.Symbol).ShouldBe(["DDD", "CCC", "AAA", "BBB", "EEE"]); // BBB has trade charges but no dividend
        groups.Groups.Select(g => g.Name).ShouldBe(["Bucket C", "Bucket A", "Bucket B", "Unallocated"]); // Bucket B has charges and no dividend
    }

    [Fact]
    public async Task The_income_of_a_list_is_read_once_and_without_the_ledger()
    {
        var page = (await _fx.ListAsync(null, null, sort: "-xirr", status: "all")).Value;

        page.Items.Count.ShouldBe(5);
        _fx.Income.Reads.ShouldBe(1);
        _fx.Income.LastScope.ShouldNotBeNull().AccountLevel.ShouldBeFalse();
        _fx.Income.LastScope.Holdings.ShouldNotBeNull().Count.ShouldBe(5); // all statuses, no bucket, no text: the holdings read already are the keys
    }

    [Fact]
    public async Task A_list_of_open_holdings_only_leaves_the_keys_to_the_income_read_because_the_closed_ones_are_missing_from_it()
    {
        await _fx.ListAsync(null, null);

        _fx.Income.LastScope.ShouldNotBeNull().Holdings.ShouldBeNull();
    }

    [Fact]
    public async Task The_summary_hands_over_the_holdings_it_read_unless_a_bucket_narrowed_them()
    {
        await SummaryAsync();
        var whole = _fx.Income.LastScope.ShouldNotBeNull();
        await SummaryAsync(BucketA);
        var narrowed = _fx.Income.LastScope.ShouldNotBeNull();

        (whole.Holdings is not null, whole.AccountLevel, narrowed.Holdings is null, _fx.Income.Reads).ShouldBe((true, true, true, 2));
    }

    [Fact]
    public async Task The_groups_read_the_income_once_and_hand_over_the_holdings_only_for_the_whole_scope()
    {
        await _fx.OkAsync(Req("bucket", status: "all"));
        var whole = _fx.Income.LastScope.ShouldNotBeNull();
        await _fx.OkAsync(Req("bucket"));
        var open = _fx.Income.LastScope.ShouldNotBeNull();

        (whole.Holdings is not null, open.Holdings is null, _fx.Income.Reads).ShouldBe((true, true, 2));
    }

    [Fact]
    public async Task Groups_can_be_sorted_by_dividends()
    {
        var groups = await _fx.OkAsync(Req("bucket", sort: "-dividends"));

        groups.Groups.Select(g => g.Name).ShouldBe(["Bucket A", "Bucket C", "Bucket B", "Unallocated"]);
    }

    [Fact]
    public async Task The_summary_has_the_income_of_the_whole_portfolio_and_of_a_bucket_without_what_belongs_to_the_account()
    {
        var all = await SummaryAsync();
        var bucket = await SummaryAsync(BucketA);

        (all.Income!.Dividends, all.Income.Charges, all.Income.Unallocated).ShouldBe((385m, 156m, 30m));
        (all.Income.TotalReturnPct, all.Income.NetTotalReturnPct).ShouldBe((30.3516m, 29.1328m));
        (bucket.Income!.Dividends, bucket.Income.Charges, bucket.Income.Unallocated).ShouldBe((350m, 10m, null));
    }

    [Fact]
    public async Task The_summary_figures_agree_with_the_groups_total_and_the_gross_figures_do_not_change()
    {
        var plain = new GroupsHarness();
        var withIncome = await SummaryAsync();
        var summaryWithout = (await new GetHoldingsSummaryHandler(
            plain.Queries, Substitute.For<IHoldingSyncStore>(), Substitute.For<IAccountStore>(), Substitute.For<IPortfolioBenchmark>(), Substitute.For<IPortfolioPrices>(), plain.Income,
            new FakeTradingCalendar(HistoryData.Weekdays), plain.Clock).HandleAsync(new GetHoldingsSummaryQuery(null, null), CancellationToken.None)).Value;
        var groups = await _fx.OkAsync(Req("account"));

        withIncome.Income.ShouldBe(groups.Total.Income);
        summaryWithout.Income.ShouldBeNull();
        (withIncome with { Income = null }).ShouldBe(summaryWithout);
    }
}
