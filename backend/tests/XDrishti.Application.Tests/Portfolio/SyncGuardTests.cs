using XDrishti.Application.Portfolio;
using XDrishti.Application.Tests.Portfolio.BrokerHistory;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>
/// What a write waits for (ADR 0030): trade import, bill import, Undo and Rebuild wait for holdings and trades runs; a DP import for holdings and ledger runs; a demat import for none. A run of another account never matters.
/// </summary>
public sealed class SyncGuardTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly Guid Account = Guid.Parse("00000000-0000-7000-8000-000000000a01");

    private static readonly Guid Other = Guid.Parse("00000000-0000-7000-8000-000000000a02");

    private readonly InMemoryPortfolioData _holdings;

    private readonly InMemoryBrokerData _runs;

    public SyncGuardTests()
    {
        _holdings = new InMemoryPortfolioData(new Accounts.InMemoryAccountData());
        _runs = new InMemoryBrokerData(_holdings);
    }

    private SyncGuard Guard() => new(_holdings, _runs);

    private void Running(Guid account, BrokerSyncKind kind)
    {
        var today = new DateOnly(2026, 10, 10);
        _runs.Runs.Add(BrokerSyncRun.Start(account, kind, BrokerSyncTrigger.Scheduled, today.AddDays(-30), today, "system:broker-history", DateTimeOffset.Parse("2026-10-10T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private void HoldingsSyncRunning(Guid account) =>
        _holdings.Syncs.Add(HoldingSync.Start(account, SyncTrigger.Manual, "owner", DateTimeOffset.Parse("2026-10-10T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture)));

    private static SyncWaits WaitsOf(string operation) => operation switch
    {
        "trade_import" or "bill_import" or "undo" or "rebuild" => SyncWaits.TradeWrite,
        "dp_import" => SyncWaits.CostWrite,
        _ => SyncWaits.None, // demat_import
    };

    [Theory]
    [InlineData("trade_import", true, false, true)]
    [InlineData("bill_import", true, false, true)]
    [InlineData("undo", true, false, true)]
    [InlineData("rebuild", true, false, true)]
    [InlineData("dp_import", false, true, true)]
    [InlineData("demat_import", false, false, false)]
    public async Task A_write_waits_only_for_the_runs_that_change_what_it_reads_and_writes(string operation, bool tradesBlock, bool ledgerBlocks, bool holdingsBlock)
    {
        var guard = Guard();
        var waits = WaitsOf(operation);
        var idle = await guard.RunningAsync(Account, waits, Ct);
        Running(Account, BrokerSyncKind.Trades);
        var withTrades = await guard.RunningAsync(Account, waits, Ct);
        _runs.Runs.Clear();
        Running(Account, BrokerSyncKind.Ledger);
        var withLedger = await guard.RunningAsync(Account, waits, Ct);
        _runs.Runs.Clear();
        HoldingsSyncRunning(Account);
        var withHoldings = await guard.RunningAsync(Account, waits, Ct);

        (idle, withTrades, withLedger, withHoldings).ShouldBe((false, tradesBlock, ledgerBlocks, holdingsBlock));
    }

    [Theory]
    [InlineData("trade_import", "trades")]
    [InlineData("trade_import", "ledger")]
    [InlineData("dp_import", "trades")]
    [InlineData("dp_import", "ledger")]
    public async Task A_run_of_another_account_never_blocks(string operation, string kind)
    {
        Running(Other, kind == "trades" ? BrokerSyncKind.Trades : BrokerSyncKind.Ledger);
        HoldingsSyncRunning(Other);

        (await Guard().RunningAsync(Account, WaitsOf(operation), Ct)).ShouldBeFalse();
    }

    [Fact]
    public void The_refusal_says_that_it_can_take_a_few_minutes()
    {
        PortfolioErrors.SyncRunningWait.Message.ShouldBe("A sync of this account is running; this can take a few minutes — try again then.");
        PortfolioErrors.SyncRunningWait.Code.ShouldBe(PortfolioErrors.SyncRunningCode);
    }
}
