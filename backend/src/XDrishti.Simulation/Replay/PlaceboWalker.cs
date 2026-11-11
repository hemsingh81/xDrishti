using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Replay;

/// <summary>
/// The placebo walk of one instrument (spec CF-07, D2 and D3; ADR 0209). For each real signal of the plan and each of the 20 fixed shapes of <see cref="PlaceboShapes"/> whose placement is on this instrument it forces an entry through <see cref="EntryOverride"/>
/// at the shifted decision minute and walks it with the rules of the strategy: the placement's side, the real signal's risk as a fraction of the trigger close, its fixed target multiple, the strategy's square-off, fill rules and tick, then costs it with this
/// instrument's sheet in the real trade's segment (<see cref="TradeCostApplier"/>). A pair that makes no trade (no bar at the shifted minute, no such session, the limit not reached, the entry through the stop, a cost above the maximum) is an outcome with no
/// net R and its reason, not a number; the caller counts what was placed.
/// <para>
/// <b>Reads.</b> Only sessions of this instrument on or before <see cref="InstrumentReplayInput.To"/>, adjusted on a copy as the replay adjusts them, so a window cut to the development block (CF-06) cannot be left; a forced walk reads strictly forward from its own
/// minute. The shapes only move forward in minutes (an earlier entry would be look-ahead). Deterministic: no random source, and the order in which instruments are walked changes no outcome.
/// </para>
/// <para>
/// <b>What it covers.</b> Intraday strategies with a fixed reward-to-risk exit, as <see cref="RandomEntryWalker"/>. A swing strategy, an instrument the replay would skip, a gross run and a session without bars are outcomes with a reason.
/// </para>
/// </summary>
public static class PlaceboWalker
{
    /// <summary>Walks the (signal, shape) pairs that are placed on this instrument. <paramref name="done"/> is told 1 after each pair; a cancel is seen before each pair.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="OperationCanceledException">The token is cancelled.</exception>
    public static PlaceboWalkResult Walk(PreparedStrategy strategy, InstrumentReplayInput input, PlaceboWalkPlan plan, IProgress<int>? done, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(plan);
        var pairs = PairsOn(plan, input.InstrumentId);
        if (pairs.Count == 0)
        {
            return new PlaceboWalkResult([]);
        }

        PlaceboWalkResult All(string reason) => new([.. pairs.Select(p => new PlaceboOutcome(p.Signal.Id, p.Shape, null, reason))]);
        if (strategy.HoldsOvernight)
        {
            return All("a swing strategy holds positions over sessions: the placebo set covers intraday strategies only");
        }

        if (input.PresetSkip is { } skip)
        {
            return All("the instrument is skipped by the replay: " + skip.ToCode());
        }

        if (input.Tick is null)
        {
            return All("the instrument has no usable tick size");
        }

        if (input.Costs is null)
        {
            return All("the run is gross: there is no charge sheet to cost a placebo trade with");
        }

        var dates = SessionDates(input);
        var targets = pairs.Select(p => TargetDate(p, dates)).ToList();
        var sessions = RandomEntryWalker.SessionsOf(input, [.. targets.Where(t => t is not null).Select(t => t!.Value)]);
        var instrument = new InstrumentRef(input.InstrumentId, input.Symbol);
        var outcomes = new List<PlaceboOutcome>(pairs.Count);
        for (var i = 0; i < pairs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcomes.Add(One(strategy, input, instrument, pairs[i], targets[i], sessions));
            done?.Report(1);
        }

        return new PlaceboWalkResult(outcomes);
    }

    private sealed record Pair(PlaceboSignal Signal, int Shape, PlaceboPlacement Placement);

    private static List<Pair> PairsOn(PlaceboWalkPlan plan, Guid instrumentId)
    {
        var pairs = new List<Pair>();
        foreach (var signal in plan.Signals)
        {
            for (var s = 0; s < PlaceboShapes.All.Count; s++)
            {
                if (PlaceboShapes.Place(PlaceboShapes.All[s], signal, plan.RollOrder) is { } placement && placement.InstrumentId == instrumentId)
                {
                    pairs.Add(new Pair(signal, s, placement));
                }
            }
        }

        return pairs;
    }

