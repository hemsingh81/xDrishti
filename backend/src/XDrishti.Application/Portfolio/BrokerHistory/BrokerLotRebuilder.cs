using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// The automatic rebuild of lots after a broker trade sync (ADR 0025, owner's decision: no preview). For every open holding that has broker trades (found from the distinct securities of those trades, with the account's holdings read once for the whole run) the rebuild is worked out by the same code as the owner's rebuild
/// (<see cref="TradeWorkspace.RebuildOfAsync(Guid, Holding, HoldingMatcher, DateOnly, CancellationToken)"/>: the pure <c>LotReconstructor</c> over the holding's stored trades) and applied only when it is safe:
/// <list type="bullet">
/// <item>the result must be <c>ok</c>; <c>unchanged</c> is counted, <c>quantity_mismatch</c>, <c>ambiguous</c> and <c>no_holding</c> are left alone and counted;</item>
/// <item>a holding with a <b>hand-dated</b> lot (the owner's own dates) is not rebuilt: it is reported <c>needs_review</c>, and the owner uses the preview-then-rebuild of the holding's Trades section;</item>
/// <item>each holding is rebuilt in its own unit of work that is <b>not retried</b> when the holding changed meanwhile (a sync or a quote saved its row): that rebuild is skipped (<c>changed_meanwhile</c>) and the next run tries again.</item>
/// </list>
/// Every rebuild is audited with counts only (<c>LotsRebuilt</c>, trigger <c>broker_sync</c>); the cached figures are dropped by the unit of work, as for the owner's rebuild. Nothing here logs a symbol, quantity or price.
/// </summary>
internal sealed class BrokerLotRebuilder(
    TradeWorkspace workspace,
    IHoldingStore holdings,
    ITradeStore trades,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<BrokerLotRebuilder> logger)
{
    /// <summary>The audit trigger of an automatic rebuild.</summary>
    public const string Trigger = "broker_sync";

    public const string NeedsReview = "needs_review";

    public const string ChangedMeanwhile = "changed_meanwhile";

    public async ValueTask<BrokerRebuildSummary> RebuildAsync(BrokerAccount account, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        // The holdings are read once for the whole run (the matcher over them decides which securities have exactly one holding), and the securities of the broker trades are read as a distinct list - not every trade of the account. Each candidate
        // then reads only its own holding, tracked, inside its own unit of work.
        var all = await holdings.ListForAccountAsync(account.Id, cancellationToken);
        var securities = await trades.ListBrokerSecuritiesAsync(account.Id, cancellationToken);
        var matcher = new HoldingMatcher(all);
        var matched = new Dictionary<Guid, string>();
        var left = new List<BrokerNotRebuilt>();
        var listed = new HashSet<(string Key, MatchKind Kind)>();
        foreach (var security in securities)
        {
            var match = matcher.Find(security.Isin, security.TradingSymbol);
            if (match is { Kind: MatchKind.One, Holding: { } holding })
            {
                matched[holding.Id] = holding.TradingSymbol;
            }
            else if (listed.Add((security.Isin ?? security.TradingSymbol, match.Kind)))
            {
                // A trade stored with its ISIN and no ticker (the share is not in the master list) is named by its ISIN.
                left.Add(new BrokerNotRebuilt(security.TradingSymbol.Length > 0 ? security.TradingSymbol : security.Isin ?? string.Empty, match.Kind == MatchKind.Ambiguous ? RebuildStatuses.Ambiguous : RebuildStatuses.NoHolding));
            }
        }

        var candidates = matched.OrderBy(m => m.Value, StringComparer.Ordinal).ThenBy(m => m.Key).Select(m => (Id: m.Key, Symbol: m.Value)).ToList();
        var rebuilt = 0;
        var unchanged = 0;
        var review = 0;
        foreach (var (id, symbol) in candidates)
        {
            var outcome = await RebuildOneAsync(account, id, matcher, actor, cancellationToken);
            switch (outcome)
            {
                case RebuildStatuses.Ok:
                    rebuilt++;
                    break;
                case RebuildStatuses.Unchanged:
                    unchanged++;
                    break;
                default:
                    review += outcome == NeedsReview ? 1 : 0;
                    left.Add(new BrokerNotRebuilt(symbol, outcome));
                    break;
            }
        }

        // The ones that need the owner first, then the other reasons; no_holding is the long tail (every security ever traded and sold).
        var ordered = left.OrderBy(n => n.Reason == NeedsReview ? 0 : n.Reason == RebuildStatuses.NoHolding ? 2 : 1).ThenBy(n => n.Symbol, StringComparer.Ordinal).ToList();
        BrokerHistoryLog.Rebuilt(logger, account.Slug, rebuilt, unchanged, review, ordered.Count);
        return new BrokerRebuildSummary(rebuilt, unchanged, review, ordered.Count, [.. ordered.Take(BrokerSyncCounts.MaxNotRebuiltListed)]);
    }

    /// <summary>One holding in its own unit of work: the status it ended in (<c>ok</c> when rebuilt, <c>unchanged</c>, <c>needs_review</c>, <c>changed_meanwhile</c>, or the reason it cannot be rebuilt).</summary>
    private async ValueTask<string> RebuildOneAsync(BrokerAccount account, Guid holdingId, HoldingMatcher matcher, string actor, CancellationToken cancellationToken)
    {
        var outcome = RebuildStatuses.Unchanged;
        var result = await unitOfWork.RunOnceAsync(
            async token =>
            {
                var holding = await holdings.FindAsync(holdingId, token);
                if (holding is null)
                {
                    outcome = RebuildStatuses.NoHolding;
                    return Result.Success();
                }

                var rebuild = await workspace.RebuildOfAsync(account.Id, holding, matcher, account.Portfolio.ImportDate, token);
                if (!rebuild.WillReplace)
                {
                    outcome = rebuild.Status;
                    return Result.Success();
                }

                if (rebuild.HandDatedLotsReplaced > 0)
                {
                    outcome = NeedsReview;
                    return Result.Success();
                }

                var now = clock.GetUtcNow();
                var applied = workspace.Apply([rebuild], now);
                workspace.RecordRebuilt(account.Id, applied, actor, now, Trigger);
                await unitOfWork.SaveAsync(token);
                outcome = RebuildStatuses.Ok;
                return Result.Success();
            },
            cancellationToken);
        if (result.IsFailure)
        {
            BrokerHistoryLog.RebuildRaced(logger, holdingId);
            return ChangedMeanwhile;
        }

        return outcome;
    }
}
