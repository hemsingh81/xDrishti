using System.Net;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>
/// What changes the cached portfolio history through the accounts side (P2-07 review): re-dating the lots on the default date, switching the Portfolio role off and archiving an account all show in the next read at once, like the
/// holdings summary does. Clock: Monday 2026-10-12 10:00 India time.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PortfolioHistoryInvalidationTests(ApiFactory factory) : PortfolioHistoryTestBase(factory)
{
    /// <summary>One account whose ALPHA and BETA lots are on the default (first import) date 2026-10-05, with bars for both.</summary>
    private async Task<AccountDetail> DefaultDatedAccountAsync()
    {
        var alpha = await ShareAsync("ALPHA", "1001", "INE000A01011");
        var beta = await ShareAsync("BETA", "1002", "INE000B01012");
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.AddRange([Holding("ALPHA", 100, 50m, 100m, "1001", "INE000A01011"), Holding("BETA", 10, 200m, 500m, "1002", "INE000B01012")]);
        (await Client.ImportAsync(account.Id, "2026-10-05", new object[] { new { key = "INE000A01011" }, new { key = "INE000B01012" } }, Ct)).Imported.ShouldBe(2);
        await StoreDailyAsync(alpha, (Oct(5), 100), (Oct(6), 100), (Oct(7), 100), (Oct(8), 100), (Oct(9), 100));
        await StoreDailyAsync(beta, (Oct(5), 500), (Oct(6), 500), (Oct(7), 500), (Oct(8), 500), (Oct(9), 500));
        return account;
    }

    [Fact]
    public async Task Changing_the_first_import_date_changes_the_next_history_read_at_once()
    {
        var account = await DefaultDatedAccountAsync();
        var before = await HistoryAsync();
        (await HistoryAsync()).Points.Count.ShouldBe(before.Points.Count);

        (await Client.SavePortfolioAsync(account.Id, new { firstImportDate = "2026-10-07" }, Ct)).Import!.Redated.ShouldBe(2);
        var after = await HistoryAsync();

        before.FirstDate.ShouldBe(Oct(5));
        after.FirstDate.ShouldBe(Oct(7));
        after.Points.Select(p => p.Date.Day).ShouldBe([7, 8, 9, 12]);
        before.Points.Count.ShouldBe(6);
    }

    [Fact]
    public async Task Switching_the_portfolio_role_off_takes_the_account_out_of_the_all_accounts_history_at_once()
    {
        var account = await DefaultDatedAccountAsync();
        var before = await HistoryAsync();

        using var off = await Client.PutJsonAsync($"/api/accounts/{account.Id}/roles", new { portfolio = new { enabled = false } }, Ct);
        off.StatusCode.ShouldBe(HttpStatusCode.OK, await off.Content.ReadAsStringAsync(Ct));
        var after = await HistoryAsync();

        before.Coverage.Holdings.ShouldBe(2);
        after.Coverage.Holdings.ShouldBe(0);
        after.Points.ShouldBeEmpty();
    }

    [Fact]
    public async Task Archiving_an_account_takes_it_out_of_the_all_accounts_history_at_once()
    {
        var account = await DefaultDatedAccountAsync();
        var before = await HistoryAsync();

        using var archived = await Client.DeleteAsync($"/api/accounts/{account.Id}", Ct);
        archived.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = await HistoryAsync();

        before.Points.Count.ShouldBe(6);
        after.Points.ShouldBeEmpty();
    }
}
