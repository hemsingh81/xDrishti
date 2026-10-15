using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Application.Portfolio.Heal;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Attention;

/// <summary>
/// Works out the items that need the owner's attention from the facts (P2-16, ADR 0027). Pure: the same facts and settings always give the same items, so each rule is tested on its own. The id of an item includes its situation, so a changed situation is a new item
/// (and the old one resolves). Nothing here is a token, a narration, an amount or a quantity: names of accounts, tickers of holdings (at most three per item), counts and dates.
/// <list type="bullet">
/// <item><b>Reconnect Dhan</b> - an account whose login is missing, expired or about to expire (within two hours).</item>
/// <item><b>A sync keeps failing</b> - holdings, trades, ledger of an account, or the quote poll, failing on <see cref="IHealSettings.PersistDays"/> days in a row, or at once when only the owner can fix it; never while a quiet retry is still to come.</item>
/// <item><b>Holdings need review</b> - the latest trade read left holdings with hand-dated lots alone (still open and still hand-dated).</item>
/// <item><b>Trades do not explain a holding</b> - a quantity mismatch that earlier history could not fix (the deepening is over) or never could.</item>
/// <item><b>Possible dividends in the ledger</b> - ledger credits that look like dividends and wait for a decision.</item>
/// <item><b>Unmatched securities</b> - holdings or traded securities with no instrument-master match.</item>
/// <item><b>Rows dropped</b> - the latest succeeded trades or ledger read dropped rows (ADR 0030): info when only lines where no money moved, a warning otherwise; counts and plain reason words only.</item>
/// </list>
/// </summary>
internal static class AttentionDeriver
{
    /// <summary>The login is called about to expire this long before it does (the same window as the countdown of the Connection tab).</summary>
    public static readonly TimeSpan ExpiringWithin = TimeSpan.FromHours(2);

    /// <summary>The most tickers an item names.</summary>
    public const int MaxSymbols = 3;

    public static IReadOnlyList<AttentionItem> Derive(AttentionFacts facts, IHealSettings settings)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(settings);
        var items = new List<AttentionItem>();
        foreach (var account in facts.Accounts)
        {
            if (Reconnect(account, facts.Now) is { } reconnect)
            {
                items.Add(reconnect);
            }

            if (!account.PortfolioOn)
            {
                continue;
            }

            foreach (var kind in new[] { SyncHealthKind.Holdings, SyncHealthKind.Trades, SyncHealthKind.Ledger })
            {
                if (Failing(account.Id, account.Name, kind, facts, settings) is { } failing)
                {
                    items.Add(failing);
                }
            }

            items.AddRange(Holdings(account, facts, settings));
            if (facts.Dividends.TryGetValue(account.Id, out var dividends) && dividends.Count > 0)
            {
                items.Add(Dividends(account, dividends));
            }

            if (facts.Unmatched.TryGetValue(account.Id, out var unmatched) && unmatched.Total > 0)
            {
                items.Add(Unmatched(account, unmatched));
            }

            if (facts.TradeCounts.TryGetValue(account.Id, out var tradeCounts) && Dropped(account, "trades", tradeCounts) is { } droppedTrades)
            {
                items.Add(droppedTrades);
            }

            if (facts.LedgerCounts.TryGetValue(account.Id, out var ledgerCounts) && Dropped(account, "ledger", ledgerCounts) is { } droppedLedger)
            {
                items.Add(droppedLedger);
            }
        }

        if (facts.DataAccountId is { } dataId && Failing(dataId, facts.DataAccountName ?? "The Data account", SyncHealthKind.Quotes, facts, settings) is { } quotes)
        {
            items.Add(quotes);
        }

