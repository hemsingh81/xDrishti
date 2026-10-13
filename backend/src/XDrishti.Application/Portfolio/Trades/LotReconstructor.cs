using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>What one holding looks like to the reconstruction: the broker's quantity <c>Qty</c> and average cost <c>AvgCost</c>, the account's first import date, its trades and the applied actions of its instrument.</summary>
internal sealed record ReconInput(long Qty, decimal AvgCost, DateOnly FirstImportDate, IReadOnlyList<ReconTrade> Trades, IReadOnlyList<AppliedAction> Actions);

/// <summary>
/// Rebuilds the lots of a holding from its trades (ADR 0023). Pure, deterministic and idempotent: the same input always gives the same lots.
/// <list type="number">
/// <item>Trades are converted to today's share basis with the applied corporate actions (<see cref="ActionConversion"/>); a quantity that is not whole afterwards is a mismatch (<see cref="MismatchReasons.FractionalAfterAdjustment"/>).</item>
/// <item>Net = bought − sold; the opening quantity <c>P0 = Q − net</c>. Below 0 the trades explain more than the broker holds: a mismatch, no lots.</item>
/// <item>Trades are walked in order (date; on a day buys before sells, then time, then the order stored). An <b>opening lot</b> of <c>P0</c> exists from the start, dated the first import date or the day before the first trade when that is
/// earlier, flagged as using the default date; each buy opens a lot (source trade, cost = the price on today's basis); each sell closes open lots first in first out — the opening lot first — into pieces closed on the sale day at the real sale price
/// (not estimated); a partial sale splits a lot into a closed piece and an open rest with the same date and cost. A sale larger than all open shares is a mismatch (<see cref="MismatchReasons.SellExceedsOpen"/>).</item>
/// <item>The opening lot's unit cost follows from the broker's average cost <c>A</c>, which Indian brokers keep by the <b>weighted-average-cost method</b> (a sale does not change it): the cost per share of the
/// opening shares is what <c>A</c> implies over everything ever bought, <c>c0 = (A × (P0 + B) − BC) / P0</c>, with <c>B</c> the shares and <c>BC</c> the cost of all the buys of the trades (on today's basis). It holds
/// whether or not sales used the opening lot up, and the pieces sold from the opening lot cost <c>c0</c> too, so the realised profit is consistent. When <c>c0</c> is not above 0 (the trades cost more than <c>A</c> allows) the opening
/// lot costs <c>A</c> and the result is flagged <see cref="CostNotes.OpeningCostFallback"/>. With <c>P0 = 0</c> there is no opening lot. <b>Invariants:</b> the open lot quantities add up to <c>Q</c>; with <b>no sales</b> in the trades and an opening lot (<c>P0 &gt; 0</c>)
/// the open lots cost <c>Q × A</c> (to within the rounding of <c>c0</c> to 4 decimals: 0.0001 × the opening quantity); with sales the first-in first-out cost basis can differ from the broker's average-cost figure by nature — the
/// difference is reported (<c>costDelta</c>), not hidden.</item>
/// <item>Without trades the result is one lot of <c>Q</c> at <c>A</c> dated the first import date and flagged as default: the state before any import.</item>
/// </list>
/// Costs and prices are rounded to 4 decimals as the lots store them.
/// </summary>
internal static class LotReconstructor
{
    public static Reconstruction Reconstruct(ReconInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var average = TradeRules.Round(input.AvgCost);
        if (input.Trades.Count == 0)
        {
            return new Reconstruction(true, null, input.Qty, 0, input.Qty, [new LotPlan(input.FirstImportDate, input.Qty, average, LotSource.Import, UsesDefaultDate: true)], 0m, 0, 0, 0, null);
        }

        var ordered = input.Trades
            .OrderBy(t => t.Date)
            .ThenBy(t => t.Side)
            .ThenBy(t => t.Time ?? TimeOnly.MinValue)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Seq)
            .ThenBy(t => t.Id)
            .ToList();
        var factors = AdjustmentFactors.From(input.Actions);
        var converted = new List<Converted>(ordered.Count);
        var fractional = false;
        long bought = 0;
        long sold = 0;
        foreach (var trade in ordered)
        {
            var (qty, price) = ActionConversion.Convert(trade.Qty, trade.Price, factors.ActionsAfter(trade.Date));
            if (qty is not { } whole)
            {
                fractional = true;
                continue;
            }

            converted.Add(new Converted(trade.Date, trade.Side, whole, TradeRules.Round(price)));
            if (trade.Side == TradeSide.Buy)
            {
                bought += whole;
            }
            else
            {
                sold += whole;
            }
        }

