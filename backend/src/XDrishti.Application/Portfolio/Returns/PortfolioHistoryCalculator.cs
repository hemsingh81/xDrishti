using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// The value history of a set of holdings (ADR 0020, docs/design/12 §4). Pure: no I/O, no clock, no ports — the closes, the calendar and today's prices are inputs.
/// <list type="bullet">
/// <item>A lot is <b>held on a day d</b> when <c>acquired_on ≤ d</c> and it is open or <c>closed_on &gt; d</c>; its value that day is <c>qty × adjusted close</c>, its cost <c>qty × unit cost</c>. A lot bought and sold on the same day never counts. A day
/// without a close carries the last earlier close forward (halts). The dates are the trading days of the calendar from the earliest lot to the last stored close, plus today's live point.</item>
/// <item>A holding without any stored close is <b>left out and counted</b>, never valued at zero. A lot acquired before its instrument's first stored close counts from that close on (the history says so, <see cref="HistoryCoverage.LateStart"/>): it
/// is not valued at an invented price.</item>
/// <item>The <b>benchmark</b> receives the same cash flows: each purchase (at its cost) buys index units at that day's close, each sale (at its proceeds) sells units, at most what it holds; value = units × close. Without an index close on or before the first
/// flow it is unavailable, never zero; it ends at the index's last close (no flat point is made up for the days after it, for example a live day). NAV series (units issued at the NAV, flows do not move it) are built by <see cref="NavSeries"/> for both.</item>
/// </list>
/// </summary>
public static class PortfolioHistoryCalculator
{
    private const int Decimals = 4;

    public static PortfolioSeries Build(HistoryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (lots, coverage, partial) = EffectiveLots(input);
        if (lots.Count == 0)
        {
            return new PortfolioSeries([], coverage, partial, BenchmarkAvailability.NoFlows, null);
        }

        // Today's point exists only when at least one price of today belongs to an instrument that is in the history; otherwise it would be a carried-forward point labelled "latest price".
        var usable = input.Live is { } offered && lots.Any(l => offered.Prices.ContainsKey(l.InstrumentId)) ? offered : null;
        input = input with { Live = usable };

        var dates = TradingDates(input, lots);
        if (dates.Length == 0)
        {
            return new PortfolioSeries([], coverage, partial, BenchmarkAvailability.NoFlows, null);
        }

        var days = dates.Length;
        var value = new decimal[days];
        var carried = new decimal[days];
        var flow = new decimal[days];
        var invested = new decimal[days + 1];
        var held = new long[days + 1];
        var buyCash = new decimal[days];
        var sellCash = new decimal[days];
        var liveIndex = input.Live is { } live ? Array.BinarySearch(dates, live.Date) : -1;

        var started = new long[days + 1];
        var ended = new long[days + 1];
        var delta = new long[days + 1];
        foreach (var group in lots.GroupBy(l => l.InstrumentId))
        {
            Array.Clear(delta);
            Array.Clear(started);
            Array.Clear(ended);
            foreach (var lot in group)
            {
                var first = IndexOnOrAfter(dates, lot.Start);
                if (first >= days)
                {
                    continue;
                }

                var end = lot.ClosedOn is { } closedOn ? IndexOnOrAfter(dates, closedOn) : days;
                if (end <= first)
                {
                    continue;
                }

                var amount = lot.Qty * lot.UnitCost;
                delta[first] += lot.Qty;
                delta[end] -= lot.Qty;
                started[first] += lot.Qty;
                ended[end] += lot.Qty;
                held[first] += lot.Qty;
                held[end] -= lot.Qty;
                invested[first] += amount;
                invested[end] -= amount;
                buyCash[first] += amount;
                if (lot.ClosedOn is not null && end < days)
                {
                    sellCash[end] += lot.Qty * lot.ClosePrice;
                }
            }

            AddInstrument(group.Key, input, dates, liveIndex, delta, started, ended, value, carried, flow);
        }

        var empty = new bool[days];
        var heldQuantity = 0L;
        var cost = 0m;
        for (var d = 0; d < days; d++)
        {
            heldQuantity += held[d];
            empty[d] = heldQuantity == 0;
            cost += invested[d];
            invested[d] = cost;
        }

        var nav = NavSeries.Compute(carried, flow, empty);
        var benchmark = Benchmark(input.BenchmarkCloses, dates, buyCash, sellCash, out var benchValue, out var benchNav, out var benchmarkAsOf);

        var points = new HistoryPoint[days];
        for (var d = 0; d < days; d++)
        {
            points[d] = new HistoryPoint(
                dates[d],
                Round(value[d]),
                Round(invested[d]),
                benchValue?[d] is { } worthOfBenchmark ? Round(worthOfBenchmark) : null,
                Round(nav[d]),
                benchNav?[d] is { } navOfBenchmark ? Round(navOfBenchmark) : null);
        }

        return new PortfolioSeries(points, coverage, partial, benchmark, liveIndex >= 0 ? input.Live!.At : null, benchmarkAsOf);
    }

