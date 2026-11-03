using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.PairResearch;
using XDrishti.Domain.Pairs;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// The pair walker, version <see cref="Version"/> (P14-13, ADR 0196): the two legs of one selected pair over the 1-minute bars of the trading dates, with the single-instrument walker's fill rules and slippage (<see cref="Slip"/>, <see cref="TickGrid"/>, the entry limit, the
/// first minute that holds a trade at most <see cref="SimulationEngine.MaxFillDelayMinutes"/> after the decision). The trades are gross: the charges are applied afterwards by <c>PairCosting</c> (the walkers of this stage know no cost type). The single-instrument walk is not touched: <see cref="SimulationEngine.Version"/> stays <c>sim-9</c>.
/// <para>
/// The spread <c>ln y - alpha - beta ln x</c> is taken from the last traded minute of each 5-minute bucket; the z-score of a bucket is its spread against the spread values of the previous <c>L</c> sessions of the pair only (never the same session). A decision is taken at a bucket's close
/// and both legs fill at the next minute that holds a trade (the entry as a limit order at the bucket close, the exits at the open). A pair opens only when <c>entry &lt;= |z| &lt; stop</c>, never after 14:45, with one position per pair; an open pair is closed by the core's state machine
/// (<c>|z| &lt;= exit</c>, or the stop) or flat at the 15:15 decision. An entry with a leg that has no bar in the delay is not made (counted); an exit uses the leg's next minute (counted <c>leg_late</c>) and, when the session has none, the last close (<c>end_of_data</c>). A decision with no z-score
/// never opens and never closes by rule (counted). Pure: no clock, no I/O.
/// </para>
/// </summary>
public static class PairWalker
{
    /// <summary>The version of the pair walker, stored with every pair run.</summary>
    public const string Version = "pair-1";

    private const long SecondsPerMinute = 60;

