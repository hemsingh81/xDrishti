using System.Globalization;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Options.Pricing;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Options.Structures;

/// <summary>
/// The re-mark of one defined-risk structure from vendor bars (spec P14-10): a debit spread or a credit spread as the expression of a stored cash trade, or an iron condor of the range family. Every leg is read from the open of the first bar at or after the event minute and held
/// on its strike (<see cref="HeldStrikeSeries"/>); the structure's value per bar is the long legs less the short legs (a debit) or the short legs less the long legs (a credit), checked against its bounds 0 and its width at the exit bar and at a stop-trigger bar (a value outside is two legs' bars that disagree: the counted fate <c>structure_inconsistent</c>, never a price), so <b>the loss of a structure never exceeds its maximum loss, which is
/// known and stored at entry</b>. A stop inside a bar takes the worse leg combination (ADR 0038: the bar high for a short leg, the bar low for a long leg, at the same time); where the stop and the cash exit fall in the same bar the worse value is taken. The exit is the cash exit's
/// mapping of P14-08 (a hole in the bars never moves it to another bar: the leg is priced by the counted Black-76 fallback or the structure is <c>strike_left_window</c>) and the D6 exit before the expiry. A credit structure is costed only on a <b>measured</b> half-spread.
/// There is no naked short leg: a short leg is always paired with a long leg beyond it. Nothing here computes a margin or places an order. Pure.
/// </summary>
public static class StructureRemark
{
    private sealed record LegPlan(bool Long, bool Call, int Offset, int Grid)
    {
        public OptionKind Kind => Call ? OptionKind.Call : OptionKind.Put;
    }

    private sealed record Spec(
        string Name,
        bool Credit,
        bool Condor,
        string SideWord,
        string HoldLabel,
        LegPlan[] Legs,
        Func<int, int?> StopLevel,
        string StopReason,
        Func<RemarkTrade, RemarkContext, int, decimal?> Units,
        string UnitsFate,
        Func<RemarkTrade, int, decimal, decimal> RiskRupees,
        bool RequireMeasured,
        bool CashTwin);

    /// <summary>What the legs of a stop exit are: the structure's exit value is the stop level (or the gapped open value); the leg prices stored and costed are the gapped opens or the bar's worst (long low, short high), which need not sum to that value.</summary>
    public const string StopLegsLabel = "exit: stop; value at the stop level or the gapped open, legs at the gapped open or the bar worst (the costs use the legs)";

    private sealed record ExitPoint(string? Fate, DateTimeOffset Ts, int Value, string Reason, int[] LegPx, bool[] Model, DateTimeOffset BarTime);

    /// <summary>A debit or credit spread as the expression of a stored cash trade. The legs read one rolling series (calls for a bought call spread, puts for a bear put spread, and so on).</summary>
    public static StructureRow EvaluateSpread(RemarkTrade trade, SeriesPlan plan, SpreadParams profile, BarGrid grid, RemarkContext context)
    {
        ArgumentNullException.ThrowIfNull(trade);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(context);
        var credit = profile.Kind == Expression.CreditSpread;

        // debit: a buy is a bull call spread, a sell a bear put spread; credit: a buy is a bull put spread, a sell a bear call spread.
        var kind = trade.IsBuy != credit ? OptionKind.Call : OptionKind.Put;
        var first = profile.Strike == OptionProfileParams.Atm ? 0 : kind == OptionKind.Call ? -1 : 1;
        var second = first + ((kind == OptionKind.Call ? 1 : -1) * profile.WidthSteps);
        LegPlan[] legs = credit ? [new(false, kind == OptionKind.Call, first, 0), new(true, kind == OptionKind.Call, second, 0)] : [new(true, kind == OptionKind.Call, first, 0), new(false, kind == OptionKind.Call, second, 0)];
        var stopPct = profile.StopPct;
        var spec = new Spec(
            profile.Kind.Code(),
            credit,
            false,
            kind == OptionKind.Call ? "CE" : "PE",
            string.Create(CultureInfo.InvariantCulture, $"planned_hold_sessions: {(trade.IsBuy ? profile.MaxHoldSessions : 1)} (1 = intraday)"),
            legs,
            value => stopPct is { } pct ? (int)Math.Round(value * (1m - (pct / 100m)), 0, MidpointRounding.AwayFromZero) : null,
            StructureNames.StructureStop,
            (t, _, maxLoss) => RemarkMoney.UnitsForMaxLoss(t.RiskRupees, maxLoss),
            RemarkFates.RiskInvalid,
            (t, _, _) => t.RiskRupees,
            credit,
            true);
        return Price(trade, plan, spec, grid, [grid], context);
    }

