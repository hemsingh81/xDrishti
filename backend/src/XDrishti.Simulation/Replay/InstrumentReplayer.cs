using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Scanning;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Replay;

/// <summary>
/// Replays a prepared strategy over the stored bars of one instrument, with a result that does not depend on the window that was asked for, and says what it did (spec P3-04, ADR 0037 amendment 2). Pure and synchronous: values in,
/// values out, no clock, no random source, no I/O.
/// <list type="number">
/// <item><b>It starts at the first stored session, whatever <c>From</c> is.</b> The raw bars run from the first stored bar to the end of <c>To</c>; the replay scans and counts from the first session that runs, and only the <i>trades</i> are cut
/// to <c>[From, To]</c> (a trade decided before <c>From</c> is dropped and counted; an order that rests overnight belongs to the day of its decision, ADR 0043). So an <c>ema(10)</c> is seeded by the first stored bar, not by the first bar of the window.</item>
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

    /// <summary>
    /// Replays a prepared comparison over the instrument (P4-05): one scan and one replay record for each candidate, each as the replay of that candidate alone would give it except that a swing side is not gated (D4); the number of entries the gate
    /// would have skipped is in <see cref="ComparisonReplay.GatedBlocked"/>. A plain strategy gives one member.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">As <see cref="Replay"/>.</exception>
    /// <exception cref="InvalidOperationException">As <see cref="Replay"/>.</exception>
    public static ComparisonReplay ReplayAll(PreparedStrategy strategy, InstrumentReplayInput input) => ReplayMembers(strategy, input, null);

    /// <summary>
    /// The replay of <see cref="Replay"/> and, for each request whose decision is a candidate of the scan (same decision instant and side), the conditions of the side at that decision with the values they read, taken from the trace the scanner
    /// made when it decided (P3-04d, D2): the same frame and the same bar list as the trade, no second evaluation. A request that matches no candidate has no entry.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static ExplainResult Explain(PreparedStrategy strategy, InstrumentReplayInput input, IReadOnlyList<ExplainRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(requests);
        var found = new Dictionary<string, TradeExplanation>(StringComparer.Ordinal);
        var replay = ReplayMembers(strategy, input, (_, result) =>
        {
            foreach (var outcome in result.Outcomes)
            {
                var candidate = outcome.Candidate;
                foreach (var request in requests)
                {
                    if (request.DecisionEpochSeconds == candidate.DecisionEpochSeconds && request.Side == candidate.Side && !found.ContainsKey(request.TradeKey)
                        && strategy.Simulator.CompiledOf(candidate.Side) is { } side && ExplanationOf(side, candidate) is { } explained)
                    {
                        found[request.TradeKey] = explained;
                    }
                }
            }
        }).Members[0];   // a plain strategy has one member; for a comparison the explanation is of the first candidate, whose entries every member shares (one scan)
        return new ExplainResult(replay, found);
    }

    /// <summary>The conditions of the candidate's side with the values the scanner recorded. The recorded truths are those the scanner folded into the decision; the lists are folded again by the cards handler with the same Kleene rules as <c>BoundSide.Evaluate</c>.</summary>
    private static TradeExplanation? ExplanationOf(CompiledSide side, EntryCandidate candidate)
    {
        var items = candidate.Trace.Items;
        if (items.Count != side.Items.Count)
        {
            return null;
        }

        var sections = new List<ConditionSectionTrace>(3);
        var at = 0;
        foreach (var (name, list) in new (string, CompiledList?)[] { ("setup", side.Setup), ("trigger", side.Trigger), ("invalidate", side.Invalidate) })
        {
            if (list is null)
            {
                continue;
            }

            var traced = new List<ConditionItemTrace>(list.Items.Count);
            foreach (var item in list.Items)
            {
                var trace = items[at++];
                var condition = item.Condition;
                var reads = new List<FeatureReadTrace>(condition.Reads.Count);
                foreach (var read in condition.Reads)
                {
                    // A read the trace cannot answer is a defect of the engine (a lag past the history the condition keeps), never "not ready": it throws.
                    if (read.FeatureIndex < 0 || read.FeatureIndex >= trace.Values.Count || read.Lag >= trace.Values[read.FeatureIndex].Lags.Count)
                    {
                        throw new InvalidOperationException("The condition `" + condition.Source + "` reads " + read.Name + " at lag " + read.Lag.ToString(CultureInfo.InvariantCulture) + ", which the decision trace does not hold.");
                    }

                    var recorded = trace.Values[read.FeatureIndex].Lags[read.Lag];
                    var value = double.IsNaN(recorded) ? (double?)null : recorded;
                    reads.Add(new FeatureReadTrace(read.Name, read.Timeframe, read.Lag, value));
                }

                var truth = trace.Truth switch { TraceTruth.True => TruthState.True, TraceTruth.False => TruthState.False, _ => TruthState.Unknown };
                traced.Add(new ConditionItemTrace(item.Path, item.Condition.Source, truth, reads, condition.ReadsTruncated));
            }

            sections.Add(new ConditionSectionTrace(name, list.Mode == ListMode.Any ? "any" : "all", traced));
        }

        return new TradeExplanation(candidate.DecisionEpochSeconds, candidate.TriggerTimeframe, sections);
    }

    /// <summary>The replay with an observer that is given the list and the result of every call of <see cref="Simulator.Run"/>, for the tests of the decision index (criterion 1b).</summary>
    internal static InstrumentReplay ReplayCore(PreparedStrategy strategy, InstrumentReplayInput input, Action<IReadOnlyList<Bar>, SimulationResult>? observer) => ReplayMembers(strategy, input, observer).Members[0];

    /// <summary>The replay of every candidate of the strategy, with the observer of <see cref="ReplayCore"/> given the result of each candidate's call.</summary>
    internal static ComparisonReplay ReplayMembers(PreparedStrategy strategy, InstrumentReplayInput input, Action<IReadOnlyList<Bar>, SimulationResult>? observer)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(input);
        var count = strategy.Members.Count;
        var raw = Validated(input);
        var hash = BarStreamHash.Compute(raw);
        if (raw.Count == 0)
        {
            return Same(new InstrumentReplay(Record(input, Facts.NoData(hash), SkipReason.NoBars, null, Scanned.None), [], []), count);
        }

        var scan = SessionScan.Classify(raw, input.Calendar, input.Options);
        var first = SessionClock.DateOf(raw[0].EpochSeconds);
        var facts = new Facts(first, SessionClock.DateOf(raw[^1].EpochSeconds), raw.Count, scan.BarsFed, scan.BarsExcluded, scan.BarsOutsideSession, hash, AdjustmentsOf(input, first), scan);
        if (input.PresetSkip is { } preset)
        {
            return Same(new InstrumentReplay(Record(input, facts, preset, input.SkipDetail, Scanned.None), [], []), count);
        }

        if (strategy.ReadsContext && ContextReader.Of(input.ContextDays) is null)
        {
            // The market context is missing or refused: the strategy cannot be evaluated on this instrument, and an empty result with no reason would look like "no signals" (P3-lift, ADR 0145, D3).
            var cause = input.ContextDays is { Count: > 0 } ? "context_invalid" : (input.ContextNote ?? "no_context");
            return Same(new InstrumentReplay(Record(input, facts, SkipReason.ContextUnavailable, cause, Scanned.None), [], []), count);
        }

        if (scan.Ran.Count == 0)
        {
            return Same(new InstrumentReplay(Record(input, facts, SkipReason.NoRunnableSessions, null, Scanned.None), [], []), count);
        }

        var members = Scan(strategy, input, scan, observer);
        var replays = new List<InstrumentReplay>(count);
        foreach (var (scanned, trades, openAtEnd, _) in members)
        {
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
            replays.Add(skip is null
                ? new InstrumentReplay(Record(input, facts, null, null, scanned), trades, openAtEnd)
                : new InstrumentReplay(Record(input, facts, skip, null, scanned), [], []));
        }

        return new ComparisonReplay(replays.AsReadOnly(), members.Select(m => m.Gated).ToList().AsReadOnly());
    }

    private static ComparisonReplay Same(InstrumentReplay replay, int count) =>
        new(Enumerable.Repeat(replay, count).ToList().AsReadOnly(), Enumerable.Repeat(0L, count).ToList().AsReadOnly());

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

    private static List<(Scanned Scanned, List<SimTrade> Trades, List<SimTrade> OpenAtEnd, long Gated)> Scan(PreparedStrategy strategy, InstrumentReplayInput input, SessionScan scan, Action<IReadOnlyList<Bar>, SimulationResult>? observer)
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
        var tallies = strategy.Members.Select(_ => new Tally()).ToList();
        var holdsOvernight = strategy.Simulator.HoldsOvernight;
        var segment = holdsOvernight ? CostSegment.Delivery : CostSegment.IntradayEquity;
        for (var s = 0; s < segmentStarts.Count; s++)
        {
            var firstSession = segmentStarts[s];
            var endSession = s + 1 < segmentStarts.Count ? segmentStarts[s + 1] : ran.Count;
            var from = starts[firstSession];
            var count = starts[endSession] - from;
            IReadOnlyList<Bar> list = firstSession == 0 && endSession == ran.Count ? fed : new BarSegment(fed, from, count);
            var segmentRun = RunSegment(strategy, instrument, list, input);
            var results = segmentRun.Members;
            DateOnly? flaggedDate = s == 0 ? null : ran[firstSession].Date;
            for (var m = 0; m < results.Count; m++)
            {
                var result = results[m];
                var tally = tallies[m];
                observer?.Invoke(list, result);
                if (strategy.Comparison is not null && holdsOvernight)
                {
                    // D4: the comparison walk is not gated; what the gate would have skipped under this exit is counted, for the entries decided in the window
                    tally.Gated += segmentRun.BlockedByGate(m, c => InWindow(c, input, flaggedDate));
                }

                foreach (var outcome in result.Outcomes)
                {
                    // The date of a candidate is the one of its decision (ADR 0043, D6): an order that rests overnight is filled on the next session, and the window cuts by the day of the decision.
                    var date = SessionClock.DateOf(outcome.Candidate.DecisionEpochSeconds - 60);
                    if (date >= input.From && date <= input.To)
                    {
                        tally.CandidatesInWindow++;
                    }

                    if (flaggedDate is { } day && date == day)
                    {
                        tally.Suppressed++;
                    }
                    else if (outcome.Walk.Trade is { } trade)
                    {
                        if (date < input.From)
                        {
                            tally.BeforeWindow++;
                        }
                        else if (trade.ExitReason == ExitReason.EndOfData && holdsOvernight && s + 1 < segmentStarts.Count)
                        {
                            // D13: a swing position that was still open when the segment ended, and a flagged day follows, cannot be followed across the discontinuity: excluded and counted (not open at the end, not a trade).
                            tally.ExcludedAcross++;
                            var nextDay = ran[segmentStarts[s + 1]].Date;
                            tally.ExcludedAtDay[nextDay] = tally.ExcludedAtDay.GetValueOrDefault(nextDay) + 1;
                        }
                        else if (Costed(trade, input.Costs, segment, input.Symbol, ref tally.SkippedQuantity, ref tally.SkippedCost) is { } kept)
                        {
                            // ADR 0042, D1: costing comes after the window cut and the exclusion, and before the fates are counted; a skipped trade has left the lists.
                            if (kept.Legs is { } legs && kept.Costing?.LegCharges is { } charged)
                            {
                                tally.LegsMerged += legs.Count - charged.Count;
                            }

                            if (kept.ExitReason == ExitReason.EndOfData)
                            {
                                tally.OpenAtEnd.Add(kept);
                            }
                            else
                            {
                                tally.Trades.Add(kept);
                            }
                        }
                    }
                    else
                    {
                        tally.NoTrades = tally.NoTrades.Add(NoTradeFate(outcome.Walk.NoTrade, input.Symbol));
                    }
                }

                tally.Walk = tally.Walk.Add(result.Walk);
            }

            // one scan: the counters are the same list in every member's result
            for (var side = 0; side < results[0].Scans.Count; side++)
            {
                if (scans.Count <= side)
                {
                    scans.Add(results[0].Scans[side]);
                }
                else
                {
                    scans[side] = scans[side].Add(results[0].Scans[side]);
                }
            }
        }

        var done = new List<(Scanned Scanned, List<SimTrade> Trades, List<SimTrade> OpenAtEnd, long Gated)>(tallies.Count);
        foreach (var tally in tallies)
        {
            var fates = tally.NoTrades with { EntriesSuppressed = tally.Suppressed, TradesBeforeWindow = tally.BeforeWindow, OpenAtEnd = tally.OpenAtEnd.Count, Trades = tally.Trades.Count, ExcludedAcrossDiscontinuity = tally.ExcludedAcross, SkippedQuantity = tally.SkippedQuantity, SkippedCost = tally.SkippedCost };
            var notes = flagged.Select(n => tally.ExcludedAtDay.TryGetValue(n.Date, out var count) ? n with { ExcludedPositions = count } : n).ToList();
            var scanned = new Scanned(ran[0].Date, new DateSpan(ran[0].Date, ran[^1].Date), scans.AsReadOnly(), tally.Walk with { LegsMerged = tally.LegsMerged }, fates, notes.AsReadOnly(), tally.CandidatesInWindow, tally.Trades.Count + tally.OpenAtEnd.Count);
            done.Add((scanned, [.. tally.Trades.OrderBy(t => t.EntryEpochSeconds).ThenBy(t => t.Side)], [.. tally.OpenAtEnd.OrderBy(t => t.EntryEpochSeconds).ThenBy(t => t.Side)], tally.Gated));
        }

        return done;
    }

    /// <summary>A candidate decided inside the window, on a day that is not the flagged one.</summary>
    private static bool InWindow(EntryCandidate candidate, InstrumentReplayInput input, DateOnly? flaggedDate)
    {
        var date = SessionClock.DateOf(candidate.DecisionEpochSeconds - 60);
        return date >= input.From && date <= input.To && (flaggedDate is not { } day || date != day);
    }

    /// <summary>One call of the simulator on a segment: the results of the one candidate of a plain strategy, or of every candidate of a comparison (one scan).</summary>
    private static ComparisonResult RunSegment(PreparedStrategy strategy, InstrumentRef instrument, IReadOnlyList<Bar> list, InstrumentReplayInput input)
    {
        var context = strategy.ReadsContext ? ContextReader.Of(input.ContextDays) : null;   // the replay has already skipped an instrument whose strategy has no context
        if (strategy.Comparison is { } comparison)
        {
            var many = comparison.Run(instrument, list, input.Tick!, input.Calendar, QuantityOf(input.Costs), context);
            return many.IsFailure
                ? throw new InvalidOperationException("The simulation of instrument " + input.Symbol + " failed: " + many.Error.Code + ": " + many.Error.Message)
                : many.Value;
        }

        var run = strategy.Simulator.Run(instrument, list, input.Tick!, input.Calendar, QuantityOf(input.Costs), context);
        return run.IsFailure
            ? throw new InvalidOperationException("The simulation of instrument " + input.Symbol + " failed: " + run.Error.Code + ": " + run.Error.Message)
            : new ComparisonResult([run.Value], run.Value.Scans);
    }

    /// <summary>What the replay of one candidate counts across the segments of an instrument.</summary>
    private sealed class Tally
    {
        public readonly List<SimTrade> Trades = [];
        public readonly List<SimTrade> OpenAtEnd = [];
        public readonly Dictionary<DateOnly, int> ExcludedAtDay = [];
        public WalkCounters Walk = WalkCounters.Empty;
        public RunFates NoTrades = RunFates.Empty;
        public long Suppressed;
        public long BeforeWindow;
        public long ExcludedAcross;
        public long CandidatesInWindow;
        public long SkippedQuantity;
        public long SkippedCost;
        public long LegsMerged;
        public long Gated;
    }

    /// <summary>The full size of ADR 0042 as the walker of a pyramid needs it (P4-04, D3): the sizing of the cost context for an exact entry and an initial risk, 0 when the input is not sizable; null for a gross run.</summary>
    private static Func<decimal, decimal, long>? QuantityOf(CostContext? context) =>
        context is null ? null : (entry, risk) => Sizing.Quantity(context.Capital, context.Profile, entry, risk) is { IsSuccess: true } sized ? sized.Value : 0L;

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
        NoTradeReason.OrderPendingAtEnd => RunFates.Empty with { OrderPendingAtEnd = 1 },
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