    /// <summary>Walks one pair over the trading dates.</summary>
    public static Result<PairWalkResult> Walk(PairWalkInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var o = input.Options;
        var core = o.Core with { Entry = input.Variant.Entry };
        if (!o.IsValid || !core.IsValid || input.Fills is null || input.YTick is null || input.XTick is null || input.From > input.To || input.Variant.Lookback < 1)
        {
            return Result.Failure<PairWalkResult>(Error.Validation("pairs.options_invalid", "The pair options, the fill rules, the ticks or the dates are not usable."));
        }

        var legY = LegSeries.Index(input.YBars, o);
        var legX = LegSeries.Index(input.XBars, o);
        if (legY is null || legX is null)
        {
            return Result.Failure<PairWalkResult>(Error.Validation(SimulationErrors.BarsNotAscendingCode, "The minutes of a leg are not strictly ascending."));
        }

        var sessions = legY.Days.Keys.Where(legX.Days.ContainsKey).Order().ToArray();
        var pair = input.Pair;
        var spreads = new Dictionary<DateOnly, double[]>();
        double[] SpreadOf(DateOnly day)
        {
            if (!spreads.TryGetValue(day, out var s))
            {
                var cy = legY.Days[day].Close;
                var cx = legX.Days[day].Close;
                s = new double[cy.Length];
                for (var b = 0; b < s.Length; b++)
                {
                    s[b] = cy[b] is { } y && cx[b] is { } x && y > 0 && x > 0 ? Math.Log(y) - pair.Alpha - (pair.Beta * Math.Log(x)) : double.NaN;
                }

                spreads[day] = s;
            }

            return s;
        }

        var slipY = Slip.Of(input.Fills.Slippage, input.YTick.Size);
        var slipX = Slip.Of(input.Fills.Slippage, input.XTick.Size);
        var trades = new List<PairTrade>();
        var counters = PairWalkCounters.Zero;
        for (var k = 0; k < sessions.Length; k++)
        {
            var day = sessions[k];
            if (day < input.From || day > input.To)
            {
                continue;
            }

            var window = new List<double>();
            for (var p = Math.Max(0, k - input.Variant.Lookback); p < k && k >= input.Variant.Lookback; p++)
            {
                window.AddRange(SpreadOf(sessions[p]).Where(double.IsFinite));
            }

            var w = window.ToArray();
            var sd = SampleSd(w);
            var spread = SpreadOf(day);
            var midnight = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUnixTimeSeconds();
            var position = PairPosition.Flat;
            Open? open = null;
            var blockedUntil = long.MinValue;
            for (var b = 0; b < spread.Length; b++)
            {
                var decisionMinute = o.OpenMinute + ((b + 1) * o.BarMinutes);
                if (decisionMinute > o.SquareOffMinute)
                {
                    break;
                }

                var decisionEpoch = midnight + (decisionMinute * SecondsPerMinute);
                if (decisionEpoch <= blockedUntil || (open is null && decisionMinute > o.LastEntryMinute))
                {
                    continue;
                }

                var z = ZScore.Of(w, spread[b], core);
                var step = PairSignal.Next(position, z, core);
                if (open is null)
                {
                    if (step.Reason == PairsReason.ZUnavailable)
                    {
                        counters = counters with { ZUnavailable = counters.ZUnavailable + 1 };
                        continue;
                    }

                    if (step.Action is not (SignalAction.EnterLongSpread or SignalAction.EnterShortSpread))
                    {
                        continue;
                    }

                    var zNow = z.Value;
                    if (!PairTradeRule.MayOpen(zNow, input.Variant.Entry, core.Stop))
                    {
                        counters = counters with { BeyondStop = counters.BeyondStop + 1 };
                        continue;
                    }

                    var long_ = step.Action == SignalAction.EnterLongSpread;
                    var cy = legY.Days[day].Close[b];
                    var cx = legX.Days[day].Close[b];
                    var size = cy is { } py && cx is { } px ? PairTradeRule.Size(o.PairNotional, py, px, pair.Beta) : null;
                    var risk = PairTradeRule.RupeeRisk(o.PairNotional, zNow, sd, core.Stop);
                    if (size is null || risk is null)
                    {
                        counters = counters with { NotSizable = counters.NotSizable + 1 };
                        continue;
                    }

                    var fy = FillEntry(legY, day, decisionEpoch, long_, cy!.Value, input.YTick.Size, input.Fills, slipY, o);
                    var fx = FillEntry(legX, day, decisionEpoch, !long_, cx!.Value, input.XTick.Size, input.Fills, slipX, o);
                    if (fy is null || fx is null)
                    {
                        counters = counters with { LegUnfilled = counters.LegUnfilled + 1 };
                        continue;
                    }

                    position = long_ ? PairPosition.LongSpread : PairPosition.ShortSpread;
                    open = new Open(long_, zNow, size, risk.Value, fy, fx);
                    continue;
                }

                string? reason = step.Action switch
                {
                    SignalAction.Stop => "stop",
                    SignalAction.Exit => "exit",
                    _ => null,
                };
                if (reason is null && decisionMinute >= o.SquareOffMinute)
                {
                    reason = "square_off";
                }

                if (reason is null)
                {
                    if (step.Reason == PairsReason.ZUnavailable)
                    {
                        counters = counters with { ZUnavailable = counters.ZUnavailable + 1 };
                    }

                    continue;
                }

                var exitReason = reason == "stop" ? ExitReason.Stop : ExitReason.SquareOff;
                var ey = FillExit(legY, day, decisionEpoch, open.Long, exitReason, slipY);
                var ex = FillExit(legX, day, decisionEpoch, !open.Long, exitReason, slipX);
                if (ey is null || ex is null)
                {
                    // A leg with no traded minute on the date at all cannot be closed: the pair is left out of the trades and counted as an unfilled leg (the entry fill proves the leg traded that day, so this is unreachable with consistent bars).
                    counters = counters with { LegUnfilled = counters.LegUnfilled + 1 };
                    open = null;
                    position = PairPosition.Flat;
                    continue;
                }

                var (trade, late) = Close(input, open, ey, ex, reason, z.TryGet(out var zv) ? zv : null);
                trades.Add(trade);
                counters = counters with { LegLate = counters.LegLate + late };
                blockedUntil = Math.Max(ey.Epoch, ex.Epoch);
                open = null;
                position = PairPosition.Flat;
            }
        }

        return Result.Success(new PairWalkResult(trades.OrderBy(t => t.EntryEpochSeconds).ThenBy(t => t.ExitEpochSeconds).ToList(), counters));
    }

    private static (PairTrade Trade, int Late) Close(PairWalkInput input, Open open, Fill ey, Fill ex, string reason, double? zExit)
    {
        var sign = open.Long ? 1m : -1m;
        var gross = (sign * (ey.Price - open.Y.Price) * open.Size.QtyY) + (-sign * (ex.Price - open.X.Price) * open.Size.QtyX);
        var turnover = ((open.Y.Price + ey.Price) * open.Size.QtyY) + ((open.X.Price + ex.Price) * open.Size.QtyX);
        var labels = new List<string>();
        var late = (ey.Late ? 1 : 0) + (ex.Late ? 1 : 0);
        if (late > 0)
        {
            labels.Add("leg_late");
        }

        if (ey.EndOfData || ex.EndOfData)
        {
            labels.Add("end_of_data");
        }

        var trade = new PairTrade(
            input.Pair.Key, open.Long ? PairSide.LongSpread : PairSide.ShortSpread, Math.Max(open.Y.Epoch, open.X.Epoch), Math.Max(ey.Epoch, ex.Epoch), open.ZEntry, zExit, reason, open.Size.QtyY, open.Size.QtyX, gross, null, open.Risk,
            PairTradeRule.R(gross, open.Risk), turnover, open.Y.Price, ey.Price, open.X.Price, ex.Price, labels);
        return (trade, late);
    }

