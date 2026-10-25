using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Planning;

/// <summary>What one replay of a ticket gave: a grade (or none yet), the trade behind it, and the run it came from.</summary>
/// <param name="Grade">The grade; null when pending.</param>
/// <param name="Trade">The closed trade of a grade, or the open trade of a pending replay; else null.</param>
/// <param name="RunKey">The key of the replay run; null when the ticket was not replayed.</param>
/// <param name="Pending">Why nothing can be said yet; null when graded.</param>
/// <param name="EngineVersion">The engine that ran; null when the ticket was not replayed.</param>
internal sealed record TicketReplay(TicketGrade? Grade, SimTrade? Trade, string? RunKey, string? Pending, string? EngineVersion = null);

/// <summary>
/// The one path that walks a ticket through the stored bars (spec P7-13, ADR 0090; reused by the paper capture of P10-01, ADR 0116): it asks the simulation use case for the decision day of the ticket and turns the record into a grade with the
/// pure <see cref="TicketGrader"/>. Grading and paper capture both call it and nothing else builds a <see cref="RunSimulationQuery"/> for a ticket, so a paper trade cannot drift from the grade. Reads only.
/// </summary>
internal sealed class TicketReplayer(IStrategyStore strategies, IBasketQueries baskets, IQueryHandler<RunSimulationQuery, Result<SimulationRun>> simulation)
{
    private Dictionary<string, Guid>? _instruments;

    /// <summary>Replays one ticket planned for <paramref name="forDate"/>, reading no bar after <paramref name="lastClosed"/>.</summary>
    public async Task<TicketReplay> ReplayAsync(DateOnly forDate, DateOnly lastClosed, TradingCalendar calendar, NewPlanItem item, CostChoice costs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(calendar);
        if (CellId.TryParse(item.Evidence.CellKey) is not { } cell || (item.Side != "BUY" && item.Side != "SELL"))
        {
            return Final(TicketGrader.NotGradable("the ticket's cell key or side cannot be read"));
        }

        if (!ExitLabel.TryRead(cell.Exit, out var exit, out var exitReason))
        {
            return Final(TicketGrader.NotGradable(exitReason));
        }

        var stored = await strategies.GetByHashAsync(item.StrategyVersionHash, cancellationToken);
        if (stored is null)
        {
            return Final(TicketGrader.NotGradable("the strategy version of the ticket is not stored"));
        }

        // Shortcut: the symbol is mapped to an instrument through the non-archived baskets, first match wins (the plan item stores no instrument id). An instrument that has left every basket is pending, not final.
        _instruments ??= await InstrumentsAsync(cancellationToken);
        if (!_instruments.TryGetValue(item.Symbol, out var instrumentId))
        {
            return Wait("the symbol `" + item.Symbol + "` is in no basket now, so its instrument is unknown");
        }

        // The engine cuts trades by the day of the DECISION. A ticket on a daily-or-longer timeframe is decided at the close of D (the session it was built on) and fills at the open of F, so its window starts on D; a ticket on an intraday
        // timeframe is decided on F, so its window starts on F. Only that one decision day is the ticket's (the grader checks). An intraday-style ticket is read to F, a swing ticket to the last closed session; nothing after the last closed
        // session is ever read. Only the counters the replayer cuts to its window (trades dropped by the sizing or cost rule, positions that cannot cross a discontinuity) are attributed to the decision day, by taking away a second run that starts the day after it (same end); flagged days and excluded sessions are read by date from the run record; no-trade reasons are not attributed.
        var daily = Timeframes.TryParse(cell.Timeframe, out var timeframe) && timeframe >= Timeframe.D1;
        var information = Before(calendar, forDate);
        var decisionDay = daily ? information : forDate;
        var swing = string.Equals(cell.Style, "swing", StringComparison.OrdinalIgnoreCase);
        var to = swing && lastClosed > forDate ? lastClosed : forDate;
        var after = decisionDay == information ? forDate : forDate.AddDays(1);
        var main = await simulation.HandleAsync(new RunSimulationQuery(Request(stored, cell, decisionDay, to, instrumentId, costs, exit)), cancellationToken);
        if (main.IsFailure)
        {
            // A refusal (a sheet not in force yet, a missing instrument row, a strategy that does not compile now) may be mended later: pending, not a final grade.
            return Wait("the simulator refused the ticket: " + Clip(main.Error.Code + " " + main.Error.Message));
        }

        SimulationRun? tail = null;
        if (after <= to)
        {
            var second = await simulation.HandleAsync(new RunSimulationQuery(Request(stored, cell, after, to, instrumentId, costs, exit)), cancellationToken);
            if (second.IsFailure)
            {
                return Wait("the simulator refused the ticket: " + Clip(second.Error.Code + " " + second.Error.Message));
            }

            tail = second.Value;
        }

        var result = TicketGrader.Grade(main.Value, tail, item.Side == "BUY" ? Side.Buy : Side.Sell, forDate, information, daily, swing);
        return new TicketReplay(result.Grade, result.Trade, main.Value.RunKey, result.PendingReason, main.Value.EngineVersion);
    }

    private static TicketReplay Final(TicketGrade grade) => new(grade, null, null, null);

    private static TicketReplay Wait(string reason) => new(null, null, null, reason);

    private static SimulationRequest Request(StoredVersion stored, CellId cell, DateOnly from, DateOnly to, Guid instrumentId, CostChoice costs, ExitChoice exit) =>
        new(stored.StrategyId, null, cell.Timeframe, from, to, [instrumentId], null, costs, StrategyHash: stored.Hash, Exits: exit);

    /// <summary>
    /// The trading session before <paramref name="forDate"/> (the session the ticket was built on). Shortcut: when the calendar knows none within ten days (a long closure, or an empty calendar) the previous calendar day is used; the window
    /// then starts on a non-session and a daily decision at the close of the true D would be missed (the ticket reads as not triggered).
    /// </summary>
    private static DateOnly Before(TradingCalendar calendar, DateOnly forDate)
    {
        for (var i = 1; i <= 10; i++)
        {
            if (calendar.IsTradingDay(forDate.AddDays(-i)))
            {
                return forDate.AddDays(-i);
            }
        }

        return forDate.AddDays(-1);
    }

    private async Task<Dictionary<string, Guid>> InstrumentsAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var basket in await baskets.ListAsync(false, cancellationToken))
        {
            if (await baskets.MembersAsync(basket.Id, null, null, cancellationToken) is { } members)
            {
                foreach (var m in members.Items)
                {
                    result.TryAdd(m.Instrument.Symbol, m.Instrument.Id);
                }
            }
        }

        return result;
    }

    private static string Clip(string text) => text.Length <= 300 ? text : string.Concat(text.AsSpan(0, 299), "…");
}
