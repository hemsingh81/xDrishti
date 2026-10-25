using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Planning;

/// <summary>The status of a post-trade prompt, derived on read.</summary>
public static class PromptStatus
{
    /// <summary>No trade found yet and the window is open.</summary>
    public const string Open = "open";

    /// <summary>A stored trade of the same account, symbol, side and day exists.</summary>
    public const string Matched = "matched";

    /// <summary>The window ended with no trade: treated as not taken; nothing is written to the journal.</summary>
    public const string ExpiredNoFill = "expired_no_fill";

    /// <summary>Two armed tickets of the same account, symbol, side and day fit one trade; nothing is guessed (ADR 0093).</summary>
    public const string Ambiguous = "ambiguous";
}

/// <summary>How a matched prompt was matched.</summary>
public static class PromptVia
{
    /// <summary>The journal linked the trade to this ticket's entry.</summary>
    public const string Journal = "journal";

    /// <summary>A trade is on record; the journal grades broker-synced fills only, so it is not linked.</summary>
    public const string TradeOnRecord = "trade_on_record";
}

/// <summary>A stored prompt as the rules need it. <paramref name="Symbol"/> is normalised (<c>TradeRules.NormaliseSymbol</c>).</summary>
public sealed record PromptFacts(long Id, long ItemId, Guid AccountId, string Symbol, Side Side, DateOnly Session);

/// <summary>A stored, non-voided trade of any source. <paramref name="JournalItemId"/> is the ticket the journal linked it to, when it did.</summary>
public sealed record PromptTrade(Guid AccountId, string Symbol, Side Side, DateOnly Date, long? JournalItemId);

/// <summary>The derived state of one prompt. <paramref name="SameTradeItems"/> names the other tickets that fit the same trade when it is ambiguous.</summary>
public sealed record PromptResult(long Id, string Status, string? Via, IReadOnlyList<long> SameTradeItems);

/// <summary>
/// The pure rules of the post-trade prompt (spec P7-20, ADR 0115): the status comes from the stored trades at read time, so the prompt closes the moment the fill exists with no job and no update. The match key is the journal's (ADR 0093): account,
/// normalised symbol, side and day. Nothing is guessed when two tickets fit one trade.
/// </summary>
public static class PromptRules
{
    /// <summary>The statuses of <paramref name="prompts"/> against <paramref name="trades"/> as of <paramref name="today"/>; the window is <paramref name="windowDays"/> trading days after the session.</summary>
    public static IReadOnlyList<PromptResult> Evaluate(IReadOnlyList<PromptFacts> prompts, IReadOnlyList<PromptTrade> trades, DateOnly today, TradingCalendar calendar, int windowDays)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(calendar);
        var results = new List<PromptResult>(prompts.Count);
        foreach (var p in prompts)
        {
            var fitting = trades.Where(t => t.AccountId == p.AccountId && t.Symbol == p.Symbol && t.Side == p.Side && t.Date == p.Session).ToList();
            if (fitting.Count == 0)
            {
                var open = calendar.TradingDaysBetween(p.Session, today) <= windowDays;
                results.Add(new PromptResult(p.Id, open ? PromptStatus.Open : PromptStatus.ExpiredNoFill, null, []));
                continue;
            }

            if (fitting.Any(t => t.JournalItemId == p.ItemId))
            {
                results.Add(new PromptResult(p.Id, PromptStatus.Matched, PromptVia.Journal, []));
                continue;
            }

            if (fitting.Any(t => t.JournalItemId is not null))
            {
                // The journal already gave this trade to another ticket (ADR 0093): this one is not matched and nothing is guessed.
                results.Add(new PromptResult(p.Id, calendar.TradingDaysBetween(p.Session, today) <= windowDays ? PromptStatus.Open : PromptStatus.ExpiredNoFill, null, []));
                continue;
            }

            var others = prompts.Where(o => o.Id != p.Id && o.AccountId == p.AccountId && o.Symbol == p.Symbol && o.Side == p.Side && o.Session == p.Session).Select(o => o.ItemId).Distinct().Order().ToList();
            results.Add(others.Count > 0
                ? new PromptResult(p.Id, PromptStatus.Ambiguous, null, others)
                : new PromptResult(p.Id, PromptStatus.Matched, PromptVia.TradeOnRecord, []));
        }

        return results;
    }

    /// <summary>The words of a prompt on the ticket card.</summary>
    public static string Text(PromptResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Status switch
        {
            PromptStatus.Matched when result.Via == PromptVia.Journal => "A broker-synced trade matches this ticket and the journal linked it.",
            PromptStatus.Matched => "A trade is on record for this account, symbol, side and day. The journal grades broker-synced fills only, so this one is not journaled or graded.",
            PromptStatus.ExpiredNoFill => "No fill found; treated as not taken. Nothing is written to the journal.",
            PromptStatus.Ambiguous => $"Another ticket (item {string.Join(", ", result.SameTradeItems)}) fits the same trade; nothing is guessed, so neither is matched.",
            _ => "Was this trade taken? No trade is on record yet. Enter it where you enter trades, or let the broker sync bring it. Armed means watched; no order is placed.",
        };
    }

    /// <summary>The words of a prompt in the bell: a count and no symbol or price. An ambiguous prompt has its own words.</summary>
    public static string BellText(string status) => status switch
    {
        PromptStatus.Ambiguous => "2 watched tickets of the session fit one trade; nothing is guessed. Open the plan",
        PromptStatus.Matched => "A trade matching a watched ticket is on record; open the plan",
        _ => "1 watched ticket of the session has no trade on record yet; was it taken? Open the plan",
    };
}
