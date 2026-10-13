using XDrishti.Api.IntegrationTests.Holdings;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio;

namespace XDrishti.Api.IntegrationTests.Trades;

/// <summary>
/// Shared set-up of the trade tests (P2-03, ADR 0023): one account with EXMPL (30 shares at 100, mapped to the master) imported with the default date Fri 9 Oct 2026, day bars for Mon 5 to Fri 9 October, and the
/// calls of the trade API. Clock: Monday 2026-10-12 10:00 India time.
/// </summary>
public abstract class TradesTestBase(ApiFactory factory) : PortfolioHistoryTestBase(factory)
{
    protected sealed record Held(AccountDetail Account, Guid Instrument, Guid HoldingId);

    protected async Task<Held> HeldAsync(long qty = 30, decimal avgCost = 100m)
    {
        var instrument = await ShareAsync("EXMPL", "1001", TradesApi.Isin);
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.Add(Holding("EXMPL", qty, avgCost, 120m, "1001", TradesApi.Isin));
        (await Client.ImportAsync(account.Id, "2026-10-09", new object[] { new { key = TradesApi.Isin } }, Ct)).Imported.ShouldBe(1);
        await StoreDailyAsync(instrument, (Oct(5), 100), (Oct(6), 102), (Oct(7), 104), (Oct(8), 106), (Oct(9), 108));
        var holding = (await Client.ListHoldingsAsync("?q=EXMPL", Ct)).Items.Single();
        return new Held(account, instrument, holding.Id);
    }

    protected async Task<HoldingRow> RowAsync() => (await Client.ListHoldingsAsync("?q=EXMPL&status=all", Ct)).Items.Single();

    protected async Task<HoldingLots> LotsOfAsync(Guid holdingId) => await Client.LotsAsync(holdingId, Ct);

    protected static string Rows(params string[] rows) => TradesApi.Csv(rows);

    protected static string Base => TradesApi.Csv(TradesApi.BaseRows);

    protected async Task<long> TransactionsAsync(string where = "true") => await CountAsync($"SELECT count(*) FROM portfolio.transactions WHERE {where}");
}
