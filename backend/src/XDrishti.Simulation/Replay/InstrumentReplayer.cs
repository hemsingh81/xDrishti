using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;

namespace XDrishti.Simulation.Replay;

/// <summary>
/// Replays a prepared strategy over the stored bars of one instrument, with a result that does not depend on the window that was asked for, and says what it did (spec P3-04, ADR 0037 amendment 2). Pure and synchronous: values in,
/// values out, no clock, no random source, no I/O.
/// <list type="number">
/// <item><b>It starts at the first stored session, whatever <c>From</c> is.</b> The raw bars run from the first stored bar to the end of <c>To</c>; the replay scans and counts from the first session that runs, and only the <i>trades</i> are cut
/// to <c>[From, To]</c> (a trade dated before <c>From</c> is dropped and counted). So an <c>ema(10)</c> is seeded by the first stored bar, not by the first bar of the window.</item>
/// <item><b>Sessions</b> are classified (<see cref="SessionScan"/>): closed days, special sessions, short sessions and missing sessions are excluded, counted and noted; none of their bars is fed. A frame is always fed whole sessions (ADR 0035).</item>
/// <item><b>Prices</b> are adjusted on a copy (<see cref="ReplayBars"/>); the raw list and the hash of it are never touched.</item>
/// <item><b>Discontinuities</b>: on the adjusted series a day whose first open differs from the last close of the previous session that ran by more than the threshold (compared in <c>decimal</c>; exactly the threshold is not flagged) is
/// flagged, gets no entry (the outcomes of its candidates are dropped and counted) and starts a new segment: <see cref="Simulator.Run"/> is called once per segment with fresh frames, so the old price level cannot sit in an average for weeks.</item>
/// <item><b>Swing positions</b> (ADR 0040, Amendment 4 of ADR 0037): the read ends at <c>To</c>, so a position still open there is <c>EndOfData</c> and goes to <c>OpenAtEnd</c>; a swing position that is open at the end of a segment that a
/// flagged day follows cannot be followed across it and is excluded and counted (<c>ExcludedAcrossDiscontinuity</c>, and <c>ExcludedPositions</c> of the day's note), by the style of the strategy; every other candidate outcome keeps its one fate,
/// <c>PositionOpen</c> included.</item>
/// <item><b>Costs</b> (ADR 0042): when the input has a <see cref="CostContext"/>, every trade that survives the window cut and the exclusion is sized and charged by <see cref="TradeCostApplier"/>, with the segment of the strategy's style; a trade it skips leaves
/// the lists and is counted (<c>SkippedQuantity</c>, <c>SkippedCost</c>). Without a context the trades are the walker's, gross. The walker, the simulator and the scanner know nothing of costs.</item>
/// <item><b>One list.</b> The segments are windows (<see cref="BarSegment"/>) onto one list of fed bars. The scanner of a call steps the list it is given from element 0 and the walker reads that same list, so a candidate's decision
/// index is its index in it; the replay never feeds the scanner a sub-list it did not also give the walker.</item>
/// </list>
/// A caller error (a list that is not strictly ascending, a bar dated after <c>To</c>, options out of range) throws; every expected condition is a skip with a reason or a note.
/// </summary>
public static class InstrumentReplayer
{
    /// <summary>Replays the strategy over the instrument.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The options are out of range, <c>From</c> is after <c>To</c>, the preset skip is not one the runner may decide, the tick is missing without a preset skip, or a bar is dated after <c>To</c>.</exception>
    /// <exception cref="InvalidOperationException">The bars are not strictly ascending (two equal epochs or a descending pair); the message names the instrument.</exception>
    public static InstrumentReplay Replay(PreparedStrategy strategy, InstrumentReplayInput input) => ReplayCore(strategy, input, null);

    /// <summary>The replay with an observer that is given the list and the result of every call of <see cref="Simulator.Run"/>, for the tests of the decision index (criterion 1b).</summary>
    internal static InstrumentReplay ReplayCore(PreparedStrategy strategy, InstrumentReplayInput input, Action<IReadOnlyList<Bar>, SimulationResult>? observer)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(input);
        var raw = Validated(input);
        var hash = BarStreamHash.Compute(raw);
        if (raw.Count == 0)
        {
            return new InstrumentReplay(Record(input, Facts.NoData(hash), SkipReason.NoBars, null, Scanned.None), [], []);
        }

