using System.Globalization;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Trades;
using XDrishti.Application.Portfolio.BrokerHistory;

namespace XDrishti.Api.IntegrationTests.BrokerHistory;

/// <summary>
/// Shared set-up of the broker history tests (P2-13, ADR 0025): one connected account with EXMPL (30 shares at 100, mapped to the master, first import date Fri 9 Oct 2026) and a fake Dhan that reports the base tradebook (buy 10 at 90 on
/// Mon 5 Oct, buy 5 at 110 on Tue 6 Oct, sell 3 at 130 on Thu 8 Oct) and a few ledger lines. Clock: Monday 2026-10-12 10:00 India time. "Sync now" is read from Mon 5 Oct unless the test says otherwise.
/// </summary>
public abstract class BrokerHistoryTestBase(ApiFactory factory) : TradesTestBase(factory)
{
    protected const string Oct5 = "2026-10-05";

    protected static readonly TimeOnly Morning = new(10, 15, 2);

    protected static DateOnly D(int day) => new(2026, 10, day);

    /// <summary>The master list names NSE shares <c>NSE:SYMBOL</c> (the trade reads look the ticker up that way); the shared seed of the portfolio tests uses the bare symbol.</summary>
    protected async Task<Held> HeldOnTheNseAsync(long qty = 30, decimal avgCost = 100m)
    {
        var held = await HeldAsync(qty, avgCost);
        await ExecuteAsync("UPDATE market.instruments SET canonical_symbol = 'NSE:EXMPL' WHERE id = @id", ("id", held.Instrument));
        return held;
    }

    protected void DhanHasTheBaseTrades()
    {
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T1", "EXMPL", "BUY", 10, 90m, D(5), Morning));
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T2", "EXMPL", "BUY", 5, 110m, D(6), Morning));
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T3", "EXMPL", "SELL", 3, 130m, D(8), Morning));
    }

    protected void DhanHasALedger()
    {
        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(D(5), "V1", "RECEIPT", "FUNDS DEPOSIT THROUGH UPI", 0m, 25000m, 25000m));
        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(D(5), "V2", "BILL", "BILL FOR 05-OCT-2026 NSE-CAPITAL EQUITY PURCHASE", 900m, 0m, 24100m));
        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(D(7), "V3", "RECEIPT", "DIVIDEND CREDIT EXMPL FINAL DIV", 0m, 1500m, 25600m));
        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(D(8), "V4", "JOURNAL", "DP CHARGES 08-10-2026 EXMPL", 15.93m, 0m, 25584.07m));
        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(D(9), "V5", "PAYBLE", "FUNDS WITHDRAWAL", 10000m, 0m, 15584.07m));
    }

    protected List<FakeBrokers.RecordedRequest> TradeRequests => [.. Dhan.Requests.Where(r => r.Uri.AbsolutePath.StartsWith("/v2/trades/", StringComparison.Ordinal))];

    protected List<FakeBrokers.RecordedRequest> LedgerRequests => [.. Dhan.Requests.Where(r => r.Uri.AbsolutePath == "/v2/ledger")];

    /// <summary>Starts "Sync now" and waits for the run to end; the status at the end.</summary>
    protected async Task<(BrokerHistoryRunView Started, BrokerHistoryStatusView Status)> SyncAndWaitAsync(Guid accountId, string? from = Oct5)
    {
        var started = await Client.SyncNowAsync(accountId, from, Ct);
        return (started, await Client.WaitForTheRunAsync(accountId, started, Ct));
    }

    protected async Task<long> EventsAsync(string type) => await CountAsync(string.Create(CultureInfo.InvariantCulture, $"SELECT count(*) FROM accounts.account_events WHERE event = '{type}'"));
}