    /// <summary>
    /// The lots that can be shown, with the coverage of the open holdings. A lot counts from the later of its acquisition date and its instrument's first stored close; one that was closed before that never counts.
    /// </summary>
    private static (List<EffectiveLot> Lots, HistoryCoverage Coverage, bool Partial) EffectiveLots(HistoryInput input)
    {
        var lots = new List<EffectiveLot>();
        int open = 0, covered = 0, unpriced = 0, late = 0;
        var leftOutClosed = false;
        foreach (var holding in input.Holdings)
        {
            if (holding.Lots.Count == 0)
            {
                continue;
            }

            IReadOnlyList<DailyClose>? closes = null;
            var isCovered = holding.InstrumentId is { } id && input.Closes.TryGetValue(id, out closes) && closes.Count > 0;
            if (holding.IsOpen)
            {
                open++;
                if (isCovered)
                {
                    covered++;
                }
                else
                {
                    unpriced++;
                }
            }
            else if (!isCovered)
            {
                leftOutClosed = true;
            }

            if (!isCovered)
            {
                continue;
            }

            var firstClose = closes![0].Date;
            var lateStart = false;
            foreach (var lot in holding.Lots)
            {
                if (lot.Qty <= 0)
                {
                    continue;
                }

                if (lot.AcquiredOn < firstClose && HasTradingDay(input.Calendar, lot.AcquiredOn, firstClose))
                {
                    lateStart = true;
                }

                var start = lot.AcquiredOn > firstClose ? lot.AcquiredOn : firstClose;
                if (lot.ClosedOn is { } closedOn && closedOn <= start)
                {
                    continue;
                }

                lots.Add(new EffectiveLot(holding.InstrumentId!.Value, lot.Qty, lot.UnitCost, start, lot.ClosedOn, lot.ClosePrice ?? 0m));
            }

            if (lateStart)
            {
                late++;
            }
        }

        var coverage = new HistoryCoverage(open, covered, unpriced, late);
        return (lots, coverage, unpriced > 0 || leftOutClosed || late > 0);
    }

    /// <summary>The trading days from the earliest lot to the last stored close of the instruments in use, and today's live date when it is a later trading day.</summary>
    private static DateOnly[] TradingDates(HistoryInput input, List<EffectiveLot> lots)
    {
        var first = lots.Min(l => l.Start);
        var last = lots.Select(l => l.InstrumentId).Distinct().Max(id => input.Closes[id][^1].Date);
        if (input.Live is { } live && live.Date > last && input.Calendar.IsTradingDay(live.Date))
        {
            last = live.Date;
        }

        var dates = new List<DateOnly>();
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            if (input.Calendar.IsTradingDay(date))
            {
                dates.Add(date);
            }
        }

