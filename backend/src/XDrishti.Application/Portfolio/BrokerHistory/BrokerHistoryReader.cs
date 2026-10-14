using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// Reads one window of Dhan's trade history or ledger (ADR 0025). Trades come page by page (pages from 0) until the first empty page; a window that reaches <see cref="MaxPages"/> pages without one, or that Dhan refuses as too large
/// (<see cref="TooLargeCode"/>, the "input exception" code), is split in half once and each half read on its own; if a half is too large too the read fails and nothing of the window is used (no half answer is ever stored). A refused token, a rate
/// limit, a timeout or any other failure fails the read with the broker error and is never turned into "no data". The calls are made one after the other; the clients keep them apart in time.
/// <para>
/// <b>DH-907</b> ("incorrect parameters or no data present") is Dhan's answer to a request with nothing in it - and also to one it does not like, so it is read with care. After a page of rows it ends the paging (the history is over). On the first page of a
/// window longer than one date it is <i>unclear</i>: the window is split in half once, like a too-large one, and what the halves return is used (a half that answers DH-907 to its first page is empty). On the first page of a single date, or of a half, it
/// is taken as empty. Every (half-)window taken as empty only for this reason is counted in <see cref="BrokerSyncCounts.EmptyByNoData"/>, so the owner can see that an empty stretch rested on that code alone.
/// </para>
/// </summary>
internal sealed class BrokerHistoryReader(IBrokerTrades trades, IBrokerLedger ledger)
{
    /// <summary>The most pages one window of trades may have.</summary>
    public const int MaxPages = 200;

    /// <summary>
    /// Dhan's code for a request with parameters it does not accept (<c>DH-905</c>, "input exception"), which is how a date range that is too wide is expected to be refused. Not verified against the real API; the first real read of the owner's account
    /// settles it, and the window size is a constant (<see cref="BrokerWindows.MaxDays"/>).
    /// </summary>
    public const string TooLargeCode = "DH-905";

    public async ValueTask<Result<TradeWindowRead>> ReadTradesAsync(SecretValue token, DateWindow window, CancellationToken cancellationToken)
    {
        var whole = await PagesAsync(token, window, mayProbe: true, cancellationToken);
        if (whole.IsSuccess)
        {
            return Result.Success(whole.Value);
        }

        var halves = BrokerWindows.Halve(window);
        if (!NeedsHalving(whole.Error) || halves is null)
        {
            return Result.Failure<TradeWindowRead>(Final(whole.Error));
        }

        var (first, second) = halves.Value;

        var one = await PagesAsync(token, first, mayProbe: false, cancellationToken);
        if (one.IsFailure)
        {
            return Result.Failure<TradeWindowRead>(Final(one.Error));
        }

        var two = await PagesAsync(token, second, mayProbe: false, cancellationToken);
        if (two.IsFailure)
        {
            return Result.Failure<TradeWindowRead>(Final(two.Error));
        }

        return Result.Success(new TradeWindowRead(
            [.. one.Value.Trades, .. two.Value.Trades],
            one.Value.Rows + two.Value.Rows,
            one.Value.Dropped + two.Value.Dropped,
            Merge(one.Value.Skipped, two.Value.Skipped),
            Halved: true,
            one.Value.EmptyByNoData + two.Value.EmptyByNoData));
    }

    public async ValueTask<Result<LedgerWindowRead>> ReadLedgerAsync(SecretValue token, DateWindow window, CancellationToken cancellationToken)
    {
        var whole = await LedgerAsync(token, window, mayProbe: true, cancellationToken);
        if (whole.IsSuccess)
        {
            return Result.Success(whole.Value);
        }

        var halves = BrokerWindows.Halve(window);
        if (!NeedsHalving(whole.Error) || halves is null)
        {
            return Result.Failure<LedgerWindowRead>(Final(whole.Error));
        }

        var (first, second) = halves.Value;

        var one = await LedgerAsync(token, first, mayProbe: false, cancellationToken);
        if (one.IsFailure)
        {
            return Result.Failure<LedgerWindowRead>(Final(one.Error));
        }

        var two = await LedgerAsync(token, second, mayProbe: false, cancellationToken);
        if (two.IsFailure)
        {
            return Result.Failure<LedgerWindowRead>(Final(two.Error));
        }

        return Result.Success(new LedgerWindowRead(
            [.. one.Value.Rows, .. two.Value.Rows],
            one.Value.Received + two.Value.Received,
            one.Value.Dropped + two.Value.Dropped,
            Halved: true,
            one.Value.EmptyByNoData + two.Value.EmptyByNoData));
    }

