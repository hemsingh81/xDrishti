using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Options.Pricing;

namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// The re-mark of one stored cash trade as a single long option (spec P14-08). Entry at the open of the first option bar at or after the cash entry minute; the strike (<c>atm</c> or <c>itm1</c>) is read from that bar and then held; the cash exit maps to the open of
/// the first bar at or after the exit minute (a session square-off to the close of the last bar of the session); an optional premium stop exits at the level or at a gapped open, and where it and the cash exit fall in the same bar the lower premium is taken (ADR 0038);
/// a position still open at the close of the last session before the expiry leaves there. A premium comes from a vendor bar; a Black-76 price on an observed IV no older than the setting is the labelled fallback; nothing is ever read at a window edge, as a zero or
/// as the last price. The size is the cash trade's rupee risk over the option's delta (O.2). Every trade returns one row: paired, or unpaired with one reason. Pure: reads nothing later than the exit bar except the close of a session's last bar for a square-off.
/// </summary>
public static class OptionRemark
{
    private const double MinDelta = 1e-6;

    public static RemarkRow Evaluate(RemarkTrade trade, SeriesPlan plan, OptionProfileParams profile, BarGrid grid, RemarkContext context)
    {
        ArgumentNullException.ThrowIfNull(trade);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(context);
        var optionNet = context.Sheet is not null;
        List<string> labels = [plan.Expiry.Label, RemarkFates.BarTimestampLabel, RemarkFates.FillLabel, optionNet ? "basis: net" : "basis: gross", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"planned_hold_sessions: {(trade.IsBuy ? profile.MaxHoldSessions : 1)} (1 = intraday)")];
        if (!trade.QuantityKnown)
        {
            labels.Add(RemarkFates.QtyPerShare);
        }

        RemarkRow Fail(string fate) => Unpaired(trade, plan, fate, labels);

        if (trade.OpenAtEnd)
        {
            return Fail(RemarkFates.OpenAtEnd);
        }

        if (context.SheetChanged || optionNet != trade.CashNet)
        {
            return Fail(RemarkFates.BasisMismatch);
        }

        var interval = TimeSpan.FromMinutes(plan.IntervalMinutes);
        var kind = trade.IsBuy ? OptionKind.Call : OptionKind.Put;
        var entryMinute = DateTimeOffset.FromUnixTimeSeconds(trade.EntryEpoch);
        var entryIndex = grid.FirstAtOrAfter(entryMinute);
        if (entryIndex < 0 || grid.Times[entryIndex] - entryMinute > interval || DateOf(grid.Times[entryIndex]) != trade.TradeDate)
        {
            return Fail(RemarkFates.NoSeries);
        }

        var entryTs = grid.Times[entryIndex];
        var offset = profile.Strike == OptionProfileParams.Atm ? 0 : (trade.IsBuy ? -1 : 1);
        var row = grid.Row(entryTs, offset);
        if (row is null || row.Suspect)
        {
            return Fail(RemarkFates.NoSeries);
        }

        if (!row.Usable)
        {
            return Fail(RemarkFates.ArchiveUnusable);
        }

        var series = new HeldStrikeSeries(grid, row.StrikePx);
        if (!TryDelta(row, kind, plan.Expiry.Date, entryTs, out var delta, out var ivUnit))
        {
            return Fail(RemarkFates.NoIv);
        }

        var spotRupees = row.SpotPx / 100m;
        var liquidity = LiquidityFilter.Evaluate(row.Oi, row.Volume, spotRupees, context.Liquidity);
        if (liquidity.Verdict != LiquidityVerdict.Pass)
        {
            return Fail(liquidity.Verdict == LiquidityVerdict.Filtered ? RemarkFates.Illiquid : RemarkFates.LiquidityUnknown);
        }

        var units = RemarkMoney.Units(trade.RiskRupees, delta, trade.EntryPrice, trade.StopPrice);
        if (units is null)
        {
            return Fail(RemarkFates.RiskInvalid);
        }

        var entryPx = row.OpenPx;
        var exit = PlanExit(trade, plan, profile, grid, series, kind, context, entryIndex, entryPx);
        if (exit.Fate is not null)
        {
            return Fail(exit.Fate);
        }

        if (exit.Model)
        {
            labels.Add(RemarkFates.BsFallback);
        }

        var entryDate = DateOf(entryTs);
        var exitDate = DateOf(exit.Ts);
        var gross = RemarkMoney.Gross(entryPx, exit.Px, units.Value);
        decimal? net = null;
        if (context.Sheet is { } sheet)
        {
            var exitOffset = series.Held(exit.BarTime, out var exitBar) == HeldState.Available ? Math.Abs(exitBar!.Offset) : Math.Abs(row.Offset);
            var entrySpread = Spread(context, Math.Abs(row.Offset), plan.Expiry.Date, entryTs);
            var exitSpread = Spread(context, exitOffset, plan.Expiry.Date, exit.Ts);
            var outcome = RemarkMoney.Net(sheet, gross, units.Value, entryPx, exit.Px, entryDate, exitDate, entrySpread, exitSpread);
            if (outcome.Fate is not null)
            {
                labels.AddRange(outcome.Labels);
                return Fail(outcome.Fate);
            }

            net = outcome.NetInr;
            labels.AddRange(outcome.Labels);
        }

