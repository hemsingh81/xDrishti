using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>Listing, summary, lots and date edits on imported holdings (clock: Saturday 2026-10-10, India).</summary>
public sealed class HoldingQueriesTests
{
    private readonly PortfolioFixture _fx = new();

    private static readonly DateOnly TwoYearsAgo = new(2024, 10, 10);

    private async Task<BrokerAccount> ImportedAsync(string name = "Example One", string clientId = "1000000001", DateOnly? date = null, params BrokerHolding[] rows)
    {
        var account = await _fx.ConnectedAsync(name, clientId);
        _fx.Dhan.Rows.Clear();
        _fx.Dhan.Rows.AddRange(rows.Length == 0
            ? [Row("AAA", 100, 50m, 60m, "1", "INE000A00001"), Row("BBB", 10, 200m, 180m, "2", "INE000B00002"), Row("CCC", 5, 1000m, 1500m, "3", "INE000C00003")]
            : rows);
        await _fx.ImportAllAsync(account.Id, date ?? TwoYearsAgo);
        return account;
    }

    private async Task<HoldingPage> ListAsync(HoldingsRequest request) => Ok(await _fx.List().HandleAsync(new ListHoldingsQuery(request), CancellationToken.None));

    private static HoldingsRequest Req(Guid? account = null, string? status = null, string? q = null, string? sort = null, int? page = null, int? size = null, Guid? bucket = null) =>
        new(account, bucket, status, q, sort, page, size);

    [Fact]
    public async Task Rows_carry_the_lots_summary_and_every_returns_field()
    {
        await ImportedAsync();

        var page = await ListAsync(Req(sort: "symbol"));

        page.Items.Select(i => i.Symbol).ShouldBe(["AAA", "BBB", "CCC"]);
        var row = page.Items[0];
        (row.LotCount, row.EarliestAcquiredOn, row.UsesDefaultDate, row.Mapped, row.Status).ShouldBe((1, TwoYearsAgo, false, false, "open"));
        (row.Returns.Invested, row.Returns.Value, row.Returns.Pnl, row.Returns.PnlPct, row.Returns.XirrState).ShouldBe((5000m, 6000m, 1000m, 20m, "ok"));
        row.Returns.Xirr.ShouldNotBeNull().ShouldBe(9.5445m, 0.001m); // percent
        (row.Returns.RealisedPnl, row.Returns.RealisedEstimated).ShouldBe((0m, false));
        (row.AccountName, row.BucketName).ShouldBe(("Example One", "Example One · Portfolio"));
        (page.Total, page.Page, page.PageSize).ShouldBe((3, 1, 50));
    }

    [Fact]
    public async Task Paging_is_stable_and_capped_at_100()
    {
        await ImportedAsync();

        var first = await ListAsync(Req(sort: "symbol", page: 1, size: 2));
        var second = await ListAsync(Req(sort: "symbol", page: 2, size: 2));

        first.Items.Select(i => i.Symbol).ShouldBe(["AAA", "BBB"]);
        second.Items.Select(i => i.Symbol).ShouldBe(["CCC"]);
        first.Total.ShouldBe(3);
        var tooBig = await _fx.List().HandleAsync(new ListHoldingsQuery(Req(size: 101)), CancellationToken.None);
        FieldsOf(tooBig).ShouldBe(["pageSize"]);
    }

    [Theory]
    [InlineData("symbol", new[] { "AAA", "BBB", "CCC" })]
    [InlineData("-symbol", new[] { "CCC", "BBB", "AAA" })]
    [InlineData("pnl", new[] { "BBB", "AAA", "CCC" })] // -200, 1,000, 2,500
    [InlineData("-pnl", new[] { "CCC", "AAA", "BBB" })]
    [InlineData("-value", new[] { "CCC", "AAA", "BBB" })] // 7,500, 6,000, 1,800
    [InlineData("pnlPct", new[] { "BBB", "AAA", "CCC" })] // -10, 20, 50
    [InlineData("qty", new[] { "CCC", "BBB", "AAA" })]
    [InlineData("lastPrice", new[] { "AAA", "BBB", "CCC" })]
    [InlineData("xirr", new[] { "BBB", "AAA", "CCC" })]
    [InlineData("invested", new[] { "BBB", "AAA", "CCC" })] // 2,000 / 5,000 / 5,000 (tie by symbol)
    [InlineData("acquiredOn", new[] { "AAA", "BBB", "CCC" })] // all equal: symbol
    public async Task The_sort_whitelist_orders_by_the_named_figure(string sort, string[] expected)
    {
        await ImportedAsync();

        var page = await ListAsync(Req(sort: sort));

        page.Items.Select(i => i.Symbol).ShouldBe(expected);
    }

