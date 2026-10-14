using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// Profit/loss and XIRR from lots (ADR 0012, docs/design/12 §4). Pure: no I/O, no clock. Open lots are valued at the holding's last price on
/// <c>valuationDate</c>; closed lots contributed their sale on the day they were closed. A group's XIRR comes from the union of all its
/// members' cash flows, never from an average. These are the gross, price-only figures; dividends and charges are added beside them by <c>IncomeCalculator</c> (ADR 0026), never inside them.
/// </summary>
public static class ReturnsCalculator
{
    /// <summary>Percentages and money are given to four decimals.</summary>
    private const int Decimals = 4;

    /// <summary>
    /// The figures of one holding. With <paramref name="withXirr"/> false the XIRR (the one costly figure) is not worked out and <see cref="ReturnFigures.Xirr"/> is <see cref="XirrResult.NoData"/> — for callers that only order or filter by
    /// the money figures; every other figure is the same.
    /// </summary>
    public static ReturnFigures ForHolding(HoldingData holding, DateOnly valuationDate, bool withXirr = true)
    {
        ArgumentNullException.ThrowIfNull(holding);
        if (holding.IsPriced)
        {
            return Of([holding], valuationDate, withXirr);
        }

        // Still held, but no usable price: the cost is known, the worth is not (and it is not a total loss).
        var (realised, estimated) = Realised([holding]);
        var cost = holding.Lots.Where(l => l.IsOpen).Sum(l => l.Qty * l.UnitCost);
        return new ReturnFigures(Round(cost), null, null, null, new XirrResult(XirrState.NoPrice, null), Round(realised), estimated);
    }

    /// <summary>
    /// Totals of holdings. Holdings without a usable price (<see cref="HoldingData.IsPriced"/>) are left out of invested, value, profit and XIRR (their
    /// worth is unknown; the summary counts them separately). Realised results of closed lots are always included.
    /// </summary>
    public static ReturnFigures ForGroup(IEnumerable<HoldingData> holdings, DateOnly valuationDate)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        var all = holdings.ToList();
        var unpriced = all.Where(h => !h.IsPriced).ToList();
        var figures = Of([.. all.Where(h => h.IsPriced)], valuationDate, withXirr: true);
        if (unpriced.Count == 0)
        {
            return figures;
        }

        var (extra, extraEstimated) = Realised(unpriced);
        return figures with { RealisedPnl = Round(figures.RealisedPnl + extra), RealisedEstimated = figures.RealisedEstimated || extraEstimated };
    }

    /// <summary>
    /// The returns of one lot on its own: an open lot is valued at the last price; a closed lot at its sale (its realised result is the whole
    /// profit or loss).
    /// </summary>
    public static ReturnFigures ForLot(HoldingData holding, LotData lot, DateOnly valuationDate)
    {
        ArgumentNullException.ThrowIfNull(holding);
        ArgumentNullException.ThrowIfNull(lot);
        var cost = lot.Qty * lot.UnitCost;
        var flows = new List<CashFlow> { new(lot.AcquiredOn, -cost) };
        if (lot.ClosedOn is { } closedOn)
        {
            var proceeds = lot.Qty * (lot.ClosePrice ?? 0m);
            flows.Add(new CashFlow(closedOn, proceeds));
            return Build(cost, proceeds, proceeds - cost, lot.Estimated, flows);
        }

        if (!holding.IsPriced)
        {
            return new ReturnFigures(Round(cost), null, null, null, new XirrResult(XirrState.NoPrice, null), 0m, false);
        }

        var value = lot.Qty * holding.LastPrice;
        flows.Add(new CashFlow(valuationDate, value));
        return Build(cost, value, 0m, false, flows);
    }

    /// <summary>The cash flows of holdings: every lot's purchase, every closed lot's sale, and today's value of what is still held.</summary>
    public static IReadOnlyList<CashFlow> FlowsOf(IEnumerable<HoldingData> holdings, DateOnly valuationDate)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        var flows = new List<CashFlow>();
        foreach (var holding in holdings)
        {
            var value = 0m;
            foreach (var lot in holding.Lots)
            {
                flows.Add(new CashFlow(lot.AcquiredOn, -(lot.Qty * lot.UnitCost)));
                if (lot.ClosedOn is { } closedOn)
                {
                    flows.Add(new CashFlow(closedOn, lot.Qty * (lot.ClosePrice ?? 0m)));
                }
                else
                {
                    value += lot.Qty * holding.LastPrice;
                }
            }

            if (value > 0m)
            {
                flows.Add(new CashFlow(valuationDate, value));
            }
        }

        return flows;
    }

    private static (decimal Realised, bool Estimated) Realised(IEnumerable<HoldingData> holdings)
    {
        var closed = holdings.SelectMany(h => h.Lots).Where(l => !l.IsOpen).ToList();
        return (closed.Sum(l => l.Qty * ((l.ClosePrice ?? 0m) - l.UnitCost)), closed.Any(l => l.Estimated));
    }

    private static ReturnFigures Of(IReadOnlyList<HoldingData> holdings, DateOnly valuationDate, bool withXirr)
    {
        var invested = 0m;
        var value = 0m;
        var realised = 0m;
        var estimated = false;
        foreach (var holding in holdings)
        {
            foreach (var lot in holding.Lots)
            {
                if (lot.ClosedOn is null)
                {
                    invested += lot.Qty * lot.UnitCost;
                    value += lot.Qty * holding.LastPrice;
                }
                else
                {
                    realised += lot.Qty * ((lot.ClosePrice ?? 0m) - lot.UnitCost);
                    estimated |= lot.Estimated;
                }
            }
        }

        return Build(invested, value, realised, estimated, FlowsOf(holdings, valuationDate), withXirr);
    }

    private static ReturnFigures Build(decimal invested, decimal value, decimal realised, bool estimated, IReadOnlyList<CashFlow> flows, bool withXirr = true)
    {
        var pnl = value - invested;
        decimal? pct = invested > 0m ? Round(pnl / invested * 100m) : null;
        return new ReturnFigures(Round(invested), Round(value), Round(pnl), pct, withXirr ? Xirr.Compute(flows) : XirrResult.NoData, Round(realised), estimated);
    }

    private static decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
}