    /// <summary>The marker for "this window is too wide"; replaced by a real error when it is not (or cannot be) halved.</summary>
    private static readonly Error TooLarge = new("portfolio.window_too_large", "The window is too large.");

    /// <summary>The marker for "Dhan answered DH-907 to the first page of a window of several dates": it may mean nothing there or a request it did not like, so the window is halved to find out. Never leaves the reader.</summary>
    private static readonly Error Unclear = new("portfolio.window_unclear", "The window is unclear.");

    private static bool NeedsHalving(Error error) => ReferenceEquals(error, TooLarge) || ReferenceEquals(error, Unclear);

    private static Error Final(Error error) => ReferenceEquals(error, TooLarge) ? PortfolioErrors.HistoryTooLarge : ReferenceEquals(error, Unclear) ? PortfolioErrors.SyncFailed : error;

    private async ValueTask<Result<TradeWindowRead>> PagesAsync(SecretValue token, DateWindow window, bool mayProbe, CancellationToken cancellationToken)
    {
        var all = new List<BrokerTrade>();
        var rows = 0;
        var dropped = 0;
        var skipped = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var page = 0; page < MaxPages; page++)
        {
            var response = await trades.FetchPageAsync(token, window.From, window.To, page, cancellationToken);
            if (!response.IsSuccess)
            {
                return Result.Failure<TradeWindowRead>(Failure(response.Failure!, window));
            }

            var read = response.Value;
            if (read.NoData)
            {
                if (page > 0)
                {
                    // After a page of rows, "no data" is the end of the history.
                    return Result.Success(new TradeWindowRead(all, rows, dropped, skipped, Halved: false));
                }

                return mayProbe && window.Days > 1
                    ? Result.Failure<TradeWindowRead>(Unclear)
                    : Result.Success(new TradeWindowRead([], 0, 0, skipped, Halved: false, EmptyByNoData: 1));
            }

            if (read.Rows == 0)
            {
                return Result.Success(new TradeWindowRead(all, rows, dropped, skipped, Halved: false));
            }

            all.AddRange(read.Trades);
            rows += read.Rows;
            dropped += read.Dropped;
            skipped = Merge(skipped, read.Skipped);
        }

        // 200 pages and still no empty one: the window has more than may be read in one go.
        return Result.Failure<TradeWindowRead>(TooLarge);
    }

    private async ValueTask<Result<LedgerWindowRead>> LedgerAsync(SecretValue token, DateWindow window, bool mayProbe, CancellationToken cancellationToken)
    {
        var response = await ledger.FetchAsync(token, window.From, window.To, cancellationToken);
        if (!response.IsSuccess)
        {
            return Result.Failure<LedgerWindowRead>(Failure(response.Failure!, window));
        }

        var read = response.Value;
        if (!read.NoData)
        {
            return Result.Success(new LedgerWindowRead(read.Rows, read.Received, read.Dropped, Halved: false));
        }

        return mayProbe && window.Days > 1
            ? Result.Failure<LedgerWindowRead>(Unclear)
            : Result.Success(new LedgerWindowRead([], 0, 0, Halved: false, EmptyByNoData: 1));
    }

    private static Error Failure(BrokerFailure failure, DateWindow window) => failure switch
    {
        { Kind: BrokerFailureKind.Unauthorized } => PortfolioErrors.TokenRefused,
        { Kind: BrokerFailureKind.Rejected, BrokerCode: TooLargeCode } when window.Days > 1 => TooLarge,
        _ => ConnectionErrors.Broker(failure),
    };

    private static Dictionary<string, int> Merge(IReadOnlyDictionary<string, int> left, IReadOnlyDictionary<string, int> right)
    {
        var merged = new Dictionary<string, int>(left, StringComparer.Ordinal);
        foreach (var (reason, count) in right)
        {
            merged[reason] = merged.GetValueOrDefault(reason) + count;
        }

        return merged;
    }
}