        var scan = SessionScan.Classify(raw, input.Calendar, input.Options);
        var first = SessionClock.DateOf(raw[0].EpochSeconds);
        var facts = new Facts(first, SessionClock.DateOf(raw[^1].EpochSeconds), raw.Count, scan.BarsFed, scan.BarsExcluded, scan.BarsOutsideSession, hash, AdjustmentsOf(input, first), scan);
        if (input.PresetSkip is { } preset)
        {
            return new InstrumentReplay(Record(input, facts, preset, input.SkipDetail, Scanned.None), [], []);
        }

        if (scan.Ran.Count == 0)
        {
            return new InstrumentReplay(Record(input, facts, SkipReason.NoRunnableSessions, null, Scanned.None), [], []);
        }

        var (scanned, trades, openAtEnd) = Scan(strategy, input, scan, observer);
        var windowSteps = scanned.Scans.Sum(s => s.WindowSteps);
        SkipReason? skip = null;
        if (windowSteps == 0)
        {
            skip = SkipReason.NoRunnableSessions;
        }
        else if (scan.Ran[^1].Date < input.From)
        {
            skip = SkipReason.NoSessionsInWindow;
        }
        else if (scanned.Scans.All(s => s.FullyReadySteps == 0))
        {
            skip = SkipReason.FeaturesNotReady;
        }

