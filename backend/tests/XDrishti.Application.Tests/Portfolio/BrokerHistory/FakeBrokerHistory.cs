using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>
/// A scripted Dhan trade history and ledger: answers the configured trades and ledger lines for the windows asked (a trade belongs to a window by its date), page by page, and the configured failures. Remembers every call (window, page) and token.
/// </summary>
internal sealed class FakeBrokerHistory : IBrokerTrades, IBrokerLedger
{
    /// <summary>The version of the Dhan mapping (a test raises it to simulate an update of the mappers).</summary>
    public int MapperVersion { get; set; } = 1;

    /// <summary>Every trade Dhan has (the fake filters them by the asked dates).</summary>
    public List<BrokerTrade> Trades { get; } = [];

    public List<BrokerLedgerRow> Ledger { get; } = [];

    /// <summary>Rows per page of the trade history (small, so that paging happens).</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Skipped and dropped counts reported with the first page of every window.</summary>
    public Dictionary<string, int> SkippedPerWindow { get; } = [];

    public int DroppedPerWindow { get; set; }

    /// <summary>Answers every trade whatever the asked dates (a Dhan that sends rows outside the window).</summary>
    public bool IgnoreDates { get; set; }

    public List<(DateOnly From, DateOnly To, int Page)> TradeCalls { get; } = [];

    public List<(DateOnly From, DateOnly To)> LedgerCalls { get; } = [];

    public List<SecretValue> Tokens { get; } = [];

    /// <summary>Decides, per call, whether Dhan fails (null answers normally).</summary>
    public Func<DateOnly, DateOnly, int, BrokerFailure?>? TradeFailure { get; set; }

    public Func<DateOnly, DateOnly, BrokerFailure?>? LedgerFailure { get; set; }

    /// <summary>
    /// Decides, per trade call, whether Dhan answers its "no data" code DH-907 (the client turns it into an empty page marked no-data). By default Dhan answers an empty page as an empty list; return true to make it answer DH-907 for that call.
    /// </summary>
    public Func<DateOnly, DateOnly, int, bool?>? TradeNoData { get; set; }

    /// <summary>Like <see cref="TradeNoData"/> for the ledger: by default Dhan answers an empty ledger as an empty list.</summary>
    public Func<DateOnly, DateOnly, bool?>? LedgerNoData { get; set; }

    /// <summary>Raised before every trade call, so that a test can let the world change meanwhile.</summary>
    public Func<Task>? BeforeAnswer { get; set; }

    public async ValueTask<BrokerResponse<BrokerTradePage>> FetchPageAsync(SecretValue accessToken, DateOnly from, DateOnly to, int page, CancellationToken cancellationToken)
    {
        Tokens.Add(accessToken);
        TradeCalls.Add((from, to, page));
        if (BeforeAnswer is { } wait)
        {
            await wait();
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (TradeFailure?.Invoke(from, to, page) is { } failure)
        {
            return BrokerResponse.Fail<BrokerTradePage>(failure);
        }

        var inWindow = Trades.Where(t => IgnoreDates || (t.Date >= from && t.Date <= to)).OrderBy(t => t.Date).ThenBy(t => t.TradeId, StringComparer.Ordinal).ToList();
        var rows = inWindow.Skip(page * PageSize).Take(PageSize).ToList();
        if (TradeNoData?.Invoke(from, to, page) ?? false)
        {
            return BrokerResponse.Ok(BrokerTradePage.NoDataAnswer);
        }

        var skipped = page == 0 ? new Dictionary<string, int>(SkippedPerWindow) : new Dictionary<string, int>();
        var dropped = page == 0 ? DroppedPerWindow : 0;
        return BrokerResponse.Ok(new BrokerTradePage(rows, rows.Count + (page == 0 ? dropped + skipped.Values.Sum() : 0), dropped, skipped));
    }

    public ValueTask<BrokerResponse<BrokerLedgerRead>> FetchAsync(SecretValue accessToken, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        Tokens.Add(accessToken);
        LedgerCalls.Add((from, to));
        if (LedgerFailure?.Invoke(from, to) is { } failure)
        {
            return ValueTask.FromResult(BrokerResponse.Fail<BrokerLedgerRead>(failure));
        }

        if (LedgerNoData?.Invoke(from, to) ?? false)
        {
            return ValueTask.FromResult(BrokerResponse.Ok(BrokerLedgerRead.NoDataAnswer));
        }

        var rows = Ledger.Where(l => l.Date >= from && l.Date <= to).ToList();
        return ValueTask.FromResult(BrokerResponse.Ok(new BrokerLedgerRead(rows, rows.Count, 0)));
    }
}