    [Fact]
    public async Task Rows_without_a_value_for_the_sorted_figure_come_last_in_both_directions()
    {
        await ImportedAsync(date: _fx.Today); // bought today: no xirr

        (await ListAsync(Req(sort: "xirr"))).Items.Select(i => i.Returns.XirrState).ShouldAllBe(s => s == "insufficient");
        await ImportedAsync("Example Two", "1000000002", TwoYearsAgo, Row("OLD", 10, 10m, 20m, "9", "INE000O00009"));

        var ascending = await ListAsync(Req(sort: "xirr"));
        var descending = await ListAsync(Req(sort: "-xirr"));

        ascending.Items[0].Symbol.ShouldBe("OLD");
        descending.Items[0].Symbol.ShouldBe("OLD");
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("secret")]
    [InlineData("-")]
    [InlineData("accountId")]
    public async Task An_unknown_sort_is_rejected_with_the_field_name(string sort)
    {
        var result = await _fx.List().HandleAsync(new ListHoldingsQuery(Req(sort: sort)), CancellationToken.None);

        FieldsOf(result).ShouldBe(["sort"]);
    }

    [Fact]
    public async Task Filters_by_account_bucket_status_and_text_combine()
    {
        var one = await ImportedAsync();
        var two = await ImportedAsync("Example Two", "1000000002", TwoYearsAgo, Row("DDD", 3, 30m, 31m, "7", "INE000D00007"));
        _fx.Dhan.Rows.Clear();
        _fx.Dhan.Rows.Add(Row("AAA", 100, 50m, 60m, "1", "INE000A00001")); // BBB, CCC gone from account one
        Ok(await _fx.SyncAsync(one.Id));
        var bucketTwo = _fx.Accounts.Buckets.Single(b => b.DefaultForAccountId == two.Id).Id;

        (await ListAsync(Req())).Items.Select(i => i.Symbol).ShouldBe(["AAA", "DDD"], ignoreOrder: true); // open only
        (await ListAsync(Req(status: "closed"))).Items.Select(i => i.Symbol).ShouldBe(["BBB", "CCC"], ignoreOrder: true);
        (await ListAsync(Req(status: "all"))).Total.ShouldBe(4);
        (await ListAsync(Req(account: two.Id))).Items.Select(i => i.Symbol).ShouldBe(["DDD"]);
        (await ListAsync(Req(bucket: bucketTwo))).Items.Select(i => i.Symbol).ShouldBe(["DDD"]);
        (await ListAsync(Req(q: "ine000a"))).Items.Select(i => i.Symbol).ShouldBe(["AAA"]);
        (await ListAsync(Req(q: "dd", account: two.Id))).Items.Select(i => i.Symbol).ShouldBe(["DDD"]);
        (await ListAsync(Req(q: "dd", account: one.Id))).Items.ShouldBeEmpty();
        FieldsOf(await _fx.List().HandleAsync(new ListHoldingsQuery(Req(status: "weird")), CancellationToken.None)).ShouldBe(["status"]);
    }

    [Fact]
    public async Task The_summary_equals_the_sum_of_the_holdings_and_counts_default_date_lots()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.AddRange([Row("AAA", 100, 50m, 60m, "1", "INE000A00001"), Row("BBB", 10, 200m, 180m, "2", "INE000B00002"), Row("CCC", 5, 1000m, 1500m, "3", "INE000C00003")]);
        Ok(await _fx.ImportAsync(account.Id, new ImportInput(TwoYearsAgo, [new ImportRowInput("INE000A00001", null), new ImportRowInput("INE000B00002", null), new ImportRowInput("INE000C00003", TwoYearsAgo)])));
        _fx.Instruments.Known[Row("AAA", isin: "INE000A00001", securityId: "1").Key] = new Application.Abstractions.Market.InstrumentRef(Guid.NewGuid(), "AAA", "Alpha");