        // A skipped instrument keeps its data facts and its counters and has no trades.
        return skip is null
            ? new InstrumentReplay(Record(input, facts, null, null, scanned), trades, openAtEnd)
            : new InstrumentReplay(Record(input, facts, skip, null, scanned), [], []);
    }

    private static IReadOnlyList<Bar> Validated(InstrumentReplayInput input)
    {
        ArgumentNullException.ThrowIfNull(input.RawBars);
        ArgumentNullException.ThrowIfNull(input.Factors);
        ArgumentNullException.ThrowIfNull(input.Calendar);
        ArgumentNullException.ThrowIfNull(input.Options);
        ArgumentNullException.ThrowIfNull(input.Symbol);
        if (input.Options.Problem() is { } problem)
        {
            throw new ArgumentException(problem, nameof(input));
        }

        if (input.From > input.To)
        {
            throw new ArgumentException("From " + input.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " is after To " + input.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".", nameof(input));
        }

        if (input.PresetSkip is { } preset && preset is not (SkipReason.CsvAdjustedTwice or SkipReason.TickSizeInvalid))
        {
            throw new ArgumentException("A preset skip is one the runner decides, `csv_adjusted_twice` or `tick_size_invalid`, not `" + preset.ToCode() + "`.", nameof(input));
        }

        if (input.Tick is null && input.PresetSkip is null)
        {
            throw new ArgumentException("The tick is absent only with a preset skip (nothing is scanned then, so no fill needs it).", nameof(input));
        }

        var raw = input.RawBars;
        for (var i = 1; i < raw.Count; i++)
        {
            if (raw[i].EpochSeconds <= raw[i - 1].EpochSeconds)
            {
                throw new InvalidOperationException("The bars of instrument " + input.Symbol + " (" + input.InstrumentId.ToString("D") + ") are not strictly ascending at position " + i.ToString(CultureInfo.InvariantCulture) + ": two equal epochs or a descending pair.");
            }
        }

        if (raw.Count > 0 && SessionClock.DateOf(raw[^1].EpochSeconds) > input.To)
        {
            throw new ArgumentException("A bar of instrument " + input.Symbol + " is dated after To " + input.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ": the read ends at the end of To.", nameof(input));
        }

        return raw;
    }

    private static List<AppliedAdjustment> AdjustmentsOf(InstrumentReplayInput input, DateOnly firstBar) =>
        [.. input.Factors.ActionsAfter(firstBar).Select(a => new AppliedAdjustment(a.ExDate, a.Kind, a.Numerator, a.Denominator, a.Factor, input.CsvAdjustedThrough is { } through && a.ExDate <= through))];

    private static (Scanned Scanned, List<SimTrade> Trades, List<SimTrade> OpenAtEnd) Scan(PreparedStrategy strategy, InstrumentReplayInput input, SessionScan scan, Action<IReadOnlyList<Bar>, SimulationResult>? observer)
    {
        var bars = ReplayBars.Build(input.RawBars, scan.Ran, input.Factors);
        var fed = bars.Fed;
        var starts = bars.SessionStarts;
        var ran = scan.Ran;

        // The data notes, part two: a day whose first open is more than the threshold from the previous close of a session that ran is flagged and starts a segment.
        var flagged = new List<DiscontinuityNote>();
        var segmentStarts = new List<int> { 0 };
        for (var k = 1; k < ran.Count; k++)
        {
            var open = fed[starts[k]].Open;
            var previousClose = fed[starts[k] - 1].Close;
            if (Discontinuity.IsFlagged(open, previousClose, input.Options.DiscontinuityThreshold))
            {
                flagged.Add(new DiscontinuityNote(ran[k].Date, open / previousClose));
                segmentStarts.Add(k);
            }
        }

        var instrument = new InstrumentRef(input.InstrumentId, input.Symbol);
        var scans = new List<ScanCounters>();
        var walk = WalkCounters.Empty;
        var trades = new List<SimTrade>();
        var openAtEnd = new List<SimTrade>();
        long suppressed = 0, beforeWindow = 0, excludedAcross = 0, candidatesInWindow = 0, skippedQuantity = 0, skippedCost = 0;
        var noTrades = RunFates.Empty;
        var holdsOvernight = strategy.Simulator.HoldsOvernight;
        var segment = holdsOvernight ? CostSegment.Delivery : CostSegment.IntradayEquity;
        var excludedAtDay = new Dictionary<DateOnly, int>();
        for (var s = 0; s < segmentStarts.Count; s++)
        {
            var firstSession = segmentStarts[s];
            var endSession = s + 1 < segmentStarts.Count ? segmentStarts[s + 1] : ran.Count;
            var from = starts[firstSession];
            var count = starts[endSession] - from;
            IReadOnlyList<Bar> list = firstSession == 0 && endSession == ran.Count ? fed : new BarSegment(fed, from, count);
            var run = strategy.Simulator.Run(instrument, list, input.Tick!, input.Calendar);
            if (run.IsFailure)
            {
                throw new InvalidOperationException("The simulation of instrument " + input.Symbol + " failed: " + run.Error.Code + ": " + run.Error.Message);
            }

            var result = run.Value;
            observer?.Invoke(list, result);
            DateOnly? flaggedDate = s == 0 ? null : ran[firstSession].Date;
            foreach (var outcome in result.Outcomes)
            {
                var date = SessionClock.DateOf(outcome.Candidate.DecisionEpochSeconds - 60);
                if (date >= input.From && date <= input.To)
                {
                    candidatesInWindow++;
                }

                if (flaggedDate is { } day && date == day)
                {
                    suppressed++;
                }
                else if (outcome.Walk.Trade is { } trade)
                {
                    if (trade.TradeDate < input.From)
                    {
                        beforeWindow++;
                    }
                    else if (trade.ExitReason == ExitReason.EndOfData && holdsOvernight && s + 1 < segmentStarts.Count)
                    {
                        // D13: a swing position that was still open when the segment ended, and a flagged day follows, cannot be followed across the discontinuity: excluded and counted (not open at the end, not a trade).
                        excludedAcross++;
                        var nextDay = ran[segmentStarts[s + 1]].Date;
                        excludedAtDay[nextDay] = excludedAtDay.GetValueOrDefault(nextDay) + 1;
                    }
                    else if (Costed(trade, input.Costs, segment, input.Symbol, ref skippedQuantity, ref skippedCost) is { } kept)
                    {
                        // ADR 0042, D1: costing comes after the window cut and the exclusion, and before the fates are counted; a skipped trade has left the lists.
                        if (kept.ExitReason == ExitReason.EndOfData)
                        {
                            openAtEnd.Add(kept);
                        }
                        else
                        {
                            trades.Add(kept);
                        }
                    }
                }
                else
                {
                    noTrades = noTrades.Add(NoTradeFate(outcome.Walk.NoTrade, input.Symbol));
                }
            }

            for (var side = 0; side < result.Scans.Count; side++)
            {
                if (scans.Count <= side)
                {
                    scans.Add(result.Scans[side]);
                }
                else
                {
                    scans[side] = scans[side].Add(result.Scans[side]);
                }
            }

            walk = walk.Add(result.Walk);
        }

        var fates = noTrades with { EntriesSuppressed = suppressed, TradesBeforeWindow = beforeWindow, OpenAtEnd = openAtEnd.Count, Trades = trades.Count, ExcludedAcrossDiscontinuity = excludedAcross, SkippedQuantity = skippedQuantity, SkippedCost = skippedCost };
        var notes = flagged.Select(n => excludedAtDay.TryGetValue(n.Date, out var count) ? n with { ExcludedPositions = count } : n).ToList();
        var scanned = new Scanned(ran[0].Date, new DateSpan(ran[0].Date, ran[^1].Date), scans.AsReadOnly(), walk, fates, notes.AsReadOnly(), candidatesInWindow, trades.Count + openAtEnd.Count);
        trades = [.. trades.OrderBy(t => t.EntryEpochSeconds).ThenBy(t => t.Side)];
        openAtEnd = [.. openAtEnd.OrderBy(t => t.EntryEpochSeconds).ThenBy(t => t.Side)];
        return (scanned, trades, openAtEnd);
    }

    /// <summary>
    /// The trade as the run keeps it: the walker's trade for a gross replay, else the trade sized and charged by <see cref="TradeCostApplier"/> (ADR 0042, D1), or null when costing skipped it (the matching counter is raised). A
    /// result that is not an outcome (a date before the first schedule of the sheet, a trade with no risk) is a caller error: the request check of P3-03f prevents the first and the walker makes no trade of the second.
    /// </summary>
    private static SimTrade? Costed(SimTrade trade, CostContext? context, CostSegment segment, string symbol, ref long skippedQuantity, ref long skippedCost)
    {
        if (context is null)
        {
            return trade;
        }

        var applied = TradeCostApplier.Apply(trade, context, segment);
        if (applied.IsFailure)
        {
            throw new InvalidOperationException("The costing of a trade of instrument " + symbol + " failed: " + applied.Error.Code + ": " + applied.Error.Message);
        }

        switch (applied.Value.Skip)
        {
            case TradeSkip.QuantityBelowOne:
                skippedQuantity++;
                break;
            case TradeSkip.CostAboveMax:
                skippedCost++;
                break;
        }

        return applied.Value.Trade;
    }

    /// <summary>
    /// The fate of a candidate that the walker (or the position gate) left without a trade: a <see cref="RunFates"/> with the one field of its reason set to 1. Every member of <see cref="NoTradeReason"/> has a case, and a value that is
    /// none of them throws, so that a reason added later cannot be dropped silently (a reflection test enumerates the enum).
    /// </summary>
    internal static RunFates NoTradeFate(NoTradeReason? reason, string symbol) => reason switch
    {
        NoTradeReason.NoFillBar => RunFates.Empty with { NoFillBar = 1 },
        NoTradeReason.AfterSquareOff => RunFates.Empty with { AfterSquareOff = 1 },
        NoTradeReason.EntryThroughStop => RunFates.Empty with { EntryThroughStop = 1 },
        NoTradeReason.LimitNotReached => RunFates.Empty with { LimitNotReached = 1 },
        NoTradeReason.PositionOpen => RunFates.Empty with { PositionOpen = 1 },
        _ => throw new InvalidOperationException("A candidate outcome of instrument " + symbol + " is neither a trade nor a known no-trade reason."),
    };

    private static InstrumentRun Record(InstrumentReplayInput input, Facts facts, SkipReason? skip, string? detail, Scanned scanned) => new(
        input.InstrumentId,
        input.Symbol,
        skip,
        detail,
        facts.FirstBar,
        facts.LastBar,
        scanned.ReplayFrom,
        scanned.Cover,
        facts.BarsRead,
        facts.BarsFed,
        facts.BarsExcluded,
        facts.BarsOutsideSession,
        facts.Hash,
        input.Tick?.Size,
        input.Tick?.FromFallback ?? false,
        facts.Adjustments.AsReadOnly(),
        facts.Scan?.Counts ?? SessionCounts.Empty,
        facts.Scan?.Notes ?? [],
        facts.Scan?.NotesTruncated ?? false,
        scanned.Discontinuities,
        scanned.Scans,
        scanned.Walk,
        scanned.Fates,
        scanned.CandidatesInWindow,
        scanned.TradesInWindow);

    /// <summary>What the data said, before and whether or not a scan was made.</summary>
    private sealed record Facts(
        DateOnly? FirstBar,
        DateOnly? LastBar,
        long BarsRead,
        long BarsFed,
        long BarsExcluded,
        long BarsOutsideSession,
        string Hash,
        List<AppliedAdjustment> Adjustments,
        SessionScan? Scan)
    {
        public static Facts NoData(string hash) => new(null, null, 0, 0, 0, 0, hash, [], null);
    }

    /// <summary>What the scan counted; <see cref="None"/> for an instrument that was not scanned.</summary>
    private sealed record Scanned(
        DateOnly? ReplayFrom,
        DateSpan? Cover,
        IReadOnlyList<ScanCounters> Scans,
        WalkCounters Walk,
        RunFates Fates,
        IReadOnlyList<DiscontinuityNote> Discontinuities,
        long CandidatesInWindow,
        long TradesInWindow)
    {
        public static Scanned None { get; } = new(null, null, [], WalkCounters.Empty, RunFates.Empty, [], 0, 0);
    }
}
