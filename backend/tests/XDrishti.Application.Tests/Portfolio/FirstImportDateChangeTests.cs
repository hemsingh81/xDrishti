using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>Saving a changed first import date re-dates the lots still on the default date (ADR 0015). Clock: Saturday 2026-10-10, India.</summary>
public sealed class FirstImportDateChangeTests
{
    private readonly PortfolioFixture _fx = new();

    private static readonly DateOnly Original = new(2025, 1, 1);

    private static readonly DateOnly Changed = new(2024, 4, 1);

    private async Task<Result<PortfolioSaved>> SaveAsync(Guid accountId, DateOnly? date) =>
        await _fx.Connections.Accounts.UpdatePortfolio().HandleAsync(new UpdatePortfolioCommand(accountId, new PortfolioInput(null, null, FirstImportDate: date), Actor), CancellationToken.None);

    /// <summary>A connected account whose two holdings were imported on <see cref="Original"/>; "BBB" has a date of its own.</summary>
    private async Task<BrokerAccount> ImportedAsync(bool connect = true)
    {
        var account = await _fx.ConnectedAsync(connect: connect);
        if (connect)
        {
            _fx.Dhan.Rows.AddRange([Row("AAA", 10, 100m, 120m, "1", "INE000A00001"), Row("BBB", 20, 200m, 190m, "2", "INE000B00002")]);
            Ok(await _fx.ImportAsync(account.Id, new ImportInput(Original, [new ImportRowInput("INE000B00002", new DateOnly(2023, 5, 5))], All: true)));
        }

        return account;
    }

    [Fact]
    public async Task A_changed_date_re_dates_the_default_dated_lots_keeps_them_flagged_and_leaves_owner_dated_lots_alone()
    {
        var account = await ImportedAsync();
        var runs = _fx.Data.Syncs.Count;
        var tokens = _fx.Dhan.Tokens.Count;

        var saved = Ok(await SaveAsync(account.Id, Changed));

        (saved.Import.Status, saved.Import.Imported, saved.Import.Redated, saved.Import.RedateSkipped).ShouldBe(("skipped", 0, 1, 0));
        saved.Account.Portfolio.FirstImportDate.ShouldBe(Changed);
        var redated = _fx.HoldingOf("AAA").Lots.Single();
        (redated.AcquiredOn, redated.UsesDefaultDate).ShouldBe((Changed, true));
        var owners = _fx.HoldingOf("BBB").Lots.Single();
        (owners.AcquiredOn, owners.UsesDefaultDate).ShouldBe((new DateOnly(2023, 5, 5), false));
        _fx.Data.Syncs.Count.ShouldBe(runs);
        _fx.Dhan.Tokens.Count.ShouldBe(tokens); // no Dhan call
    }

