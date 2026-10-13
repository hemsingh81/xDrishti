using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// The benchmark's XIRR and the alpha (ADR 0020 §2). Pure. The same cash flows as the portfolio's own XIRR (<see cref="ReturnsCalculator"/>: every lot's purchase on its date at its cost, every closed lot's sale on its date at its proceeds; the cash flows keep
/// their dates) go into the index by the rule of <see cref="TradingDays"/> (the chart uses the same): a purchase buys units at the close of the first trading day on or after it, a sale sells units at the close of its first trading day on or after it, at most
/// what is held (the benchmark never goes short and receives no more than its units are worth); a lot bought and sold before a trading day comes is left out. What is left on the valuation date is the closing inflow at the last close on or before it
/// (<see cref="BenchmarkXirrResult.AsOf"/>: the index has no live quote, so on a live day this is the previous close). The index must have a close on or before the first effective day, otherwise the result is <see cref="BenchmarkState.Unavailable"/>, never zero.
/// </summary>
public static class BenchmarkReturns
{
    public static BenchmarkXirrResult Compute(IEnumerable<HoldingData> holdings, DateOnly valuationDate, IReadOnlyList<DailyClose> indexCloses, TradingCalendar calendar)
    {
        var flows = Flows(holdings, valuationDate, indexCloses, calendar);
        if (flows.Gap != BenchmarkAvailability.Available)
        {
            return new BenchmarkXirrResult(flows.Gap == BenchmarkAvailability.NoFlows ? BenchmarkState.Insufficient : BenchmarkState.Unavailable, null, flows.Gap);
        }

        var result = Xirr.Compute(flows.Flows);
        return result.State switch
        {
            XirrState.Ok => new BenchmarkXirrResult(BenchmarkState.Ok, result.Rate, BenchmarkAvailability.Available, flows.AsOf),
            XirrState.NotMeaningful => new BenchmarkXirrResult(BenchmarkState.NotMeaningful, result.Rate, BenchmarkAvailability.Available, flows.AsOf),
            _ => new BenchmarkXirrResult(BenchmarkState.Insufficient, null, BenchmarkAvailability.Available, flows.AsOf),
        };
    }

    /// <summary>
    /// The cash flows of the same money in the index, ready for <see cref="Xirr"/>: purchases at their own dates (negative), what the index units sold bring (positive) and the closing inflow on the valuation date. <see cref="BenchmarkFlows.Gap"/> is
    /// <see cref="BenchmarkAvailability.Available"/> when the index could be used; <see cref="BenchmarkFlows.ClosingValue"/> is the value of the units left at the last close on or before the valuation date, which is what the chart's benchmark line ends in.
    /// </summary>
    public static BenchmarkFlows Flows(IEnumerable<HoldingData> holdings, DateOnly valuationDate, IReadOnlyList<DailyClose> indexCloses, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        ArgumentNullException.ThrowIfNull(indexCloses);
        ArgumentNullException.ThrowIfNull(calendar);
        var events = Events(holdings, calendar);
        if (events.Count == 0)
        {
            return new BenchmarkFlows([], 0m, null, BenchmarkAvailability.NoFlows);
        }

        if (indexCloses.Count == 0)
        {
            return new BenchmarkFlows([], 0m, null, BenchmarkAvailability.NoBars);
        }

        if (indexCloses[0].Date > events[0].Effective)
        {
            return new BenchmarkFlows([], 0m, null, BenchmarkAvailability.StartsAfterFirstFlow);
        }

        var flows = new List<CashFlow>();
        var units = 0m;
        foreach (var (date, effective, amount) in events)
        {
            var close = CloseAt(indexCloses, effective).Close;
            if (amount < 0m)
            {
                units += -amount / close;
                flows.Add(new CashFlow(date, amount));
                continue;
            }

            var sold = Math.Min(amount / close, units);
            units -= sold;
            if (sold > 0m)
            {
                flows.Add(new CashFlow(date, sold * close));
            }
        }

        var closeAtValuation = CloseAt(indexCloses, valuationDate);
        var closing = units * closeAtValuation.Close;
        if (closing > 0m)
        {
            flows.Add(new CashFlow(valuationDate, closing));
        }

        return new BenchmarkFlows(flows, closing, closeAtValuation.Date, BenchmarkAvailability.Available);
    }

    /// <summary>
    /// Portfolio XIRR minus benchmark XIRR in percentage points (4 decimals); null unless both are <see cref="XirrState.Ok"/> / <see cref="BenchmarkState.Ok"/> (a rate that is not meaningful is not subtracted).
    /// </summary>
    public static decimal? Alpha(XirrResult portfolio, BenchmarkXirrResult benchmark)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(benchmark);
        return portfolio is { State: XirrState.Ok, Rate: { } own } && benchmark is { State: BenchmarkState.Ok, Rate: { } other }
            ? Math.Round((decimal)((own - other) * 100.0), 4, MidpointRounding.AwayFromZero)
            : null;
    }

    /// <summary>The benchmark XIRR as a percent number (12.5 = 12.5 %), 4 decimals; null unless the state is ok.</summary>
    public static decimal? Percent(BenchmarkXirrResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result is { State: BenchmarkState.Ok, Rate: { } rate } ? Math.Round((decimal)(rate * 100.0), 4, MidpointRounding.AwayFromZero) : null;
    }

    /// <summary>The close on the date or, when the index has none that day, the last earlier one (the first close when there is none earlier: the caller checks). The closes are in date order and not empty.</summary>
    internal static DailyClose CloseAt(IReadOnlyList<DailyClose> closes, DateOnly date)
    {
        int low = 0, high = closes.Count - 1, found = 0;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            if (closes[middle].Date <= date)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return closes[found];
    }

    /// <summary>
    /// Purchases (negative) and sales (positive) of all lots: the date of the cash flow, the trading day it takes part in on, and the amount; ordered by that day, purchases before sales. A lot whose sale takes part on the day of (or before) its purchase
    /// is left out altogether.
    /// </summary>
    private static List<(DateOnly Date, DateOnly Effective, decimal Amount)> Events(IEnumerable<HoldingData> holdings, TradingCalendar calendar)
    {
        var events = new List<(DateOnly Date, DateOnly Effective, decimal Amount)>();
        foreach (var lot in holdings.SelectMany(h => h.Lots))
        {
            var bought = TradingDays.OnOrAfter(calendar, lot.AcquiredOn);
            if (lot.ClosedOn is { } closedOn)
            {
                var sold = TradingDays.OnOrAfter(calendar, closedOn);
                if (sold <= bought)
                {
                    continue;
                }

                events.Add((closedOn, sold, lot.Qty * (lot.ClosePrice ?? 0m)));
            }

            events.Add((lot.AcquiredOn, bought, -(lot.Qty * lot.UnitCost)));
        }

        events.Sort((a, b) => a.Effective != b.Effective ? a.Effective.CompareTo(b.Effective) : a.Amount.CompareTo(b.Amount));
        return events;
    }
}
