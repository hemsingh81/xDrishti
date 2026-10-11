using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>Preview and the internal import of holdings with acquisition dates (clock: Saturday 2026-10-10, India).</summary>
public sealed class HoldingsImporterTests
{
    private readonly PortfolioFixture _fx = new();

    private static readonly DateOnly DefaultDate = new(2025, 1, 1);

    private async Task<BrokerAccount> AccountWithDhanHoldingsAsync()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.AddRange([Row("AAA", 10, 100m, 120m, "1", "INE000A00001"), Row("BBB", 20, 200m, 190m, "2", "INE000B00002"), Row("CCC", 30, 50m, 55m, "3", null)]);
        return account;
    }

    [Fact]
    public async Task The_preview_lists_dhans_holdings_with_the_default_date_and_saves_nothing()
    {
        var account = await AccountWithDhanHoldingsAsync();
        var instrument = new InstrumentRef(Guid.NewGuid(), "AAA", "Alpha Limited");
        _fx.Instruments.Known["INE000A00001"] = instrument;

        var preview = Ok(await _fx.PreviewAsync(account.Id));

        preview.DefaultDate.ShouldBe(DefaultDate);
        preview.Rows.Select(r => (r.Key, r.Symbol, r.Name, r.Qty, r.Mapped, r.AlreadyImported, r.SuggestedDate)).ShouldBe(
        [
            ("INE000A00001", "AAA", "Alpha Limited", 10L, true, false, DefaultDate),
            ("INE000B00002", "BBB", "BBB", 20L, false, false, DefaultDate),
            ("3:ALL", "CCC", "CCC", 30L, false, false, DefaultDate),
        ]);
        preview.Rows[0].InstrumentId.ShouldBe(instrument.Id);
        preview.Rows[0].CanonicalSymbol.ShouldBe("AAA");
        _fx.Data.Holdings.ShouldBeEmpty();
        _fx.Data.Syncs.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_preview_proposes_the_first_import_date_and_marks_imported_rows_with_their_oldest_lot_date()
    {
        var account = await AccountWithDhanHoldingsAsync();
        Ok(await _fx.ImportAsync(account.Id, new ImportInput(null, [new ImportRowInput("INE000A00001", new DateOnly(2023, 5, 5))])));
        _fx.Dhan.Rows.Add(Row("DDD", 5, 10m, 11m, "4", "INE000D00004"));

        var preview = Ok(await _fx.PreviewAsync(account.Id));

        preview.DefaultDate.ShouldBe(new DateOnly(2025, 1, 1));
        var imported = preview.Rows.Single(r => r.Symbol == "AAA");
        (imported.AlreadyImported, imported.SuggestedDate).ShouldBe((true, new DateOnly(2023, 5, 5)));
        preview.Rows.Where(r => r.Symbol != "AAA").ShouldAllBe(r => !r.AlreadyImported && r.SuggestedDate == new DateOnly(2025, 1, 1));
    }

    [Fact]
    public async Task The_preview_needs_a_connection_and_the_portfolio_role()
    {
        var notConnected = await _fx.ConnectedAsync(connect: false);

        (await _fx.PreviewAsync(notConnected.Id)).Error.Code.ShouldBe(PortfolioErrors.NotConnectedCode);
        _fx.Dhan.Tokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_first_import_dates_each_lot_with_its_row_or_the_default_date_and_flags_the_default_ones()
    {
        var account = await AccountWithDhanHoldingsAsync();

        var result = Ok(await _fx.ImportAsync(account.Id, new ImportInput(
            DefaultDate, [new ImportRowInput("INE000A00001", new DateOnly(2023, 5, 5)), new ImportRowInput("INE000B00002", null), new ImportRowInput("3:ALL", null)])));

        (result.Imported, result.AlreadyImported, result.Unknown, result.DefaultDateLots).ShouldBe((3, 0, 0, 2));
        var a = _fx.HoldingOf("AAA").Lots.Single();
        (a.AcquiredOn, a.UsesDefaultDate, a.Source, a.Qty, a.UnitCost).ShouldBe((new DateOnly(2023, 5, 5), false, LotSource.Import, 10L, 100m));
        _fx.HoldingOf("BBB").Lots.Single().ShouldSatisfyAllConditions(l => l.AcquiredOn.ShouldBe(DefaultDate), l => l.UsesDefaultDate.ShouldBeTrue());
        _fx.HoldingOf("CCC").Lots.Single().UsesDefaultDate.ShouldBeTrue();
    }

    [Fact]
    public async Task Without_a_default_date_in_the_request_the_proposed_one_applies()
    {
        var account = await AccountWithDhanHoldingsAsync();

        Ok(await _fx.ImportAsync(account.Id, new ImportInput(null, [new ImportRowInput("INE000A00001", null)])));

        _fx.HoldingOf("AAA").Lots.Single().AcquiredOn.ShouldBe(DefaultDate);
    }

    [Fact]
    public async Task Importing_the_same_keys_again_changes_nothing()
    {
        var account = await AccountWithDhanHoldingsAsync();
        var input = new ImportInput(DefaultDate, [new ImportRowInput("INE000A00001", null), new ImportRowInput("INE000B00002", new DateOnly(2024, 2, 2))]);
        Ok(await _fx.ImportAsync(account.Id, input));
        var lots = _fx.Data.Holdings.SelectMany(h => h.Lots).Select(l => (l.HoldingId, l.AcquiredOn)).ToList();

        var again = Ok(await _fx.ImportAsync(account.Id, input with { DefaultDate = new DateOnly(2020, 1, 1) }));

        (again.Imported, again.AlreadyImported).ShouldBe((0, 2));
        _fx.Data.Holdings.SelectMany(h => h.Lots).Select(l => (l.HoldingId, l.AcquiredOn)).ShouldBe(lots);
    }

    [Fact]
    public async Task Unknown_keys_are_counted_duplicates_are_dropped_and_keys_match_without_regard_to_case()
    {
        var account = await AccountWithDhanHoldingsAsync();

        var result = Ok(await _fx.ImportAsync(account.Id, new ImportInput(DefaultDate,
            [new ImportRowInput("ine000a00001", null), new ImportRowInput("INE000A00001", new DateOnly(2020, 1, 1)), new ImportRowInput("NOPE", null)])));

        (result.Imported, result.Unknown).ShouldBe((1, 1));
        _fx.Data.Holdings.ShouldHaveSingleItem().Lots.Single().AcquiredOn.ShouldBe(DefaultDate); // the first of the duplicates wins
    }

    [Fact]
    public async Task A_closed_holding_that_is_imported_again_is_brought_back_with_the_chosen_date()
    {
        var account = await AccountWithDhanHoldingsAsync();
        Ok(await _fx.ImportAsync(account.Id, new ImportInput(DefaultDate, [new ImportRowInput("INE000A00001", null)])));
        var removed = _fx.Dhan.Rows[0];
        _fx.Dhan.Rows.RemoveAt(0);
        Ok(await _fx.SyncAsync(account.Id));
        _fx.HoldingOf("AAA").Status.ShouldBe(HoldingStatus.Closed);
        _fx.Dhan.Rows.Insert(0, removed with { Qty = 7 });

        var result = Ok(await _fx.ImportAsync(account.Id, new ImportInput(DefaultDate, [new ImportRowInput("INE000A00001", new DateOnly(2026, 9, 1))])));

        result.Imported.ShouldBe(1);
        var holding = _fx.HoldingOf("AAA");
        (holding.Status, holding.Qty).ShouldBe((HoldingStatus.Open, 7L));
        holding.Lots.Single(l => l.IsOpen).AcquiredOn.ShouldBe(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task Dates_in_the_future_or_before_1990_and_blank_keys_are_rejected_with_their_field_names()
    {
        var account = await AccountWithDhanHoldingsAsync();

        var result = await _fx.ImportAsync(account.Id, new ImportInput(
            new DateOnly(1989, 12, 31),
            [new ImportRowInput("INE000A00001", _fx.Today.AddDays(1)), new ImportRowInput(" ", null), new ImportRowInput("INE000B00002", new DateOnly(1990, 1, 1))]));

        result.Error.Code.ShouldBe(PortfolioErrors.InvalidCode);
        FieldsOf(result).ShouldBe(["defaultDate", "rows[0].acquiredOn", "rows[1].key"]);
        _fx.Data.Holdings.ShouldBeEmpty();
        _fx.Data.Syncs.ShouldBeEmpty();
        _fx.Dhan.Tokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task Today_and_1990_01_01_are_accepted_as_dates()
    {
        var account = await AccountWithDhanHoldingsAsync();

        Ok(await _fx.ImportAsync(account.Id, new ImportInput(_fx.Today, [new ImportRowInput("INE000A00001", new DateOnly(1990, 1, 1)), new ImportRowInput("INE000B00002", _fx.Today)])));

        _fx.HoldingOf("AAA").Lots.Single().AcquiredOn.ShouldBe(new DateOnly(1990, 1, 1));
    }

    [Fact]
    public async Task The_import_is_one_run_with_the_import_trigger_and_one_audit_entry_with_counts_only()
    {
        var account = await AccountWithDhanHoldingsAsync();

        Ok(await _fx.ImportAsync(account.Id, new ImportInput(DefaultDate, [new ImportRowInput("INE000A00001", null), new ImportRowInput("INE000B00002", new DateOnly(2024, 1, 1)), new ImportRowInput("X", null)])));

        var run = _fx.Data.Syncs.ShouldHaveSingleItem();
        (run.Trigger, run.Status, run.NewCount, run.UnchangedCount).ShouldBe((SyncTrigger.Import, SyncStatus.Succeeded, 2, 0));
        _fx.Events(AccountEventType.HoldingsImported).Single().Detail
            .ShouldBe("Holdings imported: 2 imported, 0 already imported, 1 not listed by Dhan; 1 lots on the default date 2025-01-01");
    }

    [Fact]
    public async Task Import_is_refused_while_a_sync_runs_and_when_not_connected()
    {
        var account = await AccountWithDhanHoldingsAsync();
        var input = new ImportInput(DefaultDate, [new ImportRowInput("INE000A00001", null)]);
        _fx.Data.OtherRunInProgress = true;

        (await _fx.ImportAsync(account.Id, input)).Error.ShouldBe(PortfolioErrors.SyncRunning);
        _fx.Data.OtherRunInProgress = false;
        var other = await _fx.ConnectedAsync("Example Two", "1000000002", connect: false);
        (await _fx.ImportAsync(other.Id, input)).Error.Code.ShouldBe(PortfolioErrors.NotConnectedCode);
        _fx.Data.Holdings.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failure_while_saving_imports_nothing_and_records_the_failed_run()
    {
        var account = await AccountWithDhanHoldingsAsync();
        _fx.Data.FailTransactionWith = new InvalidOperationException("boom");

        var result = await _fx.ImportAsync(account.Id, new ImportInput(DefaultDate, [new ImportRowInput("INE000A00001", null)]));

        result.Error.ShouldBe(PortfolioErrors.SyncFailed);
        _fx.Data.Syncs.Single().Status.ShouldBe(SyncStatus.Failed);
    }

    [Fact]
    public async Task Dhan_failing_during_the_import_is_a_recorded_failed_run()
    {
        var account = await AccountWithDhanHoldingsAsync();
        _fx.Dhan.Failure = new Application.Abstractions.Accounts.BrokerFailure(Application.Abstractions.Accounts.BrokerFailureKind.Unavailable);

        var result = await _fx.ImportAsync(account.Id, new ImportInput(DefaultDate, [new ImportRowInput("INE000A00001", null)]));

        result.Error.Code.ShouldBe("accounts.broker_error");
        _fx.Data.Syncs.Single().Status.ShouldBe(SyncStatus.Failed);
        _fx.Data.Holdings.ShouldBeEmpty();
    }
}
