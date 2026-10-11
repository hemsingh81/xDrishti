using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Tests.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>Saving the Portfolio settings carries out the first import (ADR 0013). Clock: Saturday 2026-10-10, India.</summary>
public sealed class FirstImportOnSaveTests
{
    private readonly PortfolioFixture _fx = new();

    private static readonly DateOnly Chosen = new(2024, 4, 1);

    private async Task<Result<PortfolioSaved>> SaveAsync(Guid accountId, PortfolioInput input) =>
        await _fx.Connections.Accounts.UpdatePortfolio().HandleAsync(new UpdatePortfolioCommand(accountId, input, Actor), CancellationToken.None);

    private static ImportRowInput R(string key, DateOnly? date) => new(key, date);

    private static PortfolioInput WithImport(DateOnly? date, params ImportRowInput[] rows) =>
        new(null, null, "daily", "17:00", date, new FirstImportInput(rows));

    private async Task<BrokerAccount> AccountWithDhanHoldingsAsync(bool connect = true)
    {
        var account = await _fx.ConnectedAsync(connect: connect);
        _fx.Dhan.Rows.AddRange([Row("AAA", 10, 100m, 120m, "1", "INE000A00001"), Row("BBB", 20, 200m, 190m, "2", "INE000B00002")]);
        return account;
    }

    [Fact]
    public async Task A_connected_account_without_holdings_gets_the_chosen_holdings_imported_with_their_dates_and_the_first_import_date()
    {
        var account = await AccountWithDhanHoldingsAsync();

        var saved = Ok(await SaveAsync(account.Id, WithImport(Chosen, R("INE000A00001", new DateOnly(2023, 5, 5)), R("INE000B00002", null))));

        saved.Import.ShouldBe(new FirstImportView("imported", 2, 1, 0, 0, null, null, null));
        (saved.Account.Portfolio.RefreshMode, saved.Account.Portfolio.RefreshDailyTimeIst, saved.Account.Portfolio.FirstImportDate, saved.Account.Portfolio.HasHoldings)
            .ShouldBe(("daily", "17:00", Chosen, true));
        var one = _fx.HoldingOf("AAA").Lots.Single();
        var two = _fx.HoldingOf("BBB").Lots.Single();
        (one.AcquiredOn, one.UsesDefaultDate, one.Source).ShouldBe((new DateOnly(2023, 5, 5), false, LotSource.Import));
        (two.AcquiredOn, two.UsesDefaultDate).ShouldBe((Chosen, true));
        _fx.Data.Syncs.Single().Trigger.ShouldBe(SyncTrigger.Import);
    }

    [Fact]
    public async Task Rows_only_override_dates_every_holding_dhan_lists_is_imported()
    {
        var account = await AccountWithDhanHoldingsAsync();

        var saved = Ok(await SaveAsync(account.Id, WithImport(Chosen, R("INE000A00001", new DateOnly(2023, 5, 5)))));

        saved.Import.ShouldBe(new FirstImportView("imported", 2, 1, 0, 0, null, null, null));
        _fx.Data.Holdings.Select(h => h.TradingSymbol).Order().ShouldBe(["AAA", "BBB"]);
        _fx.HoldingOf("AAA").Lots.Single().AcquiredOn.ShouldBe(new DateOnly(2023, 5, 5));
        _fx.HoldingOf("BBB").Lots.Single().AcquiredOn.ShouldBe(Chosen);
    }

    [Fact]
    public async Task A_save_without_rows_imports_every_holding_with_the_request_date_flagged_default()
    {
        var account = await AccountWithDhanHoldingsAsync();

        var saved = Ok(await SaveAsync(account.Id, new PortfolioInput(null, null, FirstImportDate: Chosen)));

        saved.Import.ShouldBe(new FirstImportView("imported", 2, 2, 0, 0, null, null, null));
        _fx.Data.Holdings.SelectMany(h => h.Lots).ShouldAllBe(l => l.AcquiredOn == Chosen && l.UsesDefaultDate);
        _fx.Data.Syncs.Single().Trigger.ShouldBe(SyncTrigger.Import);
    }

    [Fact]
    public async Task A_save_without_rows_or_date_uses_the_stored_date_and_then_2025_01_01()
    {
        var stored = await AccountWithDhanHoldingsAsync(connect: false);
        Ok(await SaveAsync(stored.Id, new PortfolioInput(null, null, FirstImportDate: Chosen)));
        await _fx.Connections.ConnectByLoginAsync(stored);
        Ok(await SaveAsync(stored.Id, new PortfolioInput(null, false)));
        _fx.Data.Holdings.SelectMany(h => h.Lots).ShouldAllBe(l => l.AcquiredOn == Chosen);
    }