        return items;
    }

    private static AttentionItem? Reconnect(AttentionAccount account, DateTimeOffset now)
    {
        var open = new AttentionAction("Reconnect Dhan", AttentionActionKinds.Route, ConnectionPath(account.Id));
        switch (account.Status)
        {
            case ConnectionStatus.Expired:
                return new AttentionItem(
                    $"attention:reconnect:{account.Id:N}:expired:{Stamp(account.ExpiresAt)}",
                    AttentionKinds.ReconnectDhan,
                    AttentionSeverities.Error,
                    "Reconnect Dhan",
                    $"The Dhan login of {account.Name} has expired. Sign in at Dhan again; what was missed is read by itself afterwards.",
                    [open],
                    account.Id);
            case ConnectionStatus.NotConnected:
                return new AttentionItem(
                    $"attention:reconnect:{account.Id:N}:missing:{Stamp(account.LastConnectedAt)}",
                    AttentionKinds.ReconnectDhan,
                    AttentionSeverities.Warning,
                    "Connect to Dhan",
                    $"{account.Name} is not connected to Dhan.",
                    [open with { Label = "Connect to Dhan" }],
                    account.Id);
            default:
                return account.ExpiresAt is { } expires && expires > now && expires - now < ExpiringWithin
                    ? new AttentionItem(
                        $"attention:reconnect:{account.Id:N}:expiring:{Stamp(expires)}",
                        AttentionKinds.ReconnectDhan,
                        AttentionSeverities.Warning,
                        "Reconnect Dhan soon",
                        $"The Dhan login of {account.Name} runs out at {expires.ToOffset(Domain.Market.IndiaTime.Offset).ToString("HH:mm", CultureInfo.InvariantCulture)} IST.",
                        [open],
                        account.Id)
                    : null;
        }
    }

    private static AttentionItem? Failing(Guid accountId, string accountName, SyncHealthKind kind, AttentionFacts facts, IHealSettings settings)
    {
        if (!facts.Health.TryGetValue((accountId, kind), out var health) || !health.IsFailing || health.LastFailureReason is not { } reason)
        {
            return null;
        }

        // A missing or expired login has its own item (and the sync catches up by itself once it is renewed); a retry still to come keeps the owner out of it. The price poll is never retried quietly (the next poll is the retry), so it has no retry to wait for.
        if (reason is SyncFailures.TokenExpired or SyncFailures.NotConnected || (kind != SyncHealthKind.Quotes && RetrySchedule.RetriesRemain(health, settings, facts.Now)))
        {
            return null;
        }

        if (health.ConsecutiveFailedDays < settings.PersistDays && !SyncFailures.IsOwnerOnly(reason))
        {
            return null;
        }

        var (noun, retry, open) = kind switch
        {
            SyncHealthKind.Holdings => ("holdings", new AttentionAction("Retry now", AttentionActionKinds.Command, AttentionCommands.HoldingsSyncOf(accountId)), PortfolioPath(accountId)),
            SyncHealthKind.Trades => ("trades", new AttentionAction("Retry now", AttentionActionKinds.Command, AttentionCommands.BrokerHistorySyncOf(accountId)), PortfolioPath(accountId)),
            SyncHealthKind.Ledger => ("ledger", new AttentionAction("Retry now", AttentionActionKinds.Command, AttentionCommands.BrokerHistorySyncOf(accountId)), PortfolioPath(accountId)),
            _ => ("prices", new AttentionAction("Retry now", AttentionActionKinds.Command, AttentionCommands.ServiceRunOf("holdings-quotes")), "/services"),
        };
        var days = Math.Max(1, health.ConsecutiveFailedDays);
        var title = kind switch
        {
            SyncHealthKind.Holdings => "Holdings are not being updated",
            SyncHealthKind.Trades => "Trades are not being read",
            SyncHealthKind.Ledger => "The ledger is not being read",
            _ => "Prices are not being updated",
        };
        var period = days == 1 ? "could not be updated today" : string.Create(CultureInfo.InvariantCulture, $"have not been updated for {days} days");
        var text = $"The {noun} of {accountName} {period}: {SyncFailures.Text(reason)}.";
        var actions = SyncFailures.IsLogin(reason)
            ? new[] { new AttentionAction("Reconnect Dhan", AttentionActionKinds.Route, ConnectionPath(accountId)) }
            : [retry, new AttentionAction("Open", AttentionActionKinds.Route, open)];
        var key = kind.ToString().ToLowerInvariant();
        return new AttentionItem($"attention:sync:{accountId:N}:{key}:{reason}", AttentionKinds.SyncFailing, AttentionSeverities.Error, title, text, actions, accountId);
    }

    private static IEnumerable<AttentionItem> Holdings(AttentionAccount account, AttentionFacts facts, IHealSettings settings)
    {
        if (!facts.TradeCounts.TryGetValue(account.Id, out var counts))
        {
            yield break;
        }

        var deepened = facts.Health.GetValueOrDefault((account.Id, SyncHealthKind.Trades)) ?? SyncHealth.None(account.Id, SyncHealthKind.Trades);
        var review = new List<BrokerNotRebuilt>();
        var unexplained = new List<BrokerNotRebuilt>();
        foreach (var held in counts.NotRebuilt)
        {
            // A holding that has been closed or fixed since the run is not an item (the next run would drop it from the list; the owner need not wait for it).
            var flags = held.HoldingId is { } id ? facts.Flags.GetValueOrDefault(id) : null;
            if (held.HoldingId is not null && (flags is null || !flags.Open))
            {
                continue;
            }

            if (held.Reason == BrokerLotRebuilder.NeedsReview)
            {
                if (flags is null || flags.HandDated)
                {
                    review.Add(held);
                }
            }
            else if (held.Reason == "quantity_mismatch" && (!Deepenable(held) || DeepeningOver(deepened, settings)))
            {
                unexplained.Add(held);
            }
        }

        if (review.Count > 0)
        {
            yield return Review(account, review);
        }

        if (unexplained.Count > 0)
        {
            yield return Unexplained(account, unexplained, deepened);
        }
    }

    private static AttentionItem Review(AttentionAccount account, List<BrokerNotRebuilt> review)
    {
        var ordered = review.OrderBy(h => h.Symbol, StringComparer.Ordinal).ToList();
        var first = ordered[0];
        var many = ordered.Count == 1 ? "1 holding" : string.Create(CultureInfo.InvariantCulture, $"{ordered.Count} holdings");
        var route = first.HoldingId is { } id ? $"/portfolio?holding={id}&section=trades&rebuild=preview" : $"/portfolio?q={Uri.EscapeDataString(first.Symbol)}";
        return new AttentionItem(
            $"attention:review:{account.Id:N}:{Fingerprint(ordered)}",
            AttentionKinds.HoldingsNeedReview,
            AttentionSeverities.Warning,
            "Holdings need your review",
            $"{many} of {account.Name} {(ordered.Count == 1 ? "has" : "have")} lots with a date you chose, so the lots were not rebuilt from the trades ({Names(ordered)}). Check the rebuild preview and apply it if the trades are right.",
            [new AttentionAction("Review", AttentionActionKinds.Route, route)],
            account.Id);
    }

    private static AttentionItem Unexplained(AttentionAccount account, List<BrokerNotRebuilt> unexplained, SyncHealth deepened)
    {
        var ordered = unexplained.OrderBy(h => h.Symbol, StringComparer.Ordinal).ToList();
        var first = ordered[0];
        var route = first.HoldingId is { } id ? $"/portfolio?holding={id}&section=trades" : $"/portfolio?q={Uri.EscapeDataString(first.Symbol)}";
        var reach = deepened.DeepenedTo is { } to ? $" Trades back to {to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} were read." : string.Empty;
        return new AttentionItem(
            $"attention:mismatch:{account.Id:N}:{Fingerprint(ordered)}",
            AttentionKinds.TradesUnexplained,
            AttentionSeverities.Warning,
            "Trades do not explain a holding",
            $"The trades read from Dhan do not add up to what {account.Name} holds of {Names(ordered)}.{reach} Add the missing trades by hand or import a tradebook.",
            [new AttentionAction("Open trades", AttentionActionKinds.Route, route)],
            account.Id);
    }

    private static AttentionItem Dividends(AttentionAccount account, PendingDividends pending)
    {
        var many = pending.Count == 1 ? "1 credit" : string.Create(CultureInfo.InvariantCulture, $"{pending.Count} credits");
        return new AttentionItem(
            $"attention:dividends:{account.Id:N}:{pending.NewestId.ToString("N", CultureInfo.InvariantCulture)[..8]}",
            AttentionKinds.DividendsPending,
            AttentionSeverities.Info,
            "Possible dividends in the ledger",
            $"{many} in the ledger of {account.Name} look like dividends. Check them and add the ones that are.",
            [new AttentionAction("Review", AttentionActionKinds.Route, $"/portfolio?cash=suggestions&account={account.Id}")],
            account.Id);
    }

    private static AttentionItem Unmatched(AttentionAccount account, UnmatchedCounts unmatched)
    {
        var parts = new List<string>();
        if (unmatched.Holdings > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{unmatched.Holdings} {(unmatched.Holdings == 1 ? "holding" : "holdings")}"));
        }

        if (unmatched.TradeSecurities > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{unmatched.TradeSecurities} traded {(unmatched.TradeSecurities == 1 ? "security" : "securities")}"));
        }

        return new AttentionItem(
            string.Create(CultureInfo.InvariantCulture, $"attention:unmatched:{account.Id:N}:{unmatched.Holdings}-{unmatched.TradeSecurities}"),
            AttentionKinds.SecuritiesUnmatched,
            AttentionSeverities.Warning,
            "Securities with no match in the instrument list",
            $"{string.Join(" and ", parts)} of {account.Name} {(unmatched.Total == 1 ? "has" : "have")} no match in the instrument master, so there is no price or sector for {(unmatched.Total == 1 ? "it" : "them")}. The master refreshes by itself every day.",
            [new AttentionAction("Open", AttentionActionKinds.Route, $"/portfolio?unmatched=1&account={account.Id}")],
            account.Id);
    }

    /// <summary>
    /// The rows the latest succeeded read of a kind dropped, as one item per account and kind (ADR 0030): none when it dropped nothing (which is also how an item resolves). The id holds a hash of the set of reasons, so a changed set is a new item. The text has counts and the
    /// plain words of the reasons only.
    /// </summary>
    private static AttentionItem? Dropped(AttentionAccount account, string kind, BrokerSyncCounts counts)
    {
        if (counts.Dropped <= 0)
        {
            return null;
        }

        // A run recorded before the reasons were kept has a count and no reasons: they are "other".
        IReadOnlyDictionary<string, int> reasons = counts.DroppedByReason.Count > 0 ? counts.DroppedByReason : new Dictionary<string, int> { [BrokerDropReasons.Other] = counts.Dropped };
        var harmless = reasons.Keys.All(r => r == DropReasonWords.Harmless);
        var ordered = reasons.Where(p => p.Value > 0).OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();
        var listed = ordered.Take(MaxReasons).Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Value} {DropReasonWords.Of(p.Key)}")).ToList();
        if (ordered.Count > MaxReasons)
        {
            listed.Add(string.Create(CultureInfo.InvariantCulture, $"+{ordered.Count - MaxReasons} more"));
        }

        var rows = counts.Dropped == 1 ? "1 row" : string.Create(CultureInfo.InvariantCulture, $"{counts.Dropped} rows");
        var set = string.Join('|', reasons.Where(p => p.Value > 0).Select(p => p.Key).Order(StringComparer.Ordinal));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(set)))[..8];
        return new AttentionItem(
            $"attention:dropped:{account.Id:N}:{kind}:{hash}",
            AttentionKinds.RowsDropped,
            harmless ? AttentionSeverities.Info : AttentionSeverities.Warning,
            harmless ? "Rows with no amount were left out" : kind == "trades" ? "Some trades could not be read" : "Some ledger lines could not be read",
            $"The latest {kind} read of {account.Name} dropped {rows}: {string.Join(", ", listed)}. Open the sync line to see where they were.",
            [new AttentionAction("Open", AttentionActionKinds.Route, "/portfolio?focus=sync")],
            account.Id);
    }

    /// <summary>The most reasons a <c>rows_dropped</c> text lists.</summary>
    public const int MaxReasons = 3;

    /// <summary>A mismatch an earlier read of the history could fix (the trades explain more than is held, or a sale is larger than what was open).</summary>
    internal static bool Deepenable(BrokerNotRebuilt held) =>
        held.Detail is "negative_opening" or "sell_exceeds_open";

    /// <summary>The nightly deepening has nothing more to read: Dhan's history ended, the furthest year back was read, or the deepening is switched off.</summary>
    private static bool DeepeningOver(SyncHealth trades, IHealSettings settings) => settings.DeepenMaxYears <= 0 || trades.DeepenExhausted;

    private static string Names(List<BrokerNotRebuilt> ordered)
    {
        var names = string.Join(", ", ordered.Take(MaxSymbols).Select(h => h.Symbol));
        return ordered.Count > MaxSymbols ? string.Create(CultureInfo.InvariantCulture, $"{names} and {ordered.Count - MaxSymbols} more") : names;
    }

    /// <summary>The first 8 hex digits of a hash of the holdings the item is about, so the id changes when the set does.</summary>
    private static string Fingerprint(List<BrokerNotRebuilt> ordered)
    {
        var text = string.Join('|', ordered.Select(h => h.HoldingId?.ToString("N", CultureInfo.InvariantCulture) ?? h.Symbol));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8];
    }

    private static string Stamp(DateTimeOffset? at) => at is { } value ? value.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) : "never";

    private static string ConnectionPath(Guid accountId) => $"/accounts?account={accountId}&tab=connection";

    private static string PortfolioPath(Guid accountId) => $"/accounts?account={accountId}&tab=portfolio";
}