        return [.. dates];
    }

    /// <summary>Adds one instrument's contribution to the daily totals: the value held after the day, the value of what was held before it at today's prices, and the day's net flow at today's prices.</summary>
    private static void AddInstrument(
        Guid instrumentId,
        HistoryInput input,
        DateOnly[] dates,
        int liveIndex,
        long[] delta,
        long[] started,
        long[] ended,
        decimal[] value,
        decimal[] carried,
        decimal[] flow)
    {
        var closes = input.Closes[instrumentId];
        var p = -1;
        var quantity = 0L;
        var previous = 0L;
        for (var d = 0; d < dates.Length; d++)
        {
            while (p + 1 < closes.Count && closes[p + 1].Date <= dates[d])
            {
                p++;
            }

            var close = p >= 0 ? closes[p].Close : 0m;
            if (d == liveIndex && input.Live!.Prices.TryGetValue(instrumentId, out var price) && price > 0m)
            {
                close = price;
            }

            quantity += delta[d];
            value[d] += quantity * close;
            carried[d] += previous * close;
            flow[d] += (started[d] - ended[d]) * close;
            previous = quantity;
        }
    }

    /// <summary>The same cash flows in the index: value and NAV per day, or null with the reason it cannot be compared.</summary>
    private static BenchmarkAvailability Benchmark(
        IReadOnlyList<DailyClose> closes, DateOnly[] dates, decimal[] buyCash, decimal[] sellCash, out decimal?[]? value, out decimal?[]? nav, out DateOnly? asOf)
    {
        value = null;
        nav = null;
        asOf = null;
        if (closes.Count == 0)
        {
            return BenchmarkAvailability.NoBars;
        }

        if (closes[0].Date > dates[0])
        {
            return BenchmarkAvailability.StartsAfterFirstFlow;
        }

        var lastClose = closes[^1].Date;
        if (lastClose < dates[0])
        {
            return BenchmarkAvailability.EndsBeforeFirstFlow;
        }

        var days = dates.Length;
        var worth = new decimal[days];
        var carried = new decimal[days];
        var flow = new decimal[days];
        var empty = new bool[days];
        var units = 0m;
        var p = -1;
        for (var d = 0; d < days; d++)
        {
            while (p + 1 < closes.Count && closes[p + 1].Date <= dates[d])
            {
                p++;
            }

            var close = closes[p].Close;
            var before = units;
            var bought = buyCash[d] / close;
            units += bought;
            var sold = Math.Min(sellCash[d] / close, units);
            units -= sold;
            worth[d] = units * close;
            carried[d] = before * close;
            flow[d] = (bought - sold) * close;
            empty[d] = units == 0m;
        }

        var navs = NavSeries.Compute(carried, flow, empty);

        // The benchmark ends at the index's last close: the days after it are left empty instead of repeating that close.
        value = new decimal?[days];
        nav = new decimal?[days];
        for (var d = 0; d < days && dates[d] <= lastClose; d++)
        {
            value[d] = worth[d];
            nav[d] = navs[d];
            asOf = BenchmarkReturns.CloseAt(closes, dates[d]).Date;
        }

        return BenchmarkAvailability.Available;
    }

    /// <summary>The index of the first date that is on or after <paramref name="date"/>; the number of dates when there is none.</summary>
    private static int IndexOnOrAfter(DateOnly[] dates, DateOnly date)
    {
        var found = Array.BinarySearch(dates, date);
        return found >= 0 ? found : ~found;
    }

    /// <summary>Whether a trading day lies in [<paramref name="from"/>, <paramref name="before"/>).</summary>
    private static bool HasTradingDay(TradingCalendar calendar, DateOnly from, DateOnly before)
    {
        for (var date = from; date < before; date = date.AddDays(1))
        {
            if (calendar.IsTradingDay(date))
            {
                return true;
            }
        }

        return false;
    }

    private static decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);

    private sealed record EffectiveLot(Guid InstrumentId, long Qty, decimal UnitCost, DateOnly Start, DateOnly? ClosedOn, decimal ClosePrice);
}