    /// <summary>
    /// An iron condor of the range family: short call and short put <c>k</c> steps from the money, long wings <c>wing_steps</c> beyond, one unit of one lot. There is no cash trade: <paramref name="entry"/> carries the entry minute and the exit is the D6 close before the expiry.
    /// The call bars and the put bars are two series; the call series is the clock.
    /// </summary>
    public static StructureRow EvaluateCondor(RemarkTrade entry, SeriesPlan plan, CondorParams profile, BarGrid calls, BarGrid puts, RemarkContext context)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(puts);
        ArgumentNullException.ThrowIfNull(context);
        var far = profile.K + profile.WingSteps;
        LegPlan[] legs = [new(false, true, profile.K, 0), new(true, true, far, 0), new(false, false, -profile.K, 1), new(true, false, -far, 1)];
        var multiple = profile.M;
        var spec = new Spec(
            "iron_condor",
            true,
            true,
            StructureNames.CondorSide,
            "planned_hold: to the close of the session before the expiry (D6) or the stop",
            legs,
            credit => (int)Math.Round(credit * (1m + multiple), 0, MidpointRounding.AwayFromZero),
            StructureNames.CondorStop,
            (t, c, _) => LotSizeLookup.InForce(c.Lots, c.UnderlyingId, t.TradeDate) is { Available: true } lot ? lot.LotSize : null,
            StructureNames.LotUnknown,
            (_, maxLoss, units) => maxLoss / 100m * units,
            true,
            false);
        return Price(entry, plan, spec, calls, [calls, puts], context);
    }

    private static StructureRow Price(RemarkTrade trade, SeriesPlan plan, Spec spec, BarGrid clock, BarGrid[] grids, RemarkContext context)
    {
        var optionNet = context.Sheet is not null;
        List<string> labels = [plan.Expiry.Label, RemarkFates.BarTimestampLabel, RemarkFates.FillLabel, optionNet ? "basis: net" : "basis: gross", spec.HoldLabel, "structure: " + spec.Name];
        if (spec.CashTwin && !trade.QuantityKnown)
        {
            labels.Add(RemarkFates.QtyPerShare);
        }

        if (!spec.CashTwin)
        {
            labels.Add(StructureNames.NoCashTwin);
        }

        StructureRow Fail(string fate) => new(OptionRemark.Unpaired(trade, plan, fate, labels) with { OptionSide = spec.SideWord }, [], null, null);

        if (trade.OpenAtEnd)
        {
            return Fail(RemarkFates.OpenAtEnd);
        }

        if (context.SheetChanged || optionNet != trade.CashNet)
        {
            return Fail(RemarkFates.BasisMismatch);
        }

        var interval = TimeSpan.FromMinutes(plan.IntervalMinutes);
        var entryMinute = DateTimeOffset.FromUnixTimeSeconds(trade.EntryEpoch);
        var entryIndex = clock.FirstAtOrAfter(entryMinute);
        if (entryIndex < 0 || clock.Times[entryIndex] - entryMinute > interval || OptionRemark.DateOf(clock.Times[entryIndex]) != trade.TradeDate)
        {
            return Fail(RemarkFates.NoSeries);
        }

        var entryTs = clock.Times[entryIndex];
        var rows = new OptionBar[spec.Legs.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var row = grids[spec.Legs[i].Grid].Row(entryTs, spec.Legs[i].Offset);

            // every leg must be readable at entry: a missing or suspect bar is never a price.
            if (row is null || row.Suspect || row.OpenPx <= 0)
            {
                // the first leg of a series (the first leg of the structure, or the first of the put side of a condor) missing is no series; a later leg of the same series missing is the window.
                return Fail(i == 0 || spec.Legs[i].Grid != spec.Legs[i - 1].Grid ? RemarkFates.NoSeries : RemarkFates.StrikeLeftWindow);
            }

            rows[i] = row;
        }

        if (rows.Any(r => !r.Usable))
        {
            return Fail(RemarkFates.ArchiveUnusable);
        }

        foreach (var row in rows)
        {
            var liquidity = LiquidityFilter.Evaluate(row.Oi, row.Volume, row.SpotPx / 100m, context.Liquidity);
            if (liquidity.Verdict != LiquidityVerdict.Pass)
            {
                return Fail(liquidity.Verdict == LiquidityVerdict.Filtered ? RemarkFates.Illiquid : RemarkFates.LiquidityUnknown);
            }
        }

        var entryPx = rows.Select(r => r.OpenPx).ToArray();
        var entryValue = Value(spec, entryPx);
        var width = Width(spec, rows);
        if (entryValue <= 0 || entryValue >= width)
        {
            return Fail(StructureNames.StructureInvalid);
        }

        var maxLoss = spec.Credit ? width - entryValue : entryValue;
        var maxGain = spec.Credit ? entryValue : width - entryValue;
        if (spec.Units(trade, context, maxLoss) is not { } units || units <= 0)
        {
            return Fail(spec.UnitsFate);
        }

        var series = spec.Legs.Select((l, i) => new HeldStrikeSeries(grids[l.Grid], rows[i].StrikePx)).ToArray();
        var schedule = OptionRemark.Schedule(plan, context, clock, entryTs, trade.ExitEpoch, trade.ExitReason);
        var exit = PlanExit(spec, plan, context, clock, series, schedule, entryIndex, entryValue, width, interval);
        if (exit.Fate is not null)
        {
            return Fail(exit.Fate);
        }

        var entryDate = OptionRemark.DateOf(entryTs);
        var exitDate = OptionRemark.DateOf(exit.Ts);
        var pnlPx = spec.Credit ? entryValue - exit.Value : exit.Value - entryValue;
        var gross = Math.Round(pnlPx / 100m * units, 2, MidpointRounding.AwayFromZero);
        decimal? net = null;
        if (context.Sheet is { } sheet)
        {
            var fills = new List<RemarkMoney.LegFill>();
            for (var i = 0; i < rows.Length; i++)
            {
                var steps = Math.Abs(rows[i].Offset);
                var held = series[i].Held(exit.BarTime, out var bar) == HeldState.Available ? Math.Abs(bar!.Offset) : steps;
                var entrySpread = OptionRemark.Spread(context, steps, plan.Expiry.Date, entryTs);
                var exitSpread = OptionRemark.Spread(context, held, plan.Expiry.Date, exit.Ts);
                if (spec.RequireMeasured && (entrySpread.Source != HalfSpreadSource.Measured || exitSpread.Source != HalfSpreadSource.Measured))
                {
                    labels.Add(StructureNames.SpreadNotMeasuredLabel);
                    return Fail(RemarkFates.SpreadUnavailable);
                }

                fills.Add(new RemarkMoney.LegFill(spec.Legs[i].Long ? Side.Buy : Side.Sell, entryPx[i], entryDate, entrySpread));
                fills.Add(new RemarkMoney.LegFill(spec.Legs[i].Long ? Side.Sell : Side.Buy, exit.LegPx[i], exitDate, exitSpread));
            }

            var outcome = RemarkMoney.NetLegs(sheet, gross, units, fills);
            labels.AddRange(outcome.Labels);
            if (outcome.Fate is not null)
            {
                return Fail(outcome.Fate);
            }

            net = outcome.NetInr;
        }

        var lot = LotSizeLookup.InForce(context.Lots, context.UnderlyingId, entryDate);
        if (lot.Available && !lot.Confirmed)
        {
            labels.Add(LotFeasibility.UnconfirmedLabel);
        }

        var sizable = !spec.CashTwin ? RemarkRow.NotApplicable : !trade.QuantityKnown ? RemarkRow.NotApplicable : !lot.Available ? "lot_unknown" : units >= lot.LotSize ? "sizable" : "not_sizable";
        var risk = spec.RiskRupees(trade, maxLoss, units);
        if (exit.Reason == spec.StopReason)
        {
            labels.Add(StopLegsLabel);
        }

        if (exit.Model.Any(m => m))
        {
            labels.Add(RemarkFates.BsFallback);
        }

        labels.Add(string.Create(CultureInfo.InvariantCulture, $"max_loss_px: {maxLoss}"));
        labels.Add(string.Create(CultureInfo.InvariantCulture, $"max_gain_px: {maxGain}"));
        labels.Add(string.Create(CultureInfo.InvariantCulture, $"entry_{(spec.Credit ? "credit" : "debit")}_px: {entryValue}"));
        var legRows = new List<StructureLegRow>(rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            legRows.Add(new StructureLegRow(trade.Key, i, spec.Legs[i].Call ? "CE" : "PE", spec.Legs[i].Long ? StructureNames.LongPosition : StructureNames.ShortPosition, rows[i].StrikePx, entryPx[i], exit.LegPx[i], exit.Model[i] ? [RemarkFates.BsFallback] : []));
        }

        var r = (double)((net ?? gross) / risk);
        var paired = new RemarkRow(
            trade.Key, RemarkFates.Paired, spec.SideWord, plan.Flag, plan.Code, plan.Expiry.Date, plan.Expiry.FromContract ? "contract" : "derived", rows[0].StrikePx, entryTs, exit.Ts, entryValue, exit.Value, (double)units, gross, net, r,
            spec.CashTwin ? trade.CashR : null, exit.Reason, lot.Available ? lot.LotSize : null, sizable, [.. labels.Distinct(StringComparer.Ordinal)]);
        return new StructureRow(paired, legRows, maxLoss, maxGain);
    }

    /// <summary>The structure's value per unit in paise: the long legs less the short legs for a debit, the short legs less the long legs for a credit.</summary>
    private static int Value(Spec spec, int[] px)
    {
        var sum = 0;
        for (var i = 0; i < spec.Legs.Length; i++)
        {
            sum += spec.Legs[i].Long ? px[i] : -px[i];
        }

        return spec.Credit ? -sum : sum;
    }

    /// <summary>The width in paise: the distance between the two strikes of a spread, the wider wing of a condor (one side can lose).</summary>
    private static int Width(Spec spec, OptionBar[] rows) => spec.Condor
        ? Math.Max(Math.Abs(rows[1].StrikePx - rows[0].StrikePx), Math.Abs(rows[3].StrikePx - rows[2].StrikePx))
        : Math.Abs(rows[1].StrikePx - rows[0].StrikePx);

    private static ExitPoint Fault(string fate) => new(fate, default, 0, string.Empty, [], [], default);

    private static ExitPoint PlanExit(Spec spec, SeriesPlan plan, RemarkContext context, BarGrid clock, HeldStrikeSeries[] series, ExitSchedule schedule, int entryIndex, int entryValue, int width, TimeSpan interval)
    {
        var count = series.Length;
        var planned = new BarPremium[count];
        for (var i = 0; i < count; i++)
        {
            planned[i] = OptionRemark.PremiumAt(series[i], spec.Legs[i].Kind, plan.Expiry.Date, context, schedule.BarTime, schedule.PricedAt, schedule.AtClose);
            if (planned[i].Fate is not null)
            {
                return Fault(planned[i].Fate!);
            }
        }

        var cashValue = Value(spec, planned.Select(p => p.Px).ToArray());

        // a value outside 0 and the width is two legs' bars that disagree: a counted fate, never a price.
        if (cashValue < 0 || cashValue > width)
        {
            return Fault(StructureNames.StructureInconsistent);
        }

        var cash = new ExitPoint(null, schedule.PricedAt, cashValue, schedule.Reason, planned.Select(p => p.Px).ToArray(), planned.Select(p => p.Model).ToArray(), schedule.BarTime);
        var level = spec.StopLevel(entryValue);
        var end = schedule.End(clock);
        for (var i = entryIndex; i < end; i++)
        {
            var time = clock.Times[i];
            if (level is not { } stop)
            {
                // no stop: only the key-sessions touched matter; a bar of an unusable one is archive_unusable, an absent bar is not needed.
                if (series.Any(s => s.Held(time, out _) == HeldState.Unusable))
                {
                    return Fault(RemarkFates.ArchiveUnusable);
                }

                continue;
            }

            var bars = new BarPremium[count];
            for (var j = 0; j < count; j++)
            {
                bars[j] = OptionRemark.PremiumAt(series[j], spec.Legs[j].Kind, plan.Expiry.Date, context, time, time, false);
                if (bars[j].Fate is not null)
                {
                    return Fault(bars[j].Fate!);
                }
            }

            var open = Value(spec, bars.Select(b => b.Open).ToArray());

            // the worse leg combination in the bar (ADR 0038): a short leg at its high, a long leg at its low, at the same time.
            var worstPx = bars.Select((b, j) => spec.Legs[j].Long ? b.Low : b.High).ToArray();
            var worst = Value(spec, worstPx);
            var gapped = spec.Credit ? open >= stop : open <= stop;
            var hit = spec.Credit ? worst >= stop : worst <= stop;
            if (!gapped && !hit)
            {
                continue;
            }

            if (open < 0 || open > width || worst < 0 || worst > width)
            {
                return Fault(StructureNames.StructureInconsistent);
            }

            var fill = gapped ? open : stop;
            var legPx = gapped ? bars.Select(b => b.Open).ToArray() : worstPx;
            var covers = i == end - 1 && (schedule.AtClose || time + interval > schedule.ExitMinute);
            var cashWorse = spec.Credit ? cash.Value > fill : cash.Value < fill;
            return covers && cashWorse ? cash : new ExitPoint(null, time, fill, spec.StopReason, legPx, bars.Select(b => b.Model).ToArray(), time);
        }

        return cash;
    }
}