    /// <summary>The distinct India dates of the instrument's stored minutes on or before the end of the window, ascending.</summary>
    private static List<DateOnly> SessionDates(InstrumentReplayInput input)
    {
        var dates = new List<DateOnly>();
        foreach (var bar in input.RawBars)
        {
            var date = SessionClock.DateOf(bar.EpochSeconds);
            if (date <= input.To && (dates.Count == 0 || dates[^1] != date))
            {
                dates.Add(date);
            }
        }

        return dates;
    }

    /// <summary>The session the pair is walked on: the real one, or the one that many sessions of this instrument away; null when there is no such session.</summary>
    private static DateOnly? TargetDate(Pair pair, List<DateOnly> dates)
    {
        if (pair.Placement.SessionOffset == 0)
        {
            return pair.Signal.Date;
        }

        var at = dates.BinarySearch(pair.Signal.Date);
        var to = at + pair.Placement.SessionOffset;
        return at < 0 || to < 0 || to >= dates.Count ? null : dates[to];
    }

    private static PlaceboOutcome One(PreparedStrategy strategy, InstrumentReplayInput input, InstrumentRef instrument, Pair pair, DateOnly? target, Dictionary<DateOnly, Bar[]> sessions)
    {
        PlaceboOutcome None(string reason) => new(pair.Signal.Id, pair.Shape, null, reason);
        var signal = pair.Signal;
        if (input.Tick is not { } tick || input.Costs is not { } costs)
        {
            return None("the instrument or the run cannot be restated (no tick or no charge sheet)");
        }

        if (target is not { } date || !sessions.TryGetValue(date, out var bars) || bars.Length == 0)
        {
            return None("no bars are stored for the session the shape asks for");
        }

        if (strategy.Simulator.CompiledOf(signal.Side) is not { TimeRules.SquareOffMinute: { } squareOff })
        {
            return None("the strategy has no square-off time");
        }

        if (!(signal.RiskFraction > 0 && double.IsFinite(signal.RiskFraction) && signal.RiskFraction < 1 && signal.RewardRisk > 0 && double.IsFinite(signal.RewardRisk)))
        {
            return None("the real trade has no usable risk or target");
        }

        // the decision minute is the one that closed the real evaluation bar, moved forward by the shape; the same clock time on another session
        var clock = SessionClock.MinuteOfDay(signal.DecisionEpochSeconds - 60) + pair.Placement.MinuteOffset;
        var index = -1;
        for (var i = 0; i < bars.Length; i++)
        {
            if (SessionClock.MinuteOfDay(bars[i].EpochSeconds) == clock)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return None("the session has no minute at the shifted decision time");
        }

        var side = pair.Placement.Side;
        var rules = new WalkRules(signal.RewardRisk, squareOff, strategy.Fills, tick, TierOn: input.TierOn, TurnoverOn: input.TurnoverOn, MemberOn: input.MemberOn);   // BK-11b: a placebo on a session where the instrument was not a member is no trade, as a dated run would take none there
        var candidate = EntryOverride.Forced(instrument, strategy.Record.Id, bars, index, side, EntryOverride.StopAt(side, bars[index].Close, signal.RiskFraction));
        var walked = EntryOverride.Walk(candidate, bars, rules);
        if (walked.IsFailure)
        {
            return None("the walk was refused: " + walked.Error.Code);
        }

        if (walked.Value.Trade is not { } trade)
        {
            return None("no trade: " + walked.Value.NoTrade);
        }

        var costed = TradeCostApplier.Apply(trade, costs, signal.Segment);
        return costed.IsSuccess && costed.Value.Trade is { Costing: { } costing }
            ? new PlaceboOutcome(signal.Id, pair.Shape, costing.RNet, null)
            : None("no trade: the charge sheet refused it");
    }
}