        var buys = ordered.Count(t => t.Side == TradeSide.Buy);
        var sells = ordered.Count - buys;
        var adjustedFor = factors.ActionsAfter(ordered[0].Date).Count;
        if (fractional)
        {
            return Mismatch(MismatchReasons.FractionalAfterAdjustment, input.Qty, null, null, adjustedFor, buys, sells);
        }

        var net = bought - sold;
        var opening = input.Qty - net;
        if (opening < 0)
        {
            return Mismatch(MismatchReasons.NegativeOpening, input.Qty, net, opening, adjustedFor, buys, sells);
        }

        return Walk(input, converted, net, opening, average, adjustedFor, buys, sells);
    }

    private static Reconstruction Walk(ReconInput input, List<Converted> trades, long net, long opening, decimal average, int adjustedFor, int buys, int sells)
    {
        var firstTrade = trades[0].Date;
        var openingDate = input.FirstImportDate < firstTrade.AddDays(-1) ? input.FirstImportDate : firstTrade.AddDays(-1);
        var lots = new List<WorkLot>(trades.Count + 1);
        WorkLot? openingLot = null;
        if (opening > 0)
        {
            openingLot = new WorkLot(openingDate, opening, 0m, LotSource.Import, usesDefaultDate: true);
            lots.Add(openingLot);
        }

        var pieces = new List<Piece>();
        var first = 0;
        foreach (var trade in trades)
        {
            if (trade.Side == TradeSide.Buy)
            {
                lots.Add(new WorkLot(trade.Date, trade.Qty, trade.Price, LotSource.Trade, usesDefaultDate: false));
                continue;
            }

            var remaining = trade.Qty;
            while (remaining > 0 && first < lots.Count)
            {
                var lot = lots[first];
                if (lot.Remaining == 0)
                {
                    first++;
                    continue;
                }

                var take = Math.Min(remaining, lot.Remaining);
                lot.Remaining -= take;
                remaining -= take;
                pieces.Add(new Piece(lot, take, trade.Date, trade.Price));
            }

            if (remaining > 0)
            {
                return Mismatch(MismatchReasons.SellExceedsOpen, input.Qty, net, opening, adjustedFor, buys, sells);
            }
        }

        string? costNote = null;
        if (openingLot is not null)
        {
            // The average cost covers the opening shares and all the buys of the trades (a sale does not change it): what it leaves after the buys is the opening shares' cost.
            var boughtQty = trades.Where(t => t.Side == TradeSide.Buy).Sum(t => t.Qty);
            var boughtCost = trades.Where(t => t.Side == TradeSide.Buy).Sum(t => t.Qty * t.Price);
            var unit = TradeRules.Round(((average * (opening + boughtQty)) - boughtCost) / opening);
            if (unit > 0m)
            {
                openingLot.Cost = unit;
            }
            else
            {
                openingLot.Cost = average;
                costNote = CostNotes.OpeningCostFallback;
            }
        }

        var plan = new List<LotPlan>(lots.Count + pieces.Count);
        plan.AddRange(lots.Where(l => l.Remaining > 0).Select(l => new LotPlan(l.Date, l.Remaining, l.Cost, l.Source, l.UsesDefaultDate)));
        var realised = 0m;
        foreach (var piece in pieces)
        {
            var lot = piece.Lot;
            plan.Add(new LotPlan(lot.Date, piece.Qty, lot.Cost, lot.Source, lot.UsesDefaultDate, piece.On, piece.Price));
            realised += piece.Qty * (piece.Price - lot.Cost);
        }

        return new Reconstruction(true, null, input.Qty, net, opening, plan, realised, adjustedFor, buys, sells, costNote);
    }

    private static Reconstruction Mismatch(string reason, long qty, long? net, long? opening, int adjustedFor, int buys, int sells) =>
        new(false, reason, qty, net, opening, [], 0m, adjustedFor, buys, sells, null);

    private sealed record Converted(DateOnly Date, TradeSide Side, long Qty, decimal Price);

    private sealed class WorkLot(DateOnly date, long remaining, decimal cost, LotSource source, bool usesDefaultDate)
    {
        public DateOnly Date { get; } = date;

        public long Remaining { get; set; } = remaining;

        public decimal Cost { get; set; } = cost;

        public LotSource Source { get; } = source;

        public bool UsesDefaultDate { get; } = usesDefaultDate;
    }

    private sealed record Piece(WorkLot Lot, long Qty, DateOnly On, decimal Price);
}
