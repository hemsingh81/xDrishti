using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>
/// A depository charge must count once however the two sources arrive (ADR 0029, review fix M2): a sheet imported before the ledger sync has read those days is replaced by Dhan's own entry (voided as <c>superseded</c>, one broker charge covering one line); a sheet imported after leaves the
/// covered lines out. Application tests with in-memory ports; the real database has its own.
/// </summary>
public sealed class DpLedgerSupersessionTests
{
    private const string Isin = "INE245A01021";

    private static readonly DateOnly Jun4 = new(2026, 6, 4);

    private static readonly DateOnly From = new(2026, 6, 1);

    private static readonly string Sell = DpLine(1, "04-Jun-2026", Isin, "THE EXAMPLE POWER COMPANY LIMITED", "11.000", "Sell", "Other Fees", "12.5");

    private static BrokerLedgerRow Charge(DateOnly date, string voucher, decimal paid) =>
        new(date, voucher, "JOURNAL", "DP CHARGES " + date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture) + " EXMPL", "NSE-CAPITAL", -paid, null);

    private static BrokerLedgerRow Other(DateOnly date, string voucher, decimal paid) =>
        new(date, voucher, "JOURNAL", "AMC FOR JUNE 2026", "NSE-CAPITAL", -paid, null);

    private static Task<ReportScenario> StartAsync() => ReportScenario.StartAsync();

    private static decimal ChargesTotal(ReportScenario s) =>
        s.Fx.BrokerData.Entries.Where(e => !e.IsVoided && e.EntryClass == LedgerClass.Charges).Sum(e => e.Amount);

    [Fact]
    public async Task A_sheet_imported_before_the_ledger_sync_is_replaced_by_dhans_charge_and_counted_once()
    {
        var s = await StartAsync();
        var done = await s.ImportOkAsync(Dp([Sell]));
        s.History.Ledger.Add(Charge(Jun4, "V9", 12.5m));

        var view = await s.SyncHistoryAsync(From);

        var imported = s.Fx.BrokerData.Entries.Single(e => e.Source == LedgerSource.Import);
        (imported.IsVoided, imported.VoidedBy).ShouldBe((true, "superseded"));
        s.Fx.BrokerData.Entries.Single(e => e.Source == LedgerSource.Broker).IsVoided.ShouldBeFalse();
        ChargesTotal(s).ShouldBe(-12.5m);
        (view.Ledger!.Counts.Stored, view.Ledger.Counts.Superseded).ShouldBe((1, 1));
        done.Costs!.Batch!.Id.ShouldBe(imported.BatchId!.Value);
    }

    [Fact]
    public async Task The_audit_event_of_the_ledger_run_counts_the_replaced_charges_and_nothing_else()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Dp([Sell]));
        s.History.Ledger.Add(Charge(Jun4, "V9", 12.5m));

        await s.SyncHistoryAsync(From);

        var detail = s.Fx.Events(AccountEventType.BrokerHistorySynced).Single(e => e.Detail!.Contains("ledger", StringComparison.Ordinal)).Detail!;
        detail.ShouldContain("1 imported depository charges replaced");
        detail.ShouldNotContain("EXMPL");
        detail.ShouldNotContain("12.5");
    }

    [Fact]
    public async Task A_sheet_imported_after_the_ledger_sync_leaves_the_covered_line_out_and_nothing_is_superseded()
    {
        var s = await StartAsync();
        s.History.Ledger.Add(Charge(Jun4, "V9", 12.5m));
        var view = await s.SyncHistoryAsync(From);

        var done = await s.ImportOkAsync(Dp([Sell]));

        (done.Costs!.Imported, done.Costs.AlreadyInLedger).ShouldBe((0, 1));
        view.Ledger!.Counts.Superseded.ShouldBe(0);
        ChargesTotal(s).ShouldBe(-12.5m);
    }

    [Fact]
    public async Task Two_identical_lines_on_one_day_are_covered_one_by_one()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Dp([Sell, Sell]));
        s.History.Ledger.Add(Charge(Jun4, "V9", 12.5m));

        var view = await s.SyncHistoryAsync(From);

        var imported = s.Fx.BrokerData.Entries.Where(e => e.Source == LedgerSource.Import).ToList();
        imported.Count(e => e.IsVoided).ShouldBe(1);
        imported.Count(e => !e.IsVoided).ShouldBe(1);
        ChargesTotal(s).ShouldBe(-25m);
        view.Ledger!.Counts.Superseded.ShouldBe(1);
    }

    [Fact]
    public async Task Both_lines_are_replaced_when_the_ledger_has_both_charges()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Dp([Sell, Sell]));
        s.History.Ledger.AddRange([Charge(Jun4, "V9", 12.5m), Charge(Jun4, "V10", 12.5m)]);

        await s.SyncHistoryAsync(From);

        s.Fx.BrokerData.Entries.Where(e => e.Source == LedgerSource.Import).ShouldAllBe(e => e.IsVoided);
        ChargesTotal(s).ShouldBe(-25m);
    }

    [Fact]
    public async Task A_charge_of_another_day_or_amount_replaces_nothing()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Dp([Sell]));
        s.History.Ledger.AddRange([Charge(new DateOnly(2026, 6, 5), "V9", 12.5m), Charge(Jun4, "V10", 13m)]);

        var view = await s.SyncHistoryAsync(From);

        s.ActiveCosts().Count.ShouldBe(1);
        view.Ledger!.Counts.Superseded.ShouldBe(0);
        ChargesTotal(s).ShouldBe(-12.5m - 12.5m - 13m);
    }

    [Fact]
    public async Task Reading_the_same_days_again_replaces_nothing_more()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Dp([Sell, Sell]));
        s.History.Ledger.Add(Charge(Jun4, "V9", 12.5m));
        await s.SyncHistoryAsync(From);

        var again = await s.SyncHistoryAsync(From);

        again.Ledger!.Counts.Superseded.ShouldBe(0);
        s.Fx.BrokerData.Entries.Count(e => e.Source == LedgerSource.Import && e.IsVoided).ShouldBe(1);
    }

    [Fact]
    public async Task Undoing_the_batch_after_a_replacement_voids_only_what_still_counts()
    {
        var s = await StartAsync();
        var done = await s.ImportOkAsync(Dp([Sell, Sell]));
        s.History.Ledger.Add(Charge(Jun4, "V9", 12.5m));
        await s.SyncHistoryAsync(From);

        var undone = Ok(await s.UndoCostsAsync(done.Costs!.Batch!.Id));

        undone.Voided.ShouldBe(1);
        s.Fx.BrokerData.Entries.Where(e => e.Source == LedgerSource.Import).Select(e => e.VoidedBy).Order().ShouldBe(["owner@example.invalid", "superseded"]);
        ChargesTotal(s).ShouldBe(-12.5m);
    }

    [Fact]
    public async Task Undoing_a_batch_that_was_wholly_replaced_voids_nothing_and_the_sheet_can_come_again_without_doubling()
    {
        var s = await StartAsync();
        var done = await s.ImportOkAsync(Dp([Sell]));
        s.History.Ledger.Add(Charge(Jun4, "V9", 12.5m));
        await s.SyncHistoryAsync(From);

        var undone = Ok(await s.UndoCostsAsync(done.Costs!.Batch!.Id));
        var again = await s.ImportOkAsync(Dp([Sell]));

        undone.Voided.ShouldBe(0);
        (again.Costs!.Imported, again.Costs.AlreadyInLedger).ShouldBe((0, 1));
        ChargesTotal(s).ShouldBe(-12.5m);
    }

    [Fact]
    public async Task When_the_ledger_has_depository_charges_that_day_another_charge_of_the_same_amount_replaces_nothing()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Dp([Sell]));
        s.History.Ledger.AddRange([Charge(Jun4, "V9", 20m), Other(Jun4, "V10", 12.5m)]);

        var view = await s.SyncHistoryAsync(From);

        view.Ledger!.Counts.Superseded.ShouldBe(0);
        s.Fx.BrokerData.Entries.Single(e => e.Source == LedgerSource.Import).IsVoided.ShouldBeFalse();
    }

    [Fact]
    public async Task When_the_ledger_has_depository_charges_that_day_another_charge_of_the_same_amount_does_not_excuse_a_line_of_a_later_sheet()
    {
        var s = await StartAsync();
        s.History.Ledger.AddRange([Charge(Jun4, "V9", 20m), Other(Jun4, "V10", 12.5m)]);
        await s.SyncHistoryAsync(From);

        var done = await s.ImportOkAsync(Dp([Sell]));

        (done.Costs!.Imported, done.Costs.AlreadyInLedger).ShouldBe((1, 0));
    }

    [Fact]
    public async Task A_depository_charge_of_the_same_amount_still_replaces_the_line_beside_another_charge_of_that_amount()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Dp([Sell]));
        s.History.Ledger.AddRange([Other(Jun4, "V9", 12.5m), Charge(Jun4, "V10", 12.5m)]);

        var view = await s.SyncHistoryAsync(From);

        view.Ledger!.Counts.Superseded.ShouldBe(1);
    }

    [Fact]
    public async Task When_no_charge_of_the_day_names_a_depository_charge_the_amount_alone_matches_before_and_after_the_sheet()
    {
        var before = await StartAsync();
        await before.ImportOkAsync(Dp([Sell]));
        before.History.Ledger.Add(Other(Jun4, "V9", 12.5m));
        var replaced = await before.SyncHistoryAsync(From);
        var after = await StartAsync();
        after.History.Ledger.Add(Other(Jun4, "V9", 12.5m));
        await after.SyncHistoryAsync(From);

        var done = await after.ImportOkAsync(Dp([Sell]));

        replaced.Ledger!.Counts.Superseded.ShouldBe(1);
        (done.Costs!.Imported, done.Costs.AlreadyInLedger).ShouldBe((0, 1));
    }

    [Theory]
    [InlineData("DP CHARGES 04-06-2026 EXMPL", true)]
    [InlineData("Depository charges incl. GST", true)]
    [InlineData("DP FEES JUNE", true)]
    [InlineData("DEMAT CHARGE", true)]
    [InlineData("AMC FOR JUNE 2026", false)]
    [InlineData("BROKERAGE", false)]
    [InlineData("DPC INTEREST", false)]
    [InlineData("", false)]
    public void A_ledger_text_names_a_depository_charge_when_it_has_one_of_the_phrases(string narration, bool expected) =>
        DepositoryCharges.Is("JOURNAL", narration).ShouldBe(expected);

    [Fact]
    public void The_open_charges_of_a_day_are_its_depository_charges_or_else_all_of_them()
    {
        var other = Jun4.AddDays(1);

        var open = LedgerSupersession.OpenCharges(
        [
            new LedgerChargeOnDay(Jun4, 12.5m, true), new LedgerChargeOnDay(Jun4, 12.5m, false), new LedgerChargeOnDay(Jun4, 7m, false),
            new LedgerChargeOnDay(other, 12.5m, false), new LedgerChargeOnDay(other, 7m, false),
        ]);

        open.ShouldBe(new Dictionary<(DateOnly, decimal), int> { [(Jun4, 12.5m)] = 1, [(other, 12.5m)] = 1, [(other, 7m)] = 1 }, ignoreOrder: true);
    }

    [Fact]
    public void The_covering_is_one_broker_charge_per_line_oldest_first_to_the_paisa()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var imported = new (Guid, DateOnly, decimal)[] { (ids[0], Jun4, 12.5m), (ids[1], Jun4, 12.5m), (ids[2], Jun4, 7m), (ids[3], Jun4.AddDays(1), 12.5m) };

        var covered = LedgerSupersession.Covered(imported, [new LedgerChargeOnDay(Jun4, 12.504m), new LedgerChargeOnDay(Jun4.AddDays(1), 99m)]);

        covered.ShouldBe([ids[0]]);
    }
}