    [Fact]
    public async Task The_change_is_audited_once_with_the_dates_and_counts_and_nothing_per_lot()
    {
        var account = await ImportedAsync();

        Ok(await SaveAsync(account.Id, Changed));

        var audit = _fx.Events(AccountEventType.FirstImportDateChanged).ShouldHaveSingleItem();
        audit.Detail.ShouldBe("First import date changed from 2025-01-01 to 2024-04-01; 1 lot re-dated, 0 skipped");
        audit.AccountId.ShouldBe(account.Id);
        audit.Actor.ShouldBe(Actor);
        _fx.Events(AccountEventType.PortfolioRefreshSettingsChanged).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_same_date_again_changes_nothing_and_records_no_audit()
    {
        var account = await ImportedAsync();
        Ok(await SaveAsync(account.Id, Changed));
        var audits = _fx.Events(AccountEventType.FirstImportDateChanged).Count;

        var again = Ok(await SaveAsync(account.Id, Changed));

        (again.Import.Status, again.Import.Redated, again.Import.RedateSkipped).ShouldBe(("skipped", 0, 0));
        _fx.Events(AccountEventType.FirstImportDateChanged).Count.ShouldBe(audits);
        _fx.HoldingOf("AAA").Lots.Single().AcquiredOn.ShouldBe(Changed);
    }

    [Fact]
    public async Task A_save_without_a_date_or_with_the_stored_default_changes_nothing()
    {
        var account = await ImportedAsync();

        var without = Ok(await SaveAsync(account.Id, null));
        var sameAsDefault = Ok(await SaveAsync(account.Id, Original)); // 2025-01-01 is what applies while none is stored

        (without.Import.Redated, sameAsDefault.Import.Redated).ShouldBe((0, 0));
        _fx.Events(AccountEventType.FirstImportDateChanged).ShouldBeEmpty();
        _fx.HoldingOf("AAA").Lots.Single().AcquiredOn.ShouldBe(Original);
    }

    [Fact]
    public async Task An_account_without_holdings_only_stores_the_date()
    {
        var account = await _fx.ConnectedAsync(connect: false);

        var saved = Ok(await SaveAsync(account.Id, Changed));

        saved.Import.Status.ShouldBe("deferred");
        (saved.Import.Redated, saved.Import.RedateSkipped).ShouldBe((0, 0));
        saved.Account.Portfolio.FirstImportDate.ShouldBe(Changed);
        _fx.Events(AccountEventType.FirstImportDateChanged).ShouldHaveSingleItem().Detail.ShouldBe("First import date changed from 2025-01-01 to 2024-04-01");
    }

    [Fact]
    public async Task Closed_lots_that_cannot_take_the_new_date_are_skipped_and_counted()
    {
        var account = await ImportedAsync();
        _fx.HoldingOf("AAA").CloseMissing(new DateOnly(2026, 6, 1), _fx.Clock.GetUtcNow()); // closed on 2026-06-01
        var later = new DateOnly(2026, 6, 2);

        var saved = Ok(await SaveAsync(account.Id, later));

        (saved.Import.Redated, saved.Import.RedateSkipped).ShouldBe((0, 1));
        var lot = _fx.HoldingOf("AAA").Lots.Single();
        (lot.AcquiredOn, lot.UsesDefaultDate).ShouldBe((Original, true));
        _fx.Events(AccountEventType.FirstImportDateChanged).Single().Detail.ShouldBe("First import date changed from 2025-01-01 to 2026-06-02; 0 lots re-dated, 1 skipped");
    }

    [Fact]
    public async Task Lots_found_by_a_refresh_and_manual_lots_are_not_touched()
    {
        var account = await ImportedAsync();
        var holding = _fx.HoldingOf("AAA");
        holding.Reconcile(Row("AAA", 15, 110m, 125m, "1", "INE000A00001"), null, null, _fx.Today, _fx.Clock.GetUtcNow());

        var saved = Ok(await SaveAsync(account.Id, Changed));

        saved.Import.Redated.ShouldBe(1);
        holding.Lots.Single(l => l.Source == LotSource.Added).AcquiredOn.ShouldBe(_fx.Today);
        holding.Lots.Single(l => l.Source == LotSource.Import).AcquiredOn.ShouldBe(Changed);
    }

    [Fact]
    public async Task An_account_that_is_not_connected_still_re_dates_without_calling_dhan()
    {
        var account = await ImportedAsync(connect: false);
        _fx.Data.Add(Holding.Open(account.Id, account.Portfolio.DefaultBucketId ?? Guid.NewGuid(), null, null, Row("AAA", 10, 100m, 120m, "1", "INE000A00001"), Original, usesDefaultDate: true, LotSource.Import, _fx.Clock.GetUtcNow()));

        var saved = Ok(await SaveAsync(account.Id, Changed));

        (saved.Import.Status, saved.Import.Redated).ShouldBe(("skipped", 1));
        _fx.HoldingOf("AAA").Lots.Single().AcquiredOn.ShouldBe(Changed);
        _fx.Dhan.Tokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_collision_with_a_concurrent_change_is_reported_as_a_conflict()
    {
        var account = await ImportedAsync();
        _fx.Accounts.CollidingSaves = 1;

        var result = await SaveAsync(account.Id, Changed);

        result.Error.ShouldBe(AccountErrors.ConcurrentChange);
    }
}
