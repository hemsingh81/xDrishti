using XDrishti.Application.Tests.Portfolio.BrokerHistory;
using XDrishti.Application.Tests.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportScenario;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>
/// Source precedence (ADR 0029 §7, P2-17 criterion 8): per account, day, ISIN and side a group of the broker's own trades beats the bill rows of a downloaded report. A report imported after the broker sync leaves the covered rows out (counted, the days whose totals differ
/// listed); a broker sync after the report voids the bill rows it replaces as <c>superseded</c>. Application tests with in-memory ports; the real database has its own.
/// </summary>
public sealed class BillPrecedenceTests
{
    private const string Name = "ZENITH ENERGY SYSTEM LTD";

    private static readonly DateOnly Apr7 = new(2026, 4, 7);

    private static readonly DateOnly Apr20 = new(2026, 4, 20);

    private static readonly string Buy1 = GtrRow("07 Apr 2026 00:00:00", Name, "NSE", "1001", 100, "1000.00", 0, "0.00");

    private static readonly string Buy2 = GtrRow("20 Apr 2026 00:00:00", Name, "NSE", "1002", 50, "600.00", 0, "0.00");

    private static readonly string Sell = GtrRow("04 May 2026 00:00:00", Name, "NSE", "1003", 0, "0.00", 120, "1800.00");

    private static Task<ReportScenario> StartAsync() => ReportScenario.StartAsync(s =>
    {
        s.Hold("ZENITHE", Zenith, 30, 12m);
        s.ListSampleCompanies();
    });

    private static Transaction Dhan(ReportScenario s, string id, DateOnly date, TradeSide side, long qty, decimal price)
    {
        var trade = Transaction.Create(s.Account.Id, null, Zenith, "ZENITHE", "NSE", side, date, new TimeOnly(10, 15), qty, price, 0m, TradeSource.Broker, null, 1, "t:" + date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + ":" + id, "system:broker-history", s.Fx.Clock.GetUtcNow(), Guid.NewGuid());
        s.Fx.Trades.Add([trade]);
        s.Fx.Trades.ForgetCollected();
        return trade;
    }

    [Fact]
    public async Task A_bill_row_whose_day_isin_and_side_the_broker_has_is_left_out_as_covered_by_dhan()
    {
        var s = await StartAsync();
        Dhan(s, "A", Apr7, TradeSide.Buy, 60, 10m);
        Dhan(s, "B", Apr7, TradeSide.Buy, 40, 10m);

        var preview = await s.PreviewOkAsync(Gtr([Buy1, Buy2, Sell]));

        preview.Trades!.File.Skipped.ByReason["covered_by_dhan"].ShouldBe(1);
        preview.Summary!.ToAdd.ShouldBe(2);
        preview.Bill!.Coverage.Covered.ShouldBe(1);
        preview.Bill.Coverage.DifferencesTotal.ShouldBe(0);
        preview.Warnings.ShouldContain(w => w.Code == "covered_by_dhan");
        preview.Warnings.ShouldNotContain(w => w.Code == "totals_differ");
    }

    [Fact]
    public async Task A_covered_day_whose_total_differs_is_listed_with_both_quantities_and_the_brokers_wins()
    {
        var s = await StartAsync();
        Dhan(s, "A", Apr20, TradeSide.Buy, 45, 12m);

        var preview = await s.PreviewOkAsync(Gtr([Buy1, Buy2, Sell]));

        var difference = preview.Bill!.Coverage.Differences.Single();
        (difference.Date, difference.Isin, difference.Symbol, difference.Side, difference.BillQty, difference.DhanQty).ShouldBe((Apr20, Zenith, "ZENITHE", "buy", 50L, 45L));
        preview.Bill.Coverage.DifferencesTotal.ShouldBe(1);
        preview.Warnings.ShouldContain(w => w.Code == "totals_differ" && w.Count == 1);
    }

    [Fact]
    public async Task An_import_stores_only_what_the_broker_does_not_have_and_counts_what_it_left_out()
    {
        var s = await StartAsync();
        Dhan(s, "A", Apr7, TradeSide.Buy, 100, 10m);
        Dhan(s, "B", Apr20, TradeSide.Buy, 50, 12m);

        var done = await s.ImportOkAsync(Gtr([Buy1, Buy2, Sell]), rebuild: false);

        done.Trades!.Imported.ShouldBe(1);
        s.Active().Where(t => t.Source == TradeSource.Bill).Select(t => (t.Side, t.Qty)).ShouldBe([(TradeSide.Sell, 120L)]);
        done.Trades.Report.File.Skipped.ByReason["covered_by_dhan"].ShouldBe(2);
        s.Fx.Events(AccountEventType.TradesImported).Single().Detail!.ShouldContain("2 skipped");
    }

