using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>Reading an account's ledger from Dhan after its trades (ADR 0025, P2-13 criteria 3, 6, 9, 16): classes, identity, the watermark of its own, the listing with totals.</summary>
public sealed class SyncLedgerTests
{
    private static readonly DateOnly Sep2 = new(2026, 9, 2);

    private static BrokerLedgerSamples Samples => new();

    private sealed class BrokerLedgerSamples
    {
        public XDrishti.Application.Abstractions.Portfolio.BrokerLedgerRow[] All { get; } =
        [
            Line(Sep2, "V1", "RECEIPT", "FUNDS DEPOSIT THROUGH UPI", 25000m, 25000m),
            Line(new DateOnly(2026, 9, 5), "V2", "BILL", "BILL FOR 05-SEP-2026 NSE-CAPITAL EQUITY PURCHASE", -9000m, 16000m),
            Line(new DateOnly(2026, 9, 12), "V3", "RECEIPT", "DIVIDEND CREDIT EXMPL FINAL DIV", 1500m, 17500m),
            Line(new DateOnly(2026, 9, 14), "V4", "JOURNAL", "DP CHARGES 05-09-2026 EXMPL", -15.93m, 17484.07m),
            Line(new DateOnly(2026, 10, 3), "V5", "PAYBLE", "FUNDS WITHDRAWAL", -10000m, 7484.07m),
        ];
    }

    [Fact]
    public async Task The_ledger_is_read_after_the_trades_and_stored_with_a_class_and_the_raw_texts()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Ledger.AddRange(Samples.All);

        var view = await s.SyncOkAsync();

