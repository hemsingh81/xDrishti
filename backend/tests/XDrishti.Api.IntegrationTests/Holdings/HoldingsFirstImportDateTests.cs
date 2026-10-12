using System.Net;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>Saving a changed first import date re-dates the lots still on the default date (ADR 0015), against the real database (clock: Monday 2026-10-12 10:00 India time).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldingsFirstImportDateTests(ApiFactory factory) : HoldingsTestBase(factory)
{
    private static readonly DateOnly Original = new(2025, 1, 1);

    private static readonly DateOnly Changed = new(2024, 4, 1);

    /// <summary>Imports ALPHA, BETA and GAMMA on 2025-01-01; BETA has a date of its own (2023-05-05).</summary>
    private async Task<AccountDetail> ImportedAccountAsync()
    {
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.AddRange(
        [
            Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"),
            Holding("BETA", 10, 200m, 180m, "1002", "INE000B01012"),
            Holding("GAMMA", 5, 1000m, 1500m, "1003", "INE000G01013"),
        ]);
        (await Client.ImportAsync(account.Id, "2025-01-01", new[] { new { key = "INE000B01012", acquiredOn = "2023-05-05" } }, Ct)).Imported.ShouldBe(3);
        return account;
    }

    private Task<PortfolioSaved> SaveDateAsync(Guid accountId, string date) => Client.SavePortfolioAsync(accountId, new { firstImportDate = date }, Ct);

    private async Task<List<string>> AuditAsync(Guid accountId) =>
        [.. (await (await Client.GetAsync($"/api/accounts/{accountId}/activity", Ct)).ReadAsync<List<AccountEventView>>(Ct))
            .Where(e => e.Event == "FirstImportDateChanged").Select(e => e.Detail ?? string.Empty)];

    [Fact]
    public async Task Saving_a_changed_date_re_dates_only_default_dated_import_lots_and_reports_the_counts()
    {
        var account = await ImportedAccountAsync();
        Dhan.Holdings[1] = Holding("BETA", 12, 210m, 180m, "1002", "INE000B01012"); // two more shares: a refresh lot dated today
        await Client.SyncAsync(account.Id, Ct);
        var requests = HoldingRequests.Count;

        var saved = await SaveDateAsync(account.Id, "2024-04-01");

        var import = saved.Import!;
        (import.Status, import.Imported, import.Redated, import.RedateSkipped).ShouldBe(("skipped", 0, 2, 0));
        saved.Account.Portfolio.FirstImportDate.ShouldBe(Changed);
        foreach (var symbol in new[] { "ALPHA", "GAMMA" })
        {
            var lot = (await Factory.HoldingAsync(symbol, Ct)).Lots.Single();
            (lot.AcquiredOn, lot.UsesDefaultDate, lot.Source).ShouldBe((Changed, true, LotSource.Import));
        }

        var beta = (await Factory.HoldingAsync("BETA", Ct)).Lots;
        beta.Single(l => l.Source == LotSource.Import).AcquiredOn.ShouldBe(new DateOnly(2023, 5, 5));
        beta.Single(l => l.Source == LotSource.Added).AcquiredOn.ShouldBe(new DateOnly(2026, 10, 12));
        HoldingRequests.Count.ShouldBe(requests); // no Dhan call
    }

    [Fact]
    public async Task A_lot_the_owner_dated_keeps_its_date_even_when_it_was_default_dated_before()
    {
        var account = await ImportedAccountAsync();
        var alpha = (await Client.ListHoldingsAsync("?q=ALPHA", Ct)).Items.Single();
        var lot = (await Client.LotsAsync(alpha.Id, Ct)).Lots.Single();
        using var edited = await Client.PutJsonAsync($"/api/holdings/{alpha.Id}/lots/{lot.Id}", new { acquiredOn = "2022-02-02" }, Ct);
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);

        var saved = await SaveDateAsync(account.Id, "2024-04-01");

        saved.Import!.Redated.ShouldBe(1); // GAMMA only
        (await Factory.HoldingAsync("ALPHA", Ct)).Lots.Single().AcquiredOn.ShouldBe(new DateOnly(2022, 2, 2));
    }

    [Fact]
    public async Task The_dates_returns_and_xirr_follow_and_the_change_is_audited_once()
    {
        var account = await ImportedAccountAsync();
        var before = (await Client.ListHoldingsAsync("?q=ALPHA", Ct)).Items.Single();
        before.EarliestAcquiredOn.ShouldBe(Original);

        await SaveDateAsync(account.Id, "2024-04-01");

        var after = (await Client.ListHoldingsAsync("?q=ALPHA", Ct)).Items.Single();
        (after.EarliestAcquiredOn, after.UsesDefaultDate).ShouldBe((Changed, true));
        (after.Returns.Invested, after.Returns.Pnl).ShouldBe((before.Returns.Invested, before.Returns.Pnl));
        after.Returns.Xirr.ShouldNotBeNull().ShouldBeLessThan(before.Returns.Xirr!.Value); // the same gain over a longer time
        (await Client.LotsAsync(after.Id, Ct)).Lots.Single().AcquiredOn.ShouldBe(Changed);
        (await Client.SummaryAsync("", Ct)).DefaultDateLots.ShouldBe(2);
        var audit = await AuditAsync(account.Id);
        audit.ShouldBe(["First import date changed from 2025-01-01 to 2024-04-01; 2 lots re-dated, 0 skipped"]);
        foreach (var line in audit)
        {
            AssertNoSecret(line);
        }
    }

    [Fact]
    public async Task Saving_the_same_date_again_changes_nothing_and_adds_no_audit()
    {
        var account = await ImportedAccountAsync();
        await SaveDateAsync(account.Id, "2024-04-01");

        var again = await SaveDateAsync(account.Id, "2024-04-01");
        var other = await Client.SavePortfolioAsync(account.Id, new { includeInTotals = true }, Ct);

        (again.Import!.Status, again.Import.Redated, again.Import.RedateSkipped).ShouldBe(("skipped", 0, 0));
        (other.Import!.Redated, other.Import.RedateSkipped).ShouldBe((0, 0));
        (await AuditAsync(account.Id)).Count.ShouldBe(1);
        (await Factory.HoldingAsync("ALPHA", Ct)).Lots.Single().AcquiredOn.ShouldBe(Changed);
    }

    [Fact]
    public async Task A_closed_lot_that_cannot_take_the_new_date_is_skipped_and_counted()
    {
        var account = await ImportedAccountAsync();
        Factory.Clock.Pin(MondayMorning.AddDays(-7)); // Monday 2026-10-05: part of GAMMA is sold on this day
        Dhan.Holdings[2] = Holding("GAMMA", 3, 1000m, 1500m, "1003", "INE000G01013"); // 2 of 5 sold
        await Client.SyncAsync(account.Id, Ct);
        Factory.Clock.Pin(MondayMorning);

        var saved = await SaveDateAsync(account.Id, "2026-10-08");

        var import = saved.Import!;
        (import.Redated, import.RedateSkipped).ShouldBe((2, 1)); // ALPHA and the open rest of GAMMA follow; the piece sold on 2026-10-05 cannot; BETA's own-dated lot is not involved
        var lots = (await Factory.HoldingAsync("GAMMA", Ct)).Lots;
        lots.Single(l => l.ClosedOn is not null).AcquiredOn.ShouldBe(Original);
        lots.Single(l => l.ClosedOn is null).AcquiredOn.ShouldBe(new DateOnly(2026, 10, 8));
        lots.ShouldAllBe(l => l.UsesDefaultDate);
        (await AuditAsync(account.Id)).ShouldBe(["First import date changed from 2025-01-01 to 2026-10-08; 2 lots re-dated, 1 skipped"]);
    }

    [Fact]
    public async Task An_account_without_holdings_only_stores_the_date()
    {
        var account = await ConnectedAccountAsync();

        var saved = await Client.SavePortfolioAsync(account.Id, new { firstImportDate = "2024-04-01" }, Ct);

        (saved.Import!.Redated, saved.Import.RedateSkipped).ShouldBe((0, 0));
        saved.Account.Portfolio.FirstImportDate.ShouldBe(Changed);
        (await AuditAsync(account.Id)).ShouldBe(["First import date changed from 2025-01-01 to 2024-04-01"]);
    }

    [Fact]
    public async Task A_date_in_the_future_or_before_1990_is_rejected_and_nothing_is_re_dated()
    {
        var account = await ImportedAccountAsync();

        using var future = await Client.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", new { firstImportDate = "2026-10-13" }, Ct);
        using var ancient = await Client.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", new { firstImportDate = "1989-12-31" }, Ct);

        future.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await future.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["firstImportDate"]);
        ancient.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Factory.HoldingAsync("ALPHA", Ct)).Lots.Single().AcquiredOn.ShouldBe(Original);
        (await AuditAsync(account.Id)).ShouldBeEmpty();
    }
}