        var summary = Ok(await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(null, null), CancellationToken.None));
        var rows = (await ListAsync(Req())).Items;

        summary.Returns.Invested.ShouldBe(rows.Sum(r => r.Returns.Invested));
        summary.Returns.Value.ShouldBe(rows.Sum(r => r.Returns.Value));
        summary.Returns.Pnl.ShouldBe(rows.Sum(r => r.Returns.Pnl));
        (summary.Returns.Invested, summary.Returns.Value, summary.Returns.Pnl).ShouldBe((12000m, 15300m, 3300m));
        (summary.OpenCount, summary.ClosedCount, summary.UnmappedCount, summary.DefaultDateLots).ShouldBe((3, 0, 3, 2));
        (summary.ValuationDate, summary.Returns.XirrState).ShouldBe((_fx.Today, "ok"));
        summary.LastSync.ShouldNotBeNull().Trigger.ShouldBe("import");
    }

    [Fact]
    public async Task The_summary_of_an_account_a_bucket_and_the_total_differ_and_totals_leave_out_accounts_excluded_from_totals()
    {
        var one = await ImportedAsync();
        var excluded = await _fx.ConnectedAsync("Example Two", "1000000002", portfolio: new PortfolioInput(null, false));
        _fx.Dhan.Rows.Clear();
        _fx.Dhan.Rows.Add(Row("ZZZ", 1, 100m, 100m, "99", "INE000Z00099"));
        await _fx.ImportAllAsync(excluded.Id, TwoYearsAgo);

        var total = Ok(await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(null, null), CancellationToken.None));
        var accountOne = Ok(await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(one.Id, null), CancellationToken.None));
        var accountTwo = Ok(await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(excluded.Id, null), CancellationToken.None));
        var bucketTwo = _fx.Accounts.Buckets.Single(b => b.DefaultForAccountId == excluded.Id).Id;
        var byBucket = Ok(await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(null, bucketTwo), CancellationToken.None));

        total.Returns.Invested.ShouldBe(accountOne.Returns.Invested); // the second account is excluded from totals
        accountTwo.Returns.Invested.ShouldBe(100m);
        byBucket.OpenCount.ShouldBe(0); // totals only: the bucket belongs to the excluded account
        (await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(Guid.NewGuid(), null), CancellationToken.None)).Error.Code.ShouldBe(AccountErrors.NotFoundCode);
    }

    [Fact]
    public async Task The_group_xirr_is_computed_from_the_union_of_flows_and_closed_holdings_count_as_realised()
    {
        var account = await ImportedAsync();
        _fx.Dhan.Rows.RemoveAt(2);
        Ok(await _fx.SyncAsync(account.Id));

        var summary = Ok(await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(account.Id, null), CancellationToken.None));

        (summary.OpenCount, summary.ClosedCount).ShouldBe((2, 1));
        summary.Returns.RealisedPnl.ShouldBe(5m * (1500m - 1000m)); // CCC closed at its last price 1,500
        summary.Returns.RealisedEstimated.ShouldBeTrue();
        summary.Returns.Invested.ShouldBe(7000m);
    }

    [Fact]
    public async Task The_lots_of_a_holding_show_figures_per_lot_open_first()
    {
        var account = await ImportedAsync(rows: Row("AAA", 100, 50m, 60m, "1", "INE000A00001"));
        _fx.Dhan.Rows[0] = Row("AAA", 130, 53m, 70m, "1", "INE000A00001");
        Ok(await _fx.SyncAsync(account.Id));
        _fx.Dhan.Rows[0] = Row("AAA", 110, 55m, 80m, "1", "INE000A00001");
        Ok(await _fx.SyncAsync(account.Id));
        var id = _fx.HoldingOf("AAA").Id;

        var lots = Ok(await _fx.Lots().HandleAsync(new GetHoldingLotsQuery(id), CancellationToken.None));

        lots.Symbol.ShouldBe("AAA");
        lots.Lots.Select(l => (l.Status, l.Qty, l.AcquiredOn)).ShouldBe([("open", 80L, TwoYearsAgo), ("open", 30L, _fx.Today), ("closed", 20L, TwoYearsAgo)]);
        var closed = lots.Lots[2];
        (closed.ClosePrice, closed.Estimated, closed.ClosedOn, closed.Returns.RealisedPnl, closed.Returns.Pnl).ShouldBe((80m, true, _fx.Today, 600m, 600m));
        lots.Lots[0].Returns.Pnl.ShouldBe(80m * 30m);
        (await _fx.Lots().HandleAsync(new GetHoldingLotsQuery(Guid.NewGuid()), CancellationToken.None)).Error.ShouldBe(PortfolioErrors.HoldingNotFound);
    }

    [Fact]
    public async Task Changing_a_lot_date_clears_the_default_flag_updates_the_figures_and_is_audited()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.Add(Row("AAA", 100, 50m, 60m, "1", "INE000A00001"));
        Ok(await _fx.ImportAsync(account.Id, new ImportInput(new DateOnly(2025, 1, 1), [new ImportRowInput("INE000A00001", null)])));
        var holding = _fx.HoldingOf("AAA");
        var lot = holding.Lots.Single();
        lot.UsesDefaultDate.ShouldBeTrue();

        var changed = Ok(await _fx.UpdateLotDate().HandleAsync(new UpdateLotDateCommand(holding.Id, lot.Id, new LotDateInput(new DateOnly(2024, 10, 10)), Actor), CancellationToken.None));

        (changed.AcquiredOn, changed.UsesDefaultDate).ShouldBe((new DateOnly(2024, 10, 10), false));
        changed.Returns.Xirr.ShouldNotBeNull().ShouldBe(9.5445m, 0.001m);
        _fx.Events(AccountEventType.LotDateChanged).Single().Detail.ShouldBe("Lot date of AAA changed from 2025-01-01 to 2024-10-10");
        var summary = Ok(await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(null, null), CancellationToken.None));
        summary.DefaultDateLots.ShouldBe(0);
    }

    [Fact]
    public async Task A_bad_lot_date_unknown_ids_and_a_missing_body_are_rejected()
    {
        await ImportedAsync(rows: Row("AAA", 100, 50m, 60m, "1", "INE000A00001"));
        var holding = _fx.HoldingOf("AAA");
        var lot = holding.Lots.Single();

        (await _fx.UpdateLotDate().HandleAsync(new UpdateLotDateCommand(holding.Id, lot.Id, new LotDateInput(_fx.Today.AddDays(1)), Actor), CancellationToken.None)).Error.Code.ShouldBe(PortfolioErrors.InvalidCode);
        (await _fx.UpdateLotDate().HandleAsync(new UpdateLotDateCommand(holding.Id, lot.Id, new LotDateInput(null), Actor), CancellationToken.None)).Error.Code.ShouldBe(PortfolioErrors.InvalidCode);
        (await _fx.UpdateLotDate().HandleAsync(new UpdateLotDateCommand(holding.Id, Guid.NewGuid(), new LotDateInput(_fx.Today), Actor), CancellationToken.None)).Error.ShouldBe(PortfolioErrors.LotNotFound);
        (await _fx.UpdateLotDate().HandleAsync(new UpdateLotDateCommand(Guid.NewGuid(), lot.Id, new LotDateInput(_fx.Today), Actor), CancellationToken.None)).Error.ShouldBe(PortfolioErrors.HoldingNotFound);
        lot.AcquiredOn.ShouldBe(TwoYearsAgo);
        _fx.Events(AccountEventType.LotDateChanged).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_sync_status_shows_the_setting_the_latest_run_and_the_next_automatic_run()
    {
        var account = await _fx.ConnectedAsync();
        var off = Ok(await _fx.SyncStatus().HandleAsync(new GetSyncStatusQuery(account.Id), CancellationToken.None));
        (off.Mode, off.DailyTimeIst, off.FirstImportDate, off.HasHoldings, off.NextRunAt, off.Running, off.Last)
            .ShouldBe(("off", "16:00", new DateOnly(2025, 1, 1), false, (DateTimeOffset?)null, false, null));

        await _fx.Connections.Accounts.UpdatePortfolio().HandleAsync(
            new UpdatePortfolioCommand(account.Id, new PortfolioInput(null, null, "daily", "17:30"), Actor), CancellationToken.None);
        _fx.Dhan.Rows.Add(Row());
        Ok(await _fx.SyncAsync(account.Id));
        var status = Ok(await _fx.SyncStatus().HandleAsync(new GetSyncStatusQuery(account.Id), CancellationToken.None));

        (status.Mode, status.DailyTimeIst, status.HasHoldings, status.Last!.Status, status.Last.Trigger).ShouldBe(("daily", "17:30", true, "succeeded", "import"));
        status.NextRunAt.ShouldBe(new DateTimeOffset(2026, 10, 10, 17, 30, 0, TimeSpan.FromMinutes(330)));
        (await _fx.SyncStatus().HandleAsync(new GetSyncStatusQuery(Guid.NewGuid()), CancellationToken.None)).Error.Code.ShouldBe(AccountErrors.NotFoundCode);
    }

    [Fact]
    public async Task A_holding_without_a_price_is_marked_unpriced_has_no_value_and_is_counted_apart_in_the_summary()
    {
        await ImportedAsync(rows: [Row("AAA", 100, 50m, 60m, "1", "INE000A00001"), Row("NEW", 10, 300m, 0m, "2", "INE000N00002")]);

        var rows = (await ListAsync(Req(sort: "symbol"))).Items;
        var byValue = (await ListAsync(Req(sort: "-value"))).Items.Select(i => i.Symbol);
        var summary = Ok(await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(null, null), CancellationToken.None));

        var unpriced = rows.Single(r => r.Symbol == "NEW");
        (unpriced.Priced, unpriced.Returns.Invested, unpriced.Returns.Value, unpriced.Returns.Pnl, unpriced.Returns.PnlPct, unpriced.Returns.Xirr, unpriced.Returns.XirrState)
            .ShouldBe((false, 3000m, (decimal?)null, (decimal?)null, (decimal?)null, (decimal?)null, "no_price"));
        rows.Single(r => r.Symbol == "AAA").Priced.ShouldBeTrue();
        byValue.ShouldBe(["AAA", "NEW"]); // no value: last
        (summary.UnpricedCount, summary.OpenCount).ShouldBe((1, 2));
        (summary.Returns.Invested, summary.Returns.Value, summary.Returns.Pnl).ShouldBe((5000m, (decimal?)6000m, (decimal?)1000m));
        var lots = Ok(await _fx.Lots().HandleAsync(new GetHoldingLotsQuery(unpriced.Id), CancellationToken.None));
        (lots.Lots.Single().Returns.XirrState, lots.Lots.Single().Returns.Value).ShouldBe(("no_price", (decimal?)null));
    }

    [Fact]
    public async Task The_last_sync_of_the_total_ignores_accounts_that_do_not_count_in_totals()
    {
        var counted = await ImportedAsync();
        _fx.Clock.Advance(TimeSpan.FromMinutes(5));
        var excluded = await _fx.ConnectedAsync("Example Two", "1000000002", portfolio: new PortfolioInput(null, false));
        _fx.Dhan.Rows.Clear();
        _fx.Dhan.Rows.Add(Row("ZZZ", 1, 100m, 100m, "99", "INE000Z00099"));
        await _fx.ImportAllAsync(excluded.Id, TwoYearsAgo);

        var total = Ok(await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(null, null), CancellationToken.None));
        var own = Ok(await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(excluded.Id, null), CancellationToken.None));

        total.LastSync!.AccountId.ShouldBe(counted.Id);
        own.LastSync!.AccountId.ShouldBe(excluded.Id);
    }

    [Fact]
    public async Task A_lot_edit_that_collides_with_a_concurrent_change_is_a_conflict_and_changes_nothing()
    {
        await ImportedAsync(rows: Row("AAA", 100, 50m, 60m, "1", "INE000A00001"));
        var holding = _fx.HoldingOf("AAA");
        _fx.Data.CollidingSaves = 1;

        var result = await _fx.UpdateLotDate().HandleAsync(new UpdateLotDateCommand(holding.Id, holding.Lots.Single().Id, new LotDateInput(_fx.Today), Actor), CancellationToken.None);

        result.Error.ShouldBe(PortfolioErrors.LotChanged);
        _fx.Events(AccountEventType.LotDateChanged).Count.ShouldBe(1); // recorded with the (discarded) change; the real store drops it with the rest
    }

    [Fact]
    public async Task A_lot_edit_that_conflicts_again_after_its_repeat_is_the_lot_changed_conflict_and_not_an_error()
    {
        await ImportedAsync(rows: Row("AAA", 100, 50m, 60m, "1", "INE000A00001"));
        var holding = _fx.HoldingOf("AAA");
        var before = holding.Lots.Single().AcquiredOn;
        _fx.Data.ConflictingRuns = 1;

        var result = await _fx.UpdateLotDate().HandleAsync(new UpdateLotDateCommand(holding.Id, holding.Lots.Single().Id, new LotDateInput(_fx.Today), Actor), CancellationToken.None);

        result.Error.ShouldBe(PortfolioErrors.LotChanged);
        result.Error.Message.ShouldBe("The holding changed while you edited; reload and try again");
        _fx.HoldingOf("AAA").Lots.Single().AcquiredOn.ShouldBe(before);
    }
}