        var lot = LotSizeLookup.InForce(context.Lots, context.UnderlyingId, entryDate);
        var feasibility = LotFeasibility.Evaluate(lot, delta, trade.EntryPrice, trade.StopPrice, trade.Quantity);
        if (feasibility.Label is not null)
        {
            labels.Add(feasibility.Label);
        }

        labels.Add(RemarkFates.DeltaLabel);
        labels.Add("iv_unit: " + ivUnit);
        var r = (double)((net ?? gross) / trade.RiskRupees);
        return new RemarkRow(
            trade.Key, RemarkFates.Paired, OptionSide(trade), plan.Flag, plan.Code, plan.Expiry.Date, plan.Expiry.FromContract ? "contract" : "derived", row.StrikePx, entryTs, exit.Ts, entryPx, exit.Px, (double)units.Value, gross, net, r, trade.CashR,
            exit.Reason, lot.Available ? lot.LotSize : null, feasibility.Code, [.. labels.Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>An unpaired row of a trade the series cannot be chosen for: the fate and no figures.</summary>
    public static RemarkRow Unpaired(RemarkTrade trade, SeriesPlan? plan, string fate, IReadOnlyList<string> labels)
    {
        ArgumentNullException.ThrowIfNull(trade);
        ArgumentNullException.ThrowIfNull(labels);
        return new RemarkRow(
            trade.Key, fate, OptionSide(trade), plan?.Flag, plan?.Code, plan?.Expiry.Date, plan is null ? "none" : plan.Expiry.FromContract ? "contract" : "derived", null,
            DateTimeOffset.FromUnixTimeSeconds(trade.EntryEpoch), DateTimeOffset.FromUnixTimeSeconds(trade.ExitEpoch), null, null, null, null, null, null, null, trade.ExitReason, null, RemarkRow.NotApplicable, [.. labels.Distinct(StringComparer.Ordinal)]);
    }

    private static string OptionSide(RemarkTrade trade) => trade.IsBuy ? "CE" : "PE";

    private static DateOnly DateOf(DateTimeOffset at) => SessionClock.DateOf(at.ToUnixTimeSeconds());

    private static bool TryDelta(OptionBar row, OptionKind kind, DateOnly expiry, DateTimeOffset at, out double delta, out string ivUnit)
    {
        delta = 0;
        ivUnit = string.Empty;
        if (row.Iv is not { } iv || !IvUnits.FromVendor(iv).TryGet(out var vol) || !ExpiryTime.YearFraction(at, expiry).TryGet(out var years))
        {
            return false;
        }

        if (!Black76.Greeks(kind, row.SpotPx / 100.0, row.StrikePx / 100.0, years, 1.0, vol.Fraction).TryGet(out var greeks) || Math.Abs(greeks.Delta) < MinDelta)
        {
            return false;
        }

        delta = greeks.Delta;
        ivUnit = vol.UnitLabel;
        return true;
    }

    private static HalfSpreadValue Spread(RemarkContext context, int steps, DateOnly expiry, DateTimeOffset at)
    {
        var ist = IndiaTime.ToIst(at);
        var bucket = SpreadBuckets.Of(steps, expiry.DayNumber - DateOnly.FromDateTime(ist.DateTime).DayNumber, TimeOnly.FromDateTime(ist.DateTime));
        return HalfSpreadResolver.Resolve(context.SpreadModel, context.AssumedHalfSpreadPct, bucket, DateOnly.FromDateTime(ist.DateTime));
    }

    private readonly record struct ExitPoint(string? Fate, DateTimeOffset Ts, int Px, string Reason, DateTimeOffset BarTime, bool Model);

    private static ExitPoint Fault(string fate) => new(fate, default, 0, string.Empty, default, false);

    private static ExitPoint PlanExit(RemarkTrade trade, SeriesPlan plan, OptionProfileParams profile, BarGrid grid, HeldStrikeSeries series, OptionKind kind, RemarkContext context, int entryIndex, int entryPx)
    {
        var interval = TimeSpan.FromMinutes(plan.IntervalMinutes);
        var exitMinute = DateTimeOffset.FromUnixTimeSeconds(trade.ExitEpoch);
        var exitDate = SessionClock.DateOf(trade.ExitEpoch);
        var entryTs = grid.Times[entryIndex];
        var reason = trade.ExitReason;
        DateTimeOffset barTime;
        var atClose = false;
        if (exitDate > plan.LastSession)
        {
            // D6: leave at the close of the last session before the expiry, whatever the bars of that session hold (a missing last bar is priced by the model or the trade is strike_left_window; the exit is never earlier than that close).
            atClose = true;
            reason = RemarkFates.ExpiryExit;
            barTime = LastBarStart(context, plan.LastSession, interval);
        }
        else if (SessionClock.MinuteOfDay(trade.ExitEpoch) >= CloseMinute(context, exitDate) - plan.IntervalMinutes)
        {
            // a square-off, or an exit after the regular last bar began: the close of that last bar.
            atClose = true;
            barTime = LastBarStart(context, exitDate, interval);
        }
        else
        {
            var next = grid.FirstAtOrAfter(exitMinute);
            barTime = next >= 0 && DateOf(grid.Times[next]) == exitDate && grid.Times[next] - exitMinute <= interval ? grid.Times[next] : exitMinute;
        }

        if (barTime < entryTs)
        {
            barTime = entryTs;
            atClose = false;
        }

        var pricedAt = atClose ? barTime + interval : barTime;
        var planned = PremiumAt(series, kind, plan.Expiry.Date, context, barTime, pricedAt, atClose);
        if (planned.Fate is not null)
        {
            return Fault(planned.Fate);
        }

        var cash = new ExitPoint(null, pricedAt, planned.Px, reason, barTime, planned.Model);
        var stopLevel = profile.PremiumStopPct is { } pct ? (int?)Math.Round(entryPx * (1m - (pct / 100m)), 0, MidpointRounding.AwayFromZero) : null;
        var at = grid.FirstAtOrAfter(barTime);
        var end = at < 0 ? grid.Times.Count : (atClose && grid.Times[at] == barTime ? at + 1 : at);
        for (var i = entryIndex; i < end; i++)
        {
            var time = grid.Times[i];
            if (stopLevel is not { } level)
            {
                // no premium stop: only the key-sessions touched matter; a bar of an unusable one is archive_unusable, an absent bar is not needed.
                if (series.Held(time, out _) == HeldState.Unusable)
                {
                    return Fault(RemarkFates.ArchiveUnusable);
                }

                continue;
            }

            var bar = PremiumAt(series, kind, plan.Expiry.Date, context, time, time, false);
            if (bar.Fate is not null)
            {
                return Fault(bar.Fate);
            }

            if (bar.Low > level && bar.Open > level)
            {
                continue;
            }

            var fill = bar.Open <= level ? bar.Open : level;
            var covers = i == end - 1 && (atClose || time + interval > exitMinute);
            return covers && cash.Px < fill ? cash : new ExitPoint(null, time, fill, RemarkFates.PremiumStop, time, bar.Model);
        }

        return cash;
    }

    private static int CloseMinute(RemarkContext context, DateOnly date) => context.Calendar.SessionOf(date)?.ToMinute ?? SessionClock.CloseMinute;

    private static DateTimeOffset LastBarStart(RemarkContext context, DateOnly date, TimeSpan interval) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).AddMinutes(CloseMinute(context, date)) - interval;

    private readonly record struct Premium(string? Fate, int Open, int Low, int Px, bool Model);

    private static Premium PremiumAt(HeldStrikeSeries series, OptionKind kind, DateOnly expiry, RemarkContext context, DateTimeOffset barTime, DateTimeOffset pricedAt, bool atClose)
    {
        switch (series.Held(barTime, out var bar))
        {
            case HeldState.Available:
                return new Premium(null, bar!.OpenPx, bar.LowPx, atClose ? bar.ClosePx : bar.OpenPx, false);
            case HeldState.Unusable:
                return new Premium(RemarkFates.ArchiveUnusable, 0, 0, 0, false);
            default:
                var model = Fallback(series, kind, expiry, context, barTime, pricedAt);
                if (model is null)
                {
                    return new Premium(RemarkFates.StrikeLeftWindow, 0, 0, 0, false);
                }

                return new Premium(null, model.Value, model.Value, model.Value, true);
        }
    }

    /// <summary>
    /// The Black-76 price, in paise, of the held strike at a bar with no vendor bar: on the latest vendor IV of that strike strictly before the bar and within the setting's age, a forward equal to the underlying's one-minute close that ended at the instant priced,
    /// no rate (a labelled approximation). Null when there is no such IV, no price, or the model gives no positive price: never a guess.
    /// </summary>
    private static int? Fallback(HeldStrikeSeries series, OptionKind kind, DateOnly expiry, RemarkContext context, DateTimeOffset barTime, DateTimeOffset pricedAt)
    {
        if (series.LatestIv(barTime, pricedAt, context.MaxIvAgeMinutes) is not { } latest || context.SpotPaiseBefore(pricedAt) is not { } spot || spot <= 0)
        {
            return null;
        }

        if (!IvUnits.FromVendor(latest.Iv).TryGet(out var vol) || !ExpiryTime.YearFraction(pricedAt, expiry).TryGet(out var years))
        {
            return null;
        }

        if (!Black76.Price(kind, spot / 100.0, series.StrikePx / 100.0, years, 1.0, vol.Fraction).TryGet(out var price))
        {
            return null;
        }

        var paise = (int)Math.Round(price * 100.0, MidpointRounding.AwayFromZero);
        return paise > 0 ? paise : null;
    }
}