    [Fact]
    public async Task The_other_side_of_a_covered_day_and_other_securities_are_not_covered()
    {
        var s = await StartAsync();
        Dhan(s, "A", Apr7, TradeSide.Sell, 5, 10m);
        var mixed = GtrRow("07 Apr 2026 00:00:00", Name, "NSE", "1009", 10, "100.00", 4, "50.00");

        var preview = await s.PreviewOkAsync(Gtr([mixed]));

        preview.Bill!.Coverage.Covered.ShouldBe(1);
        preview.Summary!.ToAdd.ShouldBe(1);
    }

    [Fact]
    public async Task A_voided_broker_trade_covers_nothing()
    {
        var s = await StartAsync();
        Dhan(s, "A", Apr7, TradeSide.Buy, 100, 10m).Void("owner", s.Fx.Clock.GetUtcNow());

        var preview = await s.PreviewOkAsync(Gtr([Buy1]));

        preview.Bill!.Coverage.Covered.ShouldBe(0);
        preview.Summary!.ToAdd.ShouldBe(1);
    }

    [Fact]
    public async Task A_csv_tradebook_trade_does_not_cover_a_bill_row_only_the_brokers_own_do()
    {
        var s = await StartAsync();
        var csv = Transaction.Create(s.Account.Id, null, Zenith, "ZENITHE", "NSE", TradeSide.Buy, Apr7, null, 100, 10m, 0m, TradeSource.Csv, null, 1, "t:20260407:CSV1", "owner", s.Fx.Clock.GetUtcNow());
        s.Fx.Trades.Add([csv]);
        s.Fx.Trades.ForgetCollected();

        var preview = await s.PreviewOkAsync(Gtr([Buy1]));

        preview.Bill!.Coverage.Covered.ShouldBe(0);
    }

    // ---- the broker sync after the report ----

    private static Transaction BillRow(BrokerScenario s, string id, DateOnly date, TradeSide side, long qty, decimal price)
    {
        var trade = Transaction.Create(s.Account.Id, null, TradeScenario.Isin, "EXMPL", "NSE", side, date, null, qty, price, 0m, TradeSource.Bill, null, 0, id, "owner", s.Fx.Clock.GetUtcNow());
        s.Fx.Trades.Add([trade]);
        s.Fx.Trades.ForgetCollected();
        return trade;
    }

