using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criteria 3, 9 and 11 of P3-03e over 500 seeded daily series (<see cref="SeededDaily"/>): the counters and the fates add up, the last day of a window does not depend on how far the read goes (the prefix property with the pending order), and
/// the entry of an overnight order is settled by the minutes up to the fill minute and by nothing after it. Each property has a negative control that must fail, so that the property is known to be able to fail.
/// </summary>
public sealed class DailySeededTests
{
    private static readonly FillRules Fills = FillRules.Default;

    private static List<CandidateOutcome> Gated(DailySeries series, IReadOnlyList<Bar> bars, IEnumerable<EntryCandidate> candidates)
    {
        var rules = series.Rules(Fills);
        var result = PositionGate.Apply(candidates, c => TradeWalker.Walk(c, bars, rules));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? "seed " + series.Seed + ": " + result.Error.Code + " " + result.Error.Message : string.Empty);
        return [.. result.Value];
    }

    // ---- criterion 9: the counters and the fates add up

    [Fact]
    public void The_counters_and_the_fates_add_up_on_every_series_with_every_reason_counted_once()
    {
        var reasons = new Dictionary<NoTradeReason, int>();
        long overnight = 0, trades = 0, candidates = 0;

        foreach (var series in SeededDaily.All)
        {
            var outcomes = Gated(series, series.Bars, series.Candidates);
            var counters = outcomes.Aggregate(WalkCounters.Empty, (sum, o) => sum.Add(o.Walk.ToCounters()));
            var fates = outcomes.Where(o => o.Walk.NoTrade is not null).Aggregate(RunFates.Empty, (sum, o) => sum.Add(InstrumentReplayer.NoTradeFate(o.Walk.NoTrade, "TEST")));

            counters.Candidates.ShouldBe(series.Candidates.Count, "seed " + series.Seed);
            (counters.Trades + counters.NoFillBar + counters.AfterSquareOff + counters.EntryThroughStop + counters.LimitNotReached + counters.PositionOpen + counters.OrderPendingAtEnd)
                .ShouldBe(counters.Candidates, "seed " + series.Seed + ": Candidates = Trades + every no-trade reason");
            counters.OvernightEntries.ShouldBe(counters.Trades, "seed " + series.Seed + ": every trade of a daily series is filled on a later date than its decision");
            counters.AfterSquareOff.ShouldBe(0, "seed " + series.Seed);
            (counters.ExitsStop + counters.ExitsTarget + counters.ExitsMaxHold + counters.ExitsEndOfData).ShouldBe(counters.Trades, "seed " + series.Seed);
            fates.NoTrade.ShouldBe(counters.Candidates - counters.Trades, "seed " + series.Seed + ": the fates of the no-trades add up");
            fates.OrderPendingAtEnd.ShouldBe(counters.OrderPendingAtEnd, "seed " + series.Seed);
            outcomes.Count(o => o.Walk.NoTrade == NoTradeReason.OrderPendingAtEnd).ShouldBeLessThanOrEqualTo(1, "seed " + series.Seed + ": only the last candidate of the data can be pending");
            foreach (var outcome in outcomes.Where(o => o.Walk.NoTrade is not null))
            {
                reasons[outcome.Walk.NoTrade!.Value] = reasons.GetValueOrDefault(outcome.Walk.NoTrade.Value) + 1;
            }

            overnight += counters.OvernightEntries;
            trades += counters.Trades;
            candidates += counters.Candidates;
        }

        // The generator reaches every reason a daily series can have. Measured on 500 series and 3,763 candidates: 343 pending orders, 610 misses, 398 entries through the stop, 126 without a fill bar, 1,077 position-open and 1,209 trades;
        // the thresholds are four fifths of the measure, so that a change of the generator that stops reaching a case fails here.
        reasons.GetValueOrDefault(NoTradeReason.OrderPendingAtEnd).ShouldBeGreaterThanOrEqualTo(270);
        reasons.GetValueOrDefault(NoTradeReason.LimitNotReached).ShouldBeGreaterThanOrEqualTo(480);
        reasons.GetValueOrDefault(NoTradeReason.EntryThroughStop).ShouldBeGreaterThanOrEqualTo(320);
        reasons.GetValueOrDefault(NoTradeReason.NoFillBar).ShouldBeGreaterThanOrEqualTo(100);
        reasons.GetValueOrDefault(NoTradeReason.PositionOpen).ShouldBeGreaterThanOrEqualTo(860);
        trades.ShouldBeGreaterThanOrEqualTo(1_000);
        overnight.ShouldBe(trades);
        candidates.ShouldBeGreaterThan(trades);
    }

    [Fact]
    public void Every_trade_is_dated_on_the_session_after_its_decision_by_the_calendar_given_and_fills_within_five_minutes_of_the_open()
    {
        var checkedTrades = 0;
        foreach (var series in SeededDaily.All)
        {
            foreach (var outcome in Gated(series, series.Bars, series.Candidates).Where(o => o.Walk.Trade is not null))
            {
                var trade = outcome.Walk.Trade!;
                var decisionDate = DailySeries.DecisionDate(outcome.Candidate);
                var expected = decisionDate.AddDays(1);
                while (!SwingOracle.IsSession(expected, series.Given))
                {
                    expected = expected.AddDays(1);
                }

                trade.TradeDate.ShouldBe(expected, "seed " + series.Seed);
                var open = Day.Epoch(expected, series.Given?.SessionOf(expected)?.FromMinute ?? SessionClock.OpenMinute);
                (trade.EntryEpochSeconds - open).ShouldBeInRange(0L, 300L, "seed " + series.Seed);
                trade.DecisionEpochSeconds.ShouldBe(outcome.Candidate.DecisionEpochSeconds);
                trade.Key.ShouldBe($"SWING_DAILY_TEST|TEST|Buy|{decisionDate:yyyy-MM-dd}", "seed " + series.Seed);
                checkedTrades++;
            }
        }

        checkedTrades.ShouldBeGreaterThanOrEqualTo(1_000);
    }

    [Fact]
    public void The_keys_of_the_trades_of_one_series_are_unique()
    {
        foreach (var series in SeededDaily.All)
        {
            var keys = Gated(series, series.Bars, series.Candidates).Select(o => o.Walk.Trade?.Key).OfType<string>().ToList();

            keys.Distinct().Count().ShouldBe(keys.Count, "seed " + series.Seed);
        }
    }

    // ---- criterion 3: the prefix property for the last day of a window

    private static string EntrySide(SimTrade t) => $"{t.TradeDate:yyyy-MM-dd} {t.DecisionEpochSeconds} {t.EntryEpochSeconds} {t.EntryPrice:R} {t.EntryFilledPrice:R} {t.EntryFill} {t.StopPrice:R} {t.TargetPrice:R}";

    private static (int Violations, int Compared, int PendingSeen) Prefixes(Func<NoTradeReason?, NoTradeReason?> reading, int cutsPerSeries)
    {
        var violations = 0;
        var compared = 0;
        var pendingSeen = 0;
        foreach (var series in SeededDaily.All)
        {
            var full = Gated(series, series.Bars, series.Candidates);
            var rng = new SeededSeries((ulong)series.Seed * 31UL);
            var cuts = Enumerable.Range(0, cutsPerSeries).Select(_ => 1 + rng.Int(series.Dates.Count - 1)).Distinct().Select(day => series.Prefix(day).Count).ToList();
            // Mid-session cuts: the list ends anywhere, in the middle of the fill window of an order included.
            cuts.AddRange(Enumerable.Range(0, cutsPerSeries).Select(_ => 376 + rng.Int(series.Bars.Count - 376)));
            foreach (var count in cuts)
            {
                var bars = series.Bars.Take(count).ToArray();
                var inside = series.Candidates.Where(c => c.DecisionBarIndex < count).ToList();
                var shortRun = Gated(series, bars, inside);
                var violated = false;
                for (var i = 0; i < inside.Count; i++)
                {
                    var (s, l) = (shortRun[i].Walk, full[i].Walk);
                    compared++;
                    var reason = reading(s.NoTrade);
                    if (reason == NoTradeReason.OrderPendingAtEnd)
                    {
                        pendingSeen++;
                    }

                    if (l.Trade is { } longTrade)
                    {
                        // A trade of the longer run: in the shorter one a trade (the same one when it closed), an open trade or a pending order.
                        if (s.Trade is { } shortTrade)
                        {
                            violated |= shortTrade.ExitReason == ExitReason.EndOfData
                                ? EntrySide(shortTrade) != EntrySide(longTrade) || longTrade.ExitEpochSeconds < shortTrade.ExitEpochSeconds
                                : OutcomeFormatter.Render(XDrishti.Domain.Common.Result.Success(s)) != OutcomeFormatter.Render(XDrishti.Domain.Common.Result.Success(l));
                        }
                        else
                        {
                            violated |= reason != NoTradeReason.OrderPendingAtEnd;
                        }
                    }
                    else if (s.Trade is not null)
                    {
                        violated = true;   // a trade of the shorter run is a trade of the longer one
                    }
                    else if (reading(s.NoTrade) == NoTradeReason.PositionOpen)
                    {
                        violated |= l.NoTrade != NoTradeReason.PositionOpen;
                    }
                    else if (s.NoTrade is NoTradeReason.NoFillBar or NoTradeReason.LimitNotReached or NoTradeReason.EntryThroughStop)
                    {
                        violated |= l.NoTrade != s.NoTrade;
                    }

                    if (reason == NoTradeReason.OrderPendingAtEnd)
                    {
                        violated |= inside[i] != inside[^1];
                    }
                }

                if (violated)
                {
                    violations++;
                }
            }
        }

        return (violations, compared, pendingSeen);
    }

    [Fact]
    public void A_trade_of_the_longer_run_decided_on_or_before_the_end_of_the_shorter_one_is_a_trade_an_open_trade_or_a_pending_order_in_the_shorter_run()
    {
        var (violations, compared, pending) = Prefixes(reason => reason, 3);

        violations.ShouldBe(0);
        compared.ShouldBeGreaterThan(8_000, "measured 10,409 comparisons over 3,000 cuts, half of them in the middle of a session");
        pending.ShouldBeGreaterThan(700, "the cut series end on a decision day: the pending order is what the property is about (measured 899)");
    }

    [Fact]
    public void Control_a_walker_that_reads_the_pending_order_as_a_no_fill_bar_violates_the_property_on_many_series()
    {
        // The plain E9 of the first draft: no minute after the candidate's own is a NoFillBar. Under that reading the shorter run says NoFillBar where the longer one has a trade.
        var (violations, _, _) = Prefixes(reason => reason == NoTradeReason.OrderPendingAtEnd ? NoTradeReason.NoFillBar : reason, 3);

        violations.ShouldBeGreaterThanOrEqualTo(300, "the control must fail on many cuts or the property proves nothing (measured 452)");
    }

    // ---- criterion 11: look-ahead

    private static string Entry(WalkOutcome o) => o.Trade is { } t
        ? $"trade {t.TradeDate:yyyy-MM-dd} {t.DecisionEpochSeconds} {t.EntryEpochSeconds} {t.EntryPrice:R} {t.EntryFilledPrice:R} {t.EntryFill} {t.StopPrice:R} {t.TargetPrice:R} {o.EntryAfterMissingMinutes}"
        : $"no-trade {o.NoTrade} {o.BarsExamined} {o.ZeroVolumeMinutes}";

    /// <summary>The test's own settling index: the minute that proves where the order ended, read from the list by the oracle's own scan (the fill minute, or the first minute of a later date or past the five minutes); -1 for a pending order.</summary>
    private static int Settled(DailySeries series, IReadOnlyList<Bar> bars, EntryCandidate candidate)
    {
        var decisionDate = DailySeries.DecisionDate(candidate);
        var fillDate = decisionDate.AddDays(1);
        while (!SwingOracle.IsSession(fillDate, series.Given))
        {
            fillDate = fillDate.AddDays(1);
        }

        var open = Day.Epoch(fillDate, series.Given?.SessionOf(fillDate)?.FromMinute ?? SessionClock.OpenMinute);
        for (var j = candidate.DecisionBarIndex + 1; j < bars.Count; j++)
        {
            var date = SessionClock.DateOf(bars[j].EpochSeconds);
            if (date == decisionDate)
            {
                continue;
            }

            if (date != fillDate || bars[j].EpochSeconds - open > 300)
            {
                return j;
            }

            if (bars[j].Volume > 0)
            {
                return j;
            }
        }

        return -1;
    }

    private static Bar[] Extreme(IReadOnlyList<Bar> bars, int after)
    {
        var copy = bars.ToArray();
        for (var j = after + 1; j < copy.Length; j++)
        {
            copy[j] = copy[j] with { Open = 1e7 + j, High = 2e7 + j, Low = 1e-3, Close = 1e7 + j };
        }

        return copy;
    }

    private static (int Failed, int Compared) LookAhead(Func<DailySeries, IReadOnlyList<Bar>, EntryCandidate, WalkOutcome> walk)
    {
        var failed = 0;
        var compared = 0;
        foreach (var series in SeededDaily.All)
        {
            var seriesFailed = false;
            foreach (var candidate in series.Candidates)
            {
                var k = Settled(series, series.Bars, candidate);
                if (k < 0)
                {
                    continue;
                }

                var full = Entry(walk(series, series.Bars, candidate));
                var cut = Entry(walk(series, [.. series.Bars.Take(k + 1)], candidate));
                var extreme = Entry(walk(series, Extreme(series.Bars, k), candidate));
                compared++;
                seriesFailed |= full != cut || full != extreme;
            }

            if (seriesFailed)
            {
                failed++;
            }
        }

        return (failed, compared);
    }

    private static WalkOutcome Plain(DailySeries series, IReadOnlyList<Bar> bars, EntryCandidate candidate)
    {
        var result = TradeWalker.Walk(candidate, bars, series.Rules(Fills));
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    /// <summary>The control: a wrapper that lets the limit use the high of the fill session (a look-ahead): the trigger close it hands the walker is the highest high of the fill date in the list it is given.</summary>
    private static WalkOutcome LimitFromTheFillSessionsHigh(DailySeries series, IReadOnlyList<Bar> bars, EntryCandidate candidate)
    {
        var decisionDate = DailySeries.DecisionDate(candidate);
        var high = bars.Where(b => SessionClock.DateOf(b.EpochSeconds) > decisionDate).Take(375).Select(b => b.High).DefaultIfEmpty(candidate.TriggerBar.Close).Max();
        var peeking = candidate with { TriggerBar = candidate.TriggerBar with { Close = Math.Max(candidate.TriggerBar.Close, high) } };
        return Plain(series, bars, peeking);
    }

    [Fact]
    public void The_entry_of_an_overnight_order_is_settled_by_the_minutes_up_to_the_fill_minute_and_by_nothing_after_it_over_500_series()
    {
        var (failed, compared) = LookAhead(Plain);

        failed.ShouldBe(0);
        compared.ShouldBeGreaterThan(3_000, "measured 3,263 settled candidates");
    }

    [Fact]
    public void Control_a_limit_taken_from_the_high_of_the_fill_session_fails_on_many_series()
    {
        var (failed, _) = LookAhead(LimitFromTheFillSessionsHigh);

        failed.ShouldBeGreaterThanOrEqualTo(50, "the control must fail on at least ten seeds (spec) while the plain walker reports none; measured 114 of 500");
    }
}