    private static Fill? FillEntry(LegSeries leg, DateOnly day, long decisionEpoch, bool isBuy, double closeNow, decimal tick, FillRules fills, Slip slip, PairStudyOptions o)
    {
        if (!TickGrid.TryConvert(closeNow, out var trigger))
        {
            return null;
        }

        _ = TickGrid.TryConvert(fills.EntryLimitPercent, out var percent);
        var fraction = percent / 100m;
        var limit = isBuy ? TickGrid.Down(trigger * (1m + fraction), tick) : TickGrid.Up(trigger * (1m - fraction), tick);
        var d = leg.Days[day];
        for (var j = d.First; j <= d.Last; j++)
        {
            var bar = leg.Bars[j];
            if (bar.EpochSeconds < decisionEpoch)
            {
                continue;
            }

            if (bar.EpochSeconds - decisionEpoch > SimulationEngine.MaxFillDelayMinutes * SecondsPerMinute)
            {
                return null;
            }

            if (bar.Volume <= 0)
            {
                continue;
            }

            if (SessionClock.MinuteOfDay(bar.EpochSeconds) >= o.SquareOffMinute || !TickGrid.TryConvert(bar.Open, out var open) || !TickGrid.TryConvert(bar.High, out var high) || !TickGrid.TryConvert(bar.Low, out var low))
            {
                return null;
            }

            if (isBuy ? open <= limit : open >= limit)
            {
                return new Fill(bar.EpochSeconds, slip.Entry(isBuy, open, limit, high - low), false, false);
            }

            return (isBuy ? low <= limit : high >= limit) ? new Fill(bar.EpochSeconds, limit, false, false) : null;
        }

        return null;
    }

    private static Fill? FillExit(LegSeries leg, DateOnly day, long decisionEpoch, bool wasBought, ExitReason reason, Slip slip)
    {
        var d = leg.Days[day];
        Bar? last = null;
        for (var j = d.First; j <= d.Last; j++)
        {
            var bar = leg.Bars[j];
            if (bar.Volume <= 0 || !SessionClock.InRegularSession(SessionClock.MinuteOfDay(bar.EpochSeconds)))
            {
                continue;
            }

            last = bar;
            if (bar.EpochSeconds < decisionEpoch)
            {
                continue;
            }

            if (!TickGrid.TryConvert(bar.Open, out var open) || !TickGrid.TryConvert(bar.High, out var high) || !TickGrid.TryConvert(bar.Low, out var low))
            {
                return null;
            }

            return new Fill(bar.EpochSeconds, slip.Exit(wasBought, reason, open, high - low), bar.EpochSeconds > decisionEpoch, false);
        }

        if (last is { } final && TickGrid.TryConvert(final.Close, out var close) && TickGrid.TryConvert(final.High, out var h) && TickGrid.TryConvert(final.Low, out var l))
        {
            return new Fill(final.EpochSeconds, slip.Exit(wasBought, ExitReason.EndOfData, close, h - l), true, true);
        }

        return null;
    }

    private static double SampleSd(double[] values)
    {
        if (values.Length < 2)
        {
            return double.NaN;
        }

        var mean = 0.0;
        foreach (var v in values)
        {
            mean += v;
        }

        mean /= values.Length;
        var ss = 0.0;
        foreach (var v in values)
        {
            ss += (v - mean) * (v - mean);
        }

        return Math.Sqrt(ss / (values.Length - 1));
    }

    private sealed record Fill(long Epoch, decimal Price, bool Late, bool EndOfData);

    private sealed record Open(bool Long, double ZEntry, PairSizing Size, decimal Risk, Fill Y, Fill X);

    private sealed record Day(int First, int Last, double?[] Close);

    private sealed class LegSeries
    {
        private LegSeries(IReadOnlyList<Bar> bars, Dictionary<DateOnly, Day> days)
        {
            Bars = bars;
            Days = days;
        }

        public IReadOnlyList<Bar> Bars { get; }

        public Dictionary<DateOnly, Day> Days { get; }

        /// <summary>Indexes the bars by India date and 5-minute bucket; null when the minutes are not strictly ascending.</summary>
        public static LegSeries? Index(IReadOnlyList<Bar> bars, PairStudyOptions o)
        {
            var buckets = (SessionClock.CloseMinute - o.OpenMinute) / o.BarMinutes;
            var days = new Dictionary<DateOnly, Day>();
            var previous = long.MinValue;
            for (var i = 0; i < bars.Count; i++)
            {
                var bar = bars[i];
                if (bar.EpochSeconds <= previous)
                {
                    return null;
                }

                previous = bar.EpochSeconds;
                var date = SessionClock.DateOf(bar.EpochSeconds);
                var minute = SessionClock.MinuteOfDay(bar.EpochSeconds);
                if (!days.TryGetValue(date, out var day))
                {
                    day = new Day(i, i, new double?[buckets]);
                    days[date] = day;
                }
                else
                {
                    days[date] = day with { Last = i };
                }

                if (bar.Volume > 0 && minute >= o.OpenMinute && minute < SessionClock.CloseMinute && double.IsFinite(bar.Close))
                {
                    day.Close[(minute - o.OpenMinute) / o.BarMinutes] = bar.Close;
                }
            }

            return new LegSeries(bars, days);
        }
    }

}