    [Fact]
    public async Task A_broker_sync_that_stores_a_group_voids_the_bill_rows_of_that_group_as_superseded()
    {
        var s = await BrokerScenario.StartAsync();
        var buy = BillRow(s, "g:1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m);
        var sell = BillRow(s, "g:2", new DateOnly(2026, 9, 20), TradeSide.Sell, 3, 130m);
        var other = BillRow(s, "g:3", new DateOnly(2026, 9, 25), TradeSide.Buy, 1, 100m);
        s.Dhan.Trades.AddRange(BrokerScenario.BaseTrades().Select(t => t with { Isin = TradeScenario.Isin }));

        var view = await s.SyncOkAsync();

        buy.IsVoided.ShouldBeTrue();
        sell.IsVoided.ShouldBeTrue();
        other.IsVoided.ShouldBeFalse();
        (buy.VoidedBy, sell.VoidedBy).ShouldBe(("superseded", "superseded"));
        buy.SupersededBy.ShouldBe(view.Trades.Id);
        other.SupersededBy.ShouldBeNull();
        s.BrokerTrades().Count.ShouldBe(3);
        (view.Trades.Counts.Superseded, view.Trades.Counts.SupersededDiffering).ShouldBe((2, 0));
    }

    [Fact]
    public async Task A_group_whose_total_differs_is_still_replaced_and_counted_as_differing()
    {
        var s = await BrokerScenario.StartAsync();
        var buy = BillRow(s, "g:1", new DateOnly(2026, 9, 15), TradeSide.Buy, 4, 110m);
        s.Dhan.Trades.AddRange(BrokerScenario.BaseTrades().Select(t => t with { Isin = TradeScenario.Isin }));

        var view = await s.SyncOkAsync();

        buy.IsVoided.ShouldBeTrue();
        (view.Trades.Counts.Superseded, view.Trades.Counts.SupersededDiffering).ShouldBe((1, 1));
    }

    [Fact]
    public async Task Several_bill_rows_of_one_group_are_all_replaced_and_compared_by_their_total()
    {
        var s = await BrokerScenario.StartAsync();
        var first = BillRow(s, "g:1", new DateOnly(2026, 9, 2), TradeSide.Buy, 6, 90m);
        var second = BillRow(s, "g:2", new DateOnly(2026, 9, 2), TradeSide.Buy, 4, 90m);
        s.Dhan.Trades.AddRange(BrokerScenario.BaseTrades().Select(t => t with { Isin = TradeScenario.Isin }));

        var view = await s.SyncOkAsync();

        (first.IsVoided, second.IsVoided).ShouldBe((true, true));
        (view.Trades.Counts.Superseded, view.Trades.Counts.SupersededDiffering).ShouldBe((2, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task A_group_of_several_fills_is_replaced_once_and_compared_by_its_whole_total_also_when_the_window_is_saved_row_by_row(int refusedSaves)
    {
        var s = await BrokerScenario.StartAsync();
        var date = new DateOnly(2026, 9, 2);
        var bill = BillRow(s, "g:1", date, TradeSide.Buy, 10, 90m);
        s.Dhan.Trades.AddRange(
        [
            BrokerScenario.Trade("T1a", date, TradeSide.Buy, 6, 90m, isin: TradeScenario.Isin),
            BrokerScenario.Trade("T1b", date, TradeSide.Buy, 4, 90m, isin: TradeScenario.Isin),
        ]);
        s.Fx.Data.CollidingSaves = refusedSaves; // the database refuses the window as a whole: its rows are saved one by one

        var view = await s.SyncOkAsync();

        s.Fx.Data.CollidingSaves.ShouldBe(0); // the refusal was met
        bill.IsVoided.ShouldBeTrue();
        s.BrokerTrades().Count.ShouldBe(2);
        (view.Trades.Counts.Stored, view.Trades.Counts.Superseded, view.Trades.Counts.SupersededDiffering).ShouldBe((2, 1, 0));
    }

    [Fact]
    public async Task A_group_whose_fills_are_split_over_row_by_row_saves_and_differs_from_the_bill_is_counted_as_one_differing_group()
    {
        var s = await BrokerScenario.StartAsync();
        var date = new DateOnly(2026, 9, 2);
        BillRow(s, "g:1", date, TradeSide.Buy, 12, 90m);
        s.Dhan.Trades.AddRange(
        [
            BrokerScenario.Trade("T1a", date, TradeSide.Buy, 6, 90m, isin: TradeScenario.Isin),
            BrokerScenario.Trade("T1b", date, TradeSide.Buy, 4, 90m, isin: TradeScenario.Isin),
        ]);
        s.Fx.Data.CollidingSaves = 1;

        var view = await s.SyncOkAsync();

        (view.Trades.Counts.Superseded, view.Trades.Counts.SupersededDiffering).ShouldBe((1, 1));
    }

    [Fact]
    public async Task A_voided_bill_row_is_not_voided_again_and_a_sync_that_stores_nothing_supersedes_nothing()
    {
        var s = await BrokerScenario.StartAsync();
        var buy = BillRow(s, "g:1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m);
        s.Dhan.Trades.AddRange(BrokerScenario.BaseTrades().Select(t => t with { Isin = TradeScenario.Isin }));
        await s.SyncOkAsync();
        var first = buy.VoidedAt;

        var again = await s.SyncOkAsync();

        buy.VoidedAt.ShouldBe(first);
        again.Trades.Counts.Superseded.ShouldBe(0);
    }

    [Fact]
    public async Task The_sync_leaves_csv_manual_and_other_accounts_bill_rows_alone()
    {
        var s = await BrokerScenario.StartAsync();
        var csv = Transaction.Create(s.Account.Id, null, TradeScenario.Isin, "EXMPL", "NSE", TradeSide.Buy, new DateOnly(2026, 9, 2), null, 10, 90m, 0m, TradeSource.Csv, null, 0, "t:20260902:CSV", "owner", s.Fx.Clock.GetUtcNow());
        var elsewhere = Transaction.Create(Guid.NewGuid(), null, TradeScenario.Isin, "EXMPL", "NSE", TradeSide.Buy, new DateOnly(2026, 9, 2), null, 10, 90m, 0m, TradeSource.Bill, null, 0, "g:x", "owner", s.Fx.Clock.GetUtcNow());
        s.Fx.Trades.Add([csv, elsewhere]);
        s.Fx.Trades.ForgetCollected();
        s.Dhan.Trades.AddRange(BrokerScenario.BaseTrades().Select(t => t with { Isin = TradeScenario.Isin }));

        await s.SyncOkAsync();

        csv.IsVoided.ShouldBeFalse();
        elsewhere.IsVoided.ShouldBeFalse();
    }

    [Fact]
    public async Task The_runs_audit_event_says_how_many_bill_rows_it_replaced()
    {
        var s = await BrokerScenario.StartAsync();
        BillRow(s, "g:1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m);
        s.Dhan.Trades.AddRange(BrokerScenario.BaseTrades().Select(t => t with { Isin = TradeScenario.Isin }));

        await s.SyncOkAsync();

        s.Fx.Events(AccountEventType.BrokerHistorySynced).First().Detail!.ShouldContain("1 bill-level rows of imported reports replaced");
    }
}