        view.Ledger.ShouldNotBeNull();
        (view.Ledger.Kind, view.Ledger.Status, view.Ledger.Counts.Stored, view.Ledger.Counts.WindowsDone).ShouldBe(("ledger", "succeeded", 5, 2));
        s.Data.Entries.Select(e => (e.VoucherNo, e.EntryClass, e.Amount)).Order().ShouldBe(
        [
            ("V1", LedgerClass.FundsIn, 25000m),
            ("V2", LedgerClass.Other, -9000m),
            ("V3", LedgerClass.Dividend, 1500m),
            ("V4", LedgerClass.Charges, -15.93m),
            ("V5", LedgerClass.FundsOut, -10000m),
        ]);
        var deposit = s.Data.Entries.Single(e => e.VoucherNo == "V1");
        (deposit.Narration, deposit.Description, deposit.Exchange, deposit.RunningBalance, deposit.RunId, deposit.AccountId).ShouldBe(("FUNDS DEPOSIT THROUGH UPI", "RECEIPT", "NSE-CAPITAL", 25000m, view.Ledger.Id, s.Account.Id));
    }

    [Fact]
    public async Task Reading_the_same_days_again_stores_nothing_new()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Ledger.AddRange(Samples.All);
        await s.SyncOkAsync();

        var again = await s.SyncOkAsync();

        (again.Ledger!.Counts.Stored, again.Ledger.Counts.Duplicates).ShouldBe((0, 5));
        s.Data.Entries.Count.ShouldBe(5);
    }

    [Fact]
    public async Task Two_genuinely_identical_lines_are_both_kept()
    {
        var s = await BrokerScenario.StartAsync();
        var line = Line(Sep2, "V1", "RECEIPT", "FUNDS DEPOSIT", 100m);
        s.Dhan.Ledger.AddRange([line, line]);

        await s.SyncOkAsync();
        await s.SyncOkAsync();

        s.Data.Entries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_ledger_has_a_watermark_of_its_own_and_reads_from_it_next_time()
    {
        var s = await BrokerScenario.StartAsync();
        await s.SyncOkAsync();
        s.Dhan.LedgerCalls.Clear();

        await s.SyncOkAsync(defaultFrom: true);

        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Ledger)].ShouldBe(new DateOnly(2026, 10, 10));
        s.Dhan.LedgerCalls.ShouldBe([(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10))]);
    }

    [Fact]
    public async Task A_failed_ledger_window_leaves_the_ledger_watermark_and_keeps_the_trades_that_were_stored()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.Ledger.AddRange(Samples.All);
        s.Dhan.LedgerFailure = (from, _) => from == new DateOnly(2026, 10, 1) ? new BrokerFailure(BrokerFailureKind.Unavailable) : null;

        var view = await s.SyncOkAsync();

        (view.Trades.Status, view.Ledger!.Status, view.Ledger.Counts.WindowsDone).ShouldBe(("succeeded", "failed", 1));
        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Ledger)].ShouldBe(new DateOnly(2026, 9, 30));
        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Trades)].ShouldBe(new DateOnly(2026, 10, 10));
        s.Data.Entries.Count.ShouldBe(4); // everything before October
    }

    [Fact]
    public async Task The_ledger_is_not_read_when_the_trades_failed()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.TradeFailure = (_, _, _) => new BrokerFailure(BrokerFailureKind.Unavailable);

        var view = await s.SyncOkAsync();

        view.Ledger.ShouldBeNull();
        s.Dhan.LedgerCalls.ShouldBeEmpty();
        s.RunsOf(BrokerSyncKind.Ledger).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_holdings_sync_that_starts_between_the_two_runs_leaves_the_ledger_for_the_next_run()
    {
        var s = await BrokerScenario.StartAsync();
        s.Data.OnTryStart = run =>
        {
            if (run.Kind == BrokerSyncKind.Ledger)
            {
                s.Fx.Data.Syncs.Add(HoldingSync.Start(s.Account.Id, SyncTrigger.Auto, "system", s.Fx.Clock.GetUtcNow()));
            }
        };

        var view = await s.SyncOkAsync();

        view.Trades.Status.ShouldBe("succeeded");
        view.Ledger!.Status.ShouldBe("skipped");
        view.Ledger.Error.ShouldBe("Another sync of this account started; the ledger is read by the next run.");
        s.Data.Watermarks.ContainsKey((s.Account.Id, BrokerSyncKind.Ledger)).ShouldBeFalse();
    }

    // ---- listing ----

    [Fact]
    public async Task The_ledger_is_listed_newest_first_with_the_totals_of_every_class_for_the_period()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Ledger.AddRange(Samples.All);
        await s.SyncOkAsync();

        var page = Ok(await s.Ledger().HandleAsync(new XDrishti.Application.Portfolio.BrokerHistory.ListLedgerQuery(s.Account.Id, null, null, null, null, null), CancellationToken.None));

        page.Items.Select(i => i.VoucherNo).ShouldBe(["V5", "V4", "V3", "V2", "V1"]);
        (page.Page, page.PageSize, page.Total).ShouldBe((1, 50, 5));
        page.Totals.Select(t => (t.Class, t.Count, t.Amount)).ShouldBe(
        [
            ("funds_in", 1, 25000m),
            ("funds_out", 1, -10000m),
            ("dividend", 1, 1500m),
            ("charges", 1, -15.93m),
            ("other", 1, -9000m),
        ]);
    }

    [Fact]
    public async Task The_list_can_be_filtered_by_class_and_dates_and_the_totals_follow_the_dates_only()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Ledger.AddRange(Samples.All);
        await s.SyncOkAsync();

        var page = Ok(await s.Ledger().HandleAsync(
            new XDrishti.Application.Portfolio.BrokerHistory.ListLedgerQuery(s.Account.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "dividend", 1, 10), CancellationToken.None));

        page.Items.Select(i => i.Class).ShouldBe(["dividend"]);
        page.Total.ShouldBe(1);
        page.Totals.Sum(t => t.Count).ShouldBe(4); // all the classes of September
        page.Totals.Single(t => t.Class == "funds_out").Count.ShouldBe(0);
        page.CoverageFrom.ShouldBe(Sep2); // the first date stored, whatever the filters say
    }

    [Fact]
    public async Task The_ledger_says_from_which_date_it_is_known_and_nothing_when_it_was_not_read()
    {
        var s = await BrokerScenario.StartAsync();
        var empty = Ok(await s.Ledger().HandleAsync(new XDrishti.Application.Portfolio.BrokerHistory.ListLedgerQuery(s.Account.Id, null, null, null, null, null), CancellationToken.None));
        s.Dhan.Ledger.AddRange(Samples.All);
        await s.SyncOkAsync();

        var read = Ok(await s.Ledger().HandleAsync(new XDrishti.Application.Portfolio.BrokerHistory.ListLedgerQuery(s.Account.Id, new DateOnly(2026, 10, 1), null, null, null, null), CancellationToken.None));

        empty.CoverageFrom.ShouldBeNull();
        (read.Items.Count, read.CoverageFrom).ShouldBe((1, Sep2));
    }

    [Fact]
    public async Task The_list_is_paged()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Ledger.AddRange(Samples.All);
        await s.SyncOkAsync();

        var second = Ok(await s.Ledger().HandleAsync(new XDrishti.Application.Portfolio.BrokerHistory.ListLedgerQuery(s.Account.Id, null, null, null, 2, 2), CancellationToken.None));

        second.Items.Select(i => i.VoucherNo).ShouldBe(["V3", "V2"]);
        second.Total.ShouldBe(5);
    }

    [Theory]
    [InlineData(0, 50, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 201, "pageSize")]
    public async Task A_page_out_of_range_is_refused_with_the_field(int page, int pageSize, string field)
    {
        var s = await BrokerScenario.StartAsync();

        var result = await s.Ledger().HandleAsync(new XDrishti.Application.Portfolio.BrokerHistory.ListLedgerQuery(s.Account.Id, null, null, null, page, pageSize), CancellationToken.None);

        result.Error.Code.ShouldBe("portfolio.invalid");
        FieldsOf(result).ShouldBe([field]);
    }

    [Fact]
    public async Task An_unknown_class_or_dates_the_wrong_way_round_are_refused_and_an_unknown_account_is_not_found()
    {
        var s = await BrokerScenario.StartAsync();

        var badClass = await s.Ledger().HandleAsync(new XDrishti.Application.Portfolio.BrokerHistory.ListLedgerQuery(s.Account.Id, null, null, "gifts", null, null), CancellationToken.None);
        var reversed = await s.Ledger().HandleAsync(new XDrishti.Application.Portfolio.BrokerHistory.ListLedgerQuery(s.Account.Id, new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 1), null, null, null), CancellationToken.None);
        var missing = await s.Ledger().HandleAsync(new XDrishti.Application.Portfolio.BrokerHistory.ListLedgerQuery(Guid.NewGuid(), null, null, null, null, null), CancellationToken.None);

        FieldsOf(badClass).ShouldBe(["class"]);
        FieldsOf(reversed).ShouldBe(["from"]);
        missing.Error.Code.ShouldBe("accounts.not_found");
    }

    [Fact]
    public async Task Hostile_text_in_a_narration_is_cleaned_and_capped_and_kept_as_text()
    {
        var s = await BrokerScenario.StartAsync();
        var long500 = "<script>alert(1)</script>\nline two\t" + new string('x', 600);
        s.Dhan.Ledger.Add(Line(Sep2, "V1", "RECEIPT", long500, 10m));

        await s.SyncOkAsync();

        var narration = s.Data.Entries.Single().Narration;
        narration.Length.ShouldBe(LedgerEntry.MaxNarrationLength);
        narration.ShouldStartWith("<script>alert(1)</script> line two ");
        narration.ShouldNotContain('\n');
    }
}
