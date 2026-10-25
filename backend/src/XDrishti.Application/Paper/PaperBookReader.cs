using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Market;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Paper;

/// <summary>
/// The paper book for the nightly plan (spec P10-02, ADR 0118). It is available only when every trading session from the epoch's first to the last closed one has a capture row with outcome done and nothing pending; otherwise it says so and
/// the plan takes nothing from it. Open risk, funds, held symbols and the loss carried come from the derived ledger; a counted ticket is one taken trade however many accounts it was sized for. Read only.
/// </summary>
internal sealed class PaperBookReader(PaperLedgerService ledgers, IPaperLedgerData data, ITradingCalendar calendars, IHoldingClassifications classifications, ILogger<PaperBookReader> logger) : IPaperBookReader
{
    private const int MaxDatesListed = 5;

    public async ValueTask<PaperBookRead> ReadAsync(DateOnly lastClosedSession, IReadOnlyList<PaperAccountInput> accounts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var epoch = await data.EpochAsync(null, cancellationToken);
        if (epoch is null)
        {
            PaperLog.BookUnavailable(logger, "no_epoch");
            return new PaperBookUnavailable(PaperWording.BookUnavailable("no capture has run yet, so there is no epoch"));
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        var finished = await data.FinishedSessionsAsync(epoch.StartedOn, cancellationToken);
        var missing = new List<DateOnly>();
        for (var day = epoch.StartedOn; day <= lastClosedSession; day = day.AddDays(1))
        {
            if (calendar.IsTradingDay(day) && !finished.Contains(day))
            {
                missing.Add(day);
            }
        }

        if (missing.Count > 0)
        {
            PaperLog.BookUnavailable(logger, "capture_behind");
            var listed = string.Join(", ", missing.Take(MaxDatesListed).Select(d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            return new PaperBookUnavailable(PaperWording.BookUnavailable($"capture is behind (sessions without a finished capture: {listed}{(missing.Count > MaxDatesListed ? $" and {missing.Count - MaxDatesListed} more" : string.Empty)})"));
        }

        var built = await ledgers.BuildAsync(accounts, epoch, lastClosedSession, calendar, cancellationToken);
        var ids = built.SelectMany(a => a.Instruments.Values).Distinct().ToList();
        var sectors = ids.Count == 0 ? new Dictionary<Guid, string>() : new Dictionary<Guid, string>(await classifications.SectorsAsync(ids, cancellationToken));
        var baskets = ids.Count == 0 ? new Dictionary<Guid, IReadOnlyList<BasketRef>>() : new Dictionary<Guid, IReadOnlyList<BasketRef>>(await classifications.BasketsAsync(ids, cancellationToken));

        var positions = new List<BookPosition>();
        var books = new List<PaperAccountBook>();
        foreach (var a in built)
        {
            var f = a.Figures;
            books.Add(new PaperAccountBook(
                a.Account.AccountId, D(f.Equity), D(f.OpenRiskPct), D(f.FundsAvailable), D(f.DailyLossPct),
                a.Positions.Select(p => p.Trade.Symbol.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal)));
            if (f.Equity is not > 0)
            {
                continue; // no capital: the account's limits are unknown and its positions cannot be a percent of anything
            }

            foreach (var p in a.Positions)
            {
                var id = a.Instruments.TryGetValue(p.Trade.Symbol, out var found) ? found : (Guid?)null;
                positions.Add(new BookPosition(
                    p.Trade.Symbol.ToUpperInvariant(), p.Trade.Side, p.Trade.Swing ? "swing" : "intraday", (double)(p.RiskPctOfEquity ?? 0m),
                    id is { } sid && sectors.TryGetValue(sid, out var sector) ? sector : null,
                    id is { } bid ? baskets.TryGetValue(bid, out var list) ? [.. list.Select(b => b.Name).Order(StringComparer.Ordinal)] : [] : null));
            }
        }

        var taken = built.SelectMany(a => a.Trades)
            .Where(t => t.Closed && t.TicketGroup == "primary")
            .OrderBy(t => t.Exit!.ExitAt).ThenBy(t => t.EntryId)
            .GroupBy(t => t.TradeKey, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(t => new TakenTrade(t.TradeKey, IndiaTime.Today(t.Exit!.ExitAt), t.Exit.RSim))
            .OrderBy(t => t.ClosedOn).ThenBy(t => t.TradeId, StringComparer.Ordinal)
            .ToList();
        var notes = new List<string> { PaperWording.BookNote };
        if (built.Any(a => a.Figures.Equity is not > 0))
        {
            notes.Add(PaperWording.NoCapitalNote);
        }

        PaperLog.BookRead(logger, books.Count, positions.Count, taken.Count);
        return new PaperBook(positions, books, taken, notes);
    }

    private static double? D(decimal? value) => value is { } v ? (double)v : null;
}