    [Fact]
    public async Task A_save_with_no_stored_date_falls_back_to_2025_01_01()
    {
        var account = await AccountWithDhanHoldingsAsync();

        Ok(await SaveAsync(account.Id, new PortfolioInput(null, null)));

        _fx.Data.Holdings.SelectMany(h => h.Lots).ShouldAllBe(l => l.AcquiredOn == new DateOnly(2025, 1, 1) && l.UsesDefaultDate);
    }

    [Fact]
    public async Task An_account_that_is_not_connected_stores_the_date_and_defers_the_import_to_the_first_sync()
    {
        var account = await AccountWithDhanHoldingsAsync(connect: false);

        var saved = Ok(await SaveAsync(account.Id, WithImport(Chosen, R("INE000A00001", null))));

        saved.Import!.Status.ShouldBe("deferred");
        saved.Import.Message.ShouldNotBeNull().ShouldContain("2024-04-01");
        saved.Account.Portfolio.FirstImportDate.ShouldBe(Chosen);
        _fx.Dhan.Tokens.ShouldBeEmpty();
        _fx.Data.Holdings.ShouldBeEmpty();
        _fx.Data.Syncs.ShouldBeEmpty();

        await _fx.Connections.ConnectByLoginAsync(account);
        Ok(await _fx.SyncAsync(account.Id));
        _fx.Data.Holdings.SelectMany(h => h.Lots).ShouldAllBe(l => l.AcquiredOn == Chosen && l.UsesDefaultDate);
        _fx.Data.Holdings.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_account_that_has_holdings_already_skips_the_import_and_changes_no_holding()
    {
        var account = await AccountWithDhanHoldingsAsync();
        await _fx.ImportAllAsync(account.Id, new DateOnly(2023, 1, 1));
        var runs = _fx.Data.Syncs.Count;

        var saved = Ok(await SaveAsync(account.Id, WithImport(Chosen, R("INE000A00001", new DateOnly(2020, 2, 2)))));

        saved.Import!.Status.ShouldBe("skipped");
        (saved.Import.Imported, saved.Import.DefaultDated).ShouldBe((0, 0));
        _fx.Data.Holdings.SelectMany(h => h.Lots).ShouldAllBe(l => l.AcquiredOn == new DateOnly(2023, 1, 1));
        _fx.Data.Syncs.Count.ShouldBe(runs);
        saved.Account.Portfolio.FirstImportDate.ShouldBe(Chosen); // stored, but without effect on existing holdings
    }

    [Fact]
    public async Task A_save_that_changes_nothing_on_an_account_with_holdings_skips_without_calling_dhan()
    {
        var account = await AccountWithDhanHoldingsAsync();
        Ok(await SaveAsync(account.Id, new PortfolioInput(null, null)));
        var runs = _fx.Data.Syncs.Count;
        var tokens = _fx.Dhan.Tokens.Count;

        var saved = Ok(await SaveAsync(account.Id, new PortfolioInput(null, null)));

        saved.Import!.Status.ShouldBe("skipped");
        _fx.Data.Syncs.Count.ShouldBe(runs);
        _fx.Dhan.Tokens.Count.ShouldBe(tokens);
    }

    [Fact]
    public async Task A_failing_import_still_saves_the_settings_and_reports_portfolio_import_failed_with_the_cause()
    {
        var account = await AccountWithDhanHoldingsAsync();
        _fx.Dhan.Failure = new BrokerFailure(BrokerFailureKind.Unavailable);

        var saved = Ok(await SaveAsync(account.Id, WithImport(Chosen, R("INE000A00001", null))));

        saved.Import!.Status.ShouldBe("failed");
        saved.Import.ErrorCode.ShouldBe("portfolio.import_failed");
        saved.Import.Cause.ShouldNotBeNullOrEmpty();
        saved.Import.Message.ShouldNotBeNullOrEmpty();
        (saved.Account.Portfolio.RefreshMode, saved.Account.Portfolio.FirstImportDate, saved.Account.Portfolio.HasHoldings).ShouldBe(("daily", Chosen, false));
        account.Portfolio.FirstImportDate.ShouldBe(Chosen);
        _fx.Data.Holdings.ShouldBeEmpty();
        _fx.Data.Syncs.Single().Status.ShouldBe(SyncStatus.Failed);

        _fx.Dhan.Failure = null;
        Ok(await SaveAsync(account.Id, WithImport(Chosen, R("INE000A00001", null)))).Import!.Status.ShouldBe("imported");
    }

    [Fact]
    public async Task The_failed_import_message_is_made_of_fixed_text_and_a_well_formed_code_never_of_dhans_answer()
    {
        var account = await AccountWithDhanHoldingsAsync();
        _fx.Dhan.Failure = new BrokerFailure(BrokerFailureKind.Rejected, "DH-905");

        var saved = Ok(await SaveAsync(account.Id, WithImport(Chosen, R("INE000A00001", null))));

        saved.Import!.Message.ShouldBe("The settings were saved but the holdings were not imported: Dhan refused the request (error DH-905). Save again or press Sync now to retry.");
    }

    [Fact]
    public async Task Changing_the_first_import_date_is_audited_with_the_old_and_the_new_date()
    {
        var account = await _fx.ConnectedAsync();

        Ok(await SaveAsync(account.Id, new PortfolioInput(null, null, FirstImportDate: Chosen)));

        _fx.Accounts.Events.Select(e => e.Detail).ShouldContain("First import date: 2025-01-01 -> 2024-04-01");
    }

    [Fact]
    public async Task A_sync_in_progress_fails_the_import_but_keeps_the_settings()
    {
        var account = await AccountWithDhanHoldingsAsync();
        _fx.Data.OtherRunInProgress = true;

        var saved = Ok(await SaveAsync(account.Id, WithImport(Chosen, R("INE000A00001", null))));

        (saved.Import!.Status, saved.Import.Cause).ShouldBe(("failed", PortfolioErrors.SyncRunningCode));
        saved.Account.Portfolio.FirstImportDate.ShouldBe(Chosen);
    }

    [Fact]
    public async Task A_role_that_is_off_defers_the_import()
    {
        var account = await _fx.Connections.Accounts.CreatedAsync(Inputs.Account());

        var saved = Ok(await SaveAsync(account.Id, WithImport(Chosen, R("INE000A00001", null))));

        saved.Import!.Status.ShouldBe("deferred");
        saved.Account.Portfolio.FirstImportDate.ShouldBe(Chosen);
    }

    [Fact]
    public async Task Bad_dates_and_rows_are_rejected_before_anything_is_saved()
    {
        var account = await AccountWithDhanHoldingsAsync();

        var result = await SaveAsync(account.Id, new PortfolioInput(
            null, null, "daily", "17:00", new DateOnly(2026, 10, 11),
            new FirstImportInput([new ImportRowInput(" ", null), new ImportRowInput("INE000A00001", new DateOnly(1989, 12, 31))])));

        result.Error.Code.ShouldBe(AccountErrors.InvalidCode);
        result.Error.Fields!.Select(f => f.Field).Order().ShouldBe(["firstImport.rows[0].key", "firstImport.rows[1].acquiredOn", "firstImportDate"]);
        account.Portfolio.Refresh.ShouldBe(PortfolioRefresh.Default);
        account.Portfolio.FirstImportDate.ShouldBeNull();
        _fx.Data.Syncs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2026-10-11")]
    [InlineData("1989-12-31")]
    public async Task The_first_import_date_must_not_be_in_the_future_or_before_1990(string date)
    {
        var account = await _fx.ConnectedAsync();

        var result = await SaveAsync(account.Id, new PortfolioInput(null, null, FirstImportDate: DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)));

        result.Error.Fields!.Select(f => f.Field).ShouldBe(["firstImportDate"]);
    }

    [Fact]
    public async Task Today_and_1990_01_01_are_accepted_as_first_import_dates()
    {
        var account = await _fx.ConnectedAsync();

        Ok(await SaveAsync(account.Id, new PortfolioInput(null, null, FirstImportDate: new DateOnly(1990, 1, 1)))).Account.Portfolio.FirstImportDate.ShouldBe(new DateOnly(1990, 1, 1));
        Ok(await SaveAsync(account.Id, new PortfolioInput(null, null, FirstImportDate: _fx.Today))).Account.Portfolio.FirstImportDate.ShouldBe(_fx.Today);
    }

    [Fact]
    public async Task The_first_import_date_is_validated_at_account_creation_too()
    {
        var result = await _fx.Connections.Accounts.Create().HandleAsync(
            new CreateAccountCommand(Inputs.Account(portfolio: new PortfolioInput(null, null, FirstImportDate: new DateOnly(2030, 1, 1))), Actor), CancellationToken.None);

        result.Error.Fields!.Select(f => f.Field).ShouldBe(["portfolio.firstImportDate"]);
    }

    [Fact]
    public async Task Rows_in_a_creation_request_are_ignored_because_a_new_account_has_no_connection()
    {
        var created = await _fx.Connections.Accounts.CreatedAsync(Inputs.Account(
            roles: Inputs.Roles(false, true),
            portfolio: new PortfolioInput(null, null, FirstImportDate: Chosen, FirstImport: new FirstImportInput([new ImportRowInput("INE000A00001", null)]))));

        created.Portfolio.FirstImportDate.ShouldBe(Chosen);
        _fx.Data.Holdings.ShouldBeEmpty();
    }
}
