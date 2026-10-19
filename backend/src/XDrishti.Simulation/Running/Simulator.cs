using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Walking;
using XDrishti.Strategies;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Running;

/// <summary>
/// The two-stage simulator (spec P3-03a, ADR 0037): compiles every listed side of a strategy once, and for each instrument scans the 1-minute bars side by side with an <see cref="EntryScanner"/> and walks every candidate with
/// the <see cref="TradeWalker"/>. The two stages share nothing but the entry candidate, which carries no exit, so the same entries can be replayed under every exit policy (P4).
/// <para>
/// <b>What it runs.</b> An intraday strategy, or a swing strategy evaluated inside the session (30m or 1h; ADR 0040), on an evaluation timeframe up to one day, entered at the close of the trigger bar, with a stop that is a level
/// (<c>or_opposite</c>, <c>swing_low</c>, <c>swing_high</c>, <c>bar_low</c> or <c>bar_high</c>, no buffer, read on the evaluation timeframe), exactly one <c>FIXED_RR</c> exit with one <c>rr</c> value, no overlay, no filter, no market context and no expiry dates. A swing side is held up to its
/// <c>max_hold_sessions</c> (the entry session counts as the first) and is gated to one open position per instrument and side (<see cref="PositionGate"/>). <see cref="Create"/> refuses everything else by name (<see cref="SimulatorCodes"/>) with a located problem, rather than
/// ignoring it: a result must be what it says it is. A side that does not compile makes <see cref="Create"/> return no simulator; a half strategy is never run.
/// </para>
/// <para>
/// A <see cref="Simulator"/> holds only compiled sides (immutable) and is safe to share between threads: every <see cref="Run"/> makes its own frames and scanners. Pure and deterministic: no clock, no I/O, no ambient state.
/// </para>
/// <para>
/// <b>Where P3-03b plugs in.</b> The fill rules enter at two places only: <c>Create</c> takes <c>FillRules? fills = null</c> and keeps it next to the sides, and <c>Run</c> takes the instrument's <c>TickSpec</c> before the calendar
/// (<c>Run(instrument, bars, tick, calendar = null)</c>); <see cref="RulesOf"/> then builds <c>WalkRules(rr, squareOff, fills, tick)</c>, <see cref="HeaderOf"/> takes the first two labels from the description of the fills
/// (<c>FillDescription.Labels</c>), and the walker's counters, the new ones included, are summed in <see cref="Run"/> where <c>Walk</c> is built. Nothing else in this class knows how a fill is made.
/// </para>
/// </summary>
public sealed class Simulator
{
    private static readonly SourceSpan NoText = new(0, 0, 1, 1);

    private readonly string _strategyId;
    private readonly SideRun[] _sides;
    private readonly FillRules _fills;

    private Simulator(string strategyId, SideRun[] sides, FillRules fills, bool holdsOvernight)
    {
        _strategyId = strategyId;
        _sides = sides;
        _fills = fills;
        HoldsOvernight = holdsOvernight;
    }

    /// <summary>The strategy is a swing one: its positions are held across sessions (ADR 0040). The replay excludes such a position at a flagged day, and the cost segment of P3-03c is chosen from it.</summary>
    public bool HoldsOvernight { get; }

    /// <summary>The fill rules every walk of this simulator applies (the default ones when <see cref="Create"/> was given none).</summary>
    public FillRules Fills => _fills;

    /// <summary>The sides this simulator runs, Buy first.</summary>
    public IReadOnlyList<Side> Sides => _sides.Select(s => s.Compiled.Side).ToList().AsReadOnly();

    /// <summary>
    /// Compiles every side the definition lists for the evaluation timeframe (<see cref="StrategyCompiler"/>), refuses by name what this simulator cannot run, and returns the simulator or the located problems. The problems of the
    /// compile are passed on unchanged, with their paths and spans; a <c>simulation.*</c> refusal carries the path of the field and the span of a model problem (0, 0, line 1, column 1). When a simulator exists, the problems are the
    /// warnings of the compile.
    /// </summary>
    /// <param name="definition">The strategy.</param>
    /// <param name="evaluationTimeframe">The timeframe the rules are evaluated on.</param>
    /// <param name="fills">The entry limit and the slippage of every walk (ADR 0038); null means <see cref="FillRules.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="evaluationTimeframe"/> is not one of the nine timeframes (a programming error, as in the compiler).</exception>
    public static SimulatorBuild Create(StrategyDefinition definition, Timeframe evaluationTimeframe, FillRules? fills = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var problems = new List<LocatedProblem>();
        var compiled = new List<CompiledSide>();
        var listed = (definition.Sides ?? []).Where(s => Enum.IsDefined(s)).Distinct().Order().ToList();
        if (listed.Count == 0)
        {
            listed.Add(Side.Buy);   // the compile of any side reports a strategy that lists none (or only undefined ones), with its path
        }

        foreach (var side in listed)
        {
            var result = StrategyCompiler.Compile(definition, evaluationTimeframe, side);
            problems.AddRange(result.Problems);
            if (result.Compiled is { } one)
            {
                compiled.Add(one);
            }
        }

        if (evaluationTimeframe >= Timeframe.W1)
        {
            problems.Add(Refusal("timeframes", SimulatorCodes.ScannerTimeframeUnsupported, "The simulator does not evaluate on " + evaluationTimeframe.ToCode() + ": the frame closes a 1D bar at the step of the 15:29 minute (a bar closed late by the first minute of the next date is stale and not evaluated), but a bar of a week or a month is not evaluated (ADR 0043)."));
        }

        var swing = definition.Style == Style.Swing;
        var runs = new List<SideRun>(compiled.Count);
        foreach (var side in compiled)
        {
            var before = problems.Count;
            RefuseWhatItCannotRun(definition, side, problems);
            if (problems.Count != before)
            {
                continue;
            }

            // A side that cannot be built always gives a problem: a result must be what it says it is, and a side that is dropped silently would make a half strategy look whole.
            var hold = side.TimeRules.MaxHoldSessions;
            var square = side.TimeRules.SquareOffMinute;
            var name = side.Side == Side.Buy ? "buy" : "sell";
            var rr = side.Exits[0].Values["rr"][0];   // exactly one FIXED_RR with one rr value: RefuseWhatItCannotRun refused every other shape above
            if (swing && hold is { } sessions)
            {
                runs.Add(new SideRun(side, rr, WalkRules.NoSquareOff, sessions));
            }
            else if (!swing && square is { } minute)
            {
                runs.Add(new SideRun(side, rr, minute, null));
            }
            else
            {
                problems.Add(Refusal("time_rules", SimulationErrors.RulesInvalidCode, swing ? "A swing side needs `max_hold_sessions` (1 to 5)." : "An intraday side needs a `square_off` time."));
            }
        }

        var distinct = problems.Distinct().OrderBy(p => p.Problem.Severity == ProblemSeverity.Error ? 0 : 1).ToList();
        if (distinct.Any(p => p.Problem.Severity == ProblemSeverity.Error) || runs.Count != listed.Count)
        {
            return new SimulatorBuild(null, distinct.AsReadOnly());
        }

        return new SimulatorBuild(new Simulator(definition.Id, [.. runs], fills ?? FillRules.Default, swing), distinct.AsReadOnly());
    }

    /// <summary>
    /// Scans the minutes of one instrument with one scanner per side, then walks every candidate, and returns the trades, the candidates that made none, the counters of both stages and the header. The minutes must be strictly
    /// ascending: this is checked once, in O(n), before any step, and a violation is <c>simulation.bars_not_ascending</c> (so the <c>IgnoredBars</c> of a result is always 0). The calendar, when given, lets the frame close the last
    /// buckets of a special session at their own last minute.
    /// </summary>
    /// <param name="instrument">The instrument; its id and symbol are written on every trade.</param>
    /// <param name="bars">The instrument's 1-minute bars in ascending order.</param>
    /// <param name="tick">The instrument's tick size (<see cref="TickSpec.FromMasterPaise"/>); the price grid of its fills and of its entry limit.</param>
    /// <param name="calendar">The trading calendar, or null for the regular 09:15 to 15:30 session.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bars"/> or <paramref name="tick"/> is null.</exception>
    public Result<SimulationResult> Run(InstrumentRef instrument, IReadOnlyList<Bar> bars, TickSpec tick, TradingCalendar? calendar = null)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(tick);
        for (var i = 1; i < bars.Count; i++)
        {
            if (bars[i].EpochSeconds <= bars[i - 1].EpochSeconds)
            {
                return Result.Failure<SimulationResult>(Error.Validation(SimulatorCodes.BarsNotAscending, "The minutes are not strictly ascending at position " + i + ": two equal epochs or a descending pair."));
            }
        }

        var outcomes = new List<CandidateOutcome>();
        var scans = new List<ScanCounters>(_sides.Length);
        var walk = WalkCounters.Empty;
        foreach (var side in _sides)
        {
            var created = EntryScanner.Create(side.Compiled, instrument, calendar);
            if (created.IsFailure)
            {
                return Result.Failure<SimulationResult>(created.Error);
            }

            var scanner = created.Value;
            for (var i = 0; i < bars.Count; i++)
            {
                scanner.Step(bars[i]);
            }

            scans.Add(scanner.Counters);
            var rules = RulesOf(side, _fills, tick, calendar);
            if (rules.IsFailure)
            {
                return Result.Failure<SimulationResult>(rules.Error);
            }

            // D11: a swing side keeps one open position at a time, so its candidates go through the gate; an intraday trade leaves on its entry date and is not gated.
            var sideRules = rules.Value;
            var walked = HoldsOvernight
                ? PositionGate.Apply(scanner.Candidates, candidate => TradeWalker.Walk(candidate, bars, sideRules))
                : WalkAll(scanner.Candidates, bars, sideRules);
            if (walked.IsFailure)
            {
                return Result.Failure<SimulationResult>(walked.Error);
            }

            foreach (var outcome in walked.Value)
            {
                outcomes.Add(outcome);
                walk = walk.Add(outcome.Walk.ToCounters());
            }
        }

        var ordered = outcomes.OrderBy(o => o.Candidate.DecisionEpochSeconds).ThenBy(o => o.Candidate.Side).ToList().AsReadOnly();
        return Result.Success(new SimulationResult(HeaderOf(), ordered, scans.AsReadOnly(), walk));
    }

    /// <summary>The one place the rules of a walk are built: the exit rules of the side, the fill rules, the tick of the instrument and, for a swing side, the hold counted with the calendar.</summary>
    private static Result<WalkRules> RulesOf(SideRun side, FillRules fills, TickSpec tick, TradingCalendar? calendar)
    {
        if (side.MaxHoldSessions is not { } sessions)
        {
            return Result.Success(new WalkRules(side.RewardRisk, side.SquareOffMinute, fills, tick));
        }

        var hold = SwingHold.Create(sessions, calendar);
        return hold.IsFailure
            ? Result.Failure<WalkRules>(hold.Error)
            : Result.Success(new WalkRules(side.RewardRisk, WalkRules.NoSquareOff, fills, tick, hold.Value));
    }

    private static Result<IReadOnlyList<CandidateOutcome>> WalkAll(IReadOnlyList<EntryCandidate> candidates, IReadOnlyList<Bar> bars, WalkRules rules)
    {
        var outcomes = new List<CandidateOutcome>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var walked = TradeWalker.Walk(candidate, bars, rules);
            if (walked.IsFailure)
            {
                return Result.Failure<IReadOnlyList<CandidateOutcome>>(walked.Error);
            }

            outcomes.Add(new CandidateOutcome(candidate, walked.Value));
        }

        return Result.Success<IReadOnlyList<CandidateOutcome>>(outcomes);
    }

    /// <summary>The one place the header is built: the first two labels come from the description of the fill rules, the other three are fixed.</summary>
    private SimulationHeader HeaderOf() =>
        new(SimulationEngine.Version, SimTrade.CurrentSchemaVersion, _strategyId, _fills, _fills.Basis, CostBasis.None, SimulationHeader.LabelsOf(_fills));

    private static LocatedProblem Refusal(string path, string code, string message) => new(path, new Problem(code, message, NoText));

    private static void RefuseWhatItCannotRun(StrategyDefinition definition, CompiledSide side, List<LocatedProblem> problems)
    {
        var name = side.Side == Side.Buy ? "buy" : "sell";
        if (side.Entry != EntryKind.AtClose)
        {
            var kind = side.Entry == EntryKind.StopAboveHigh ? "stop_above_high" : "limit_pullback";
            problems.Add(Refusal(name + ".entry", SimulatorCodes.UnsupportedEntry, "The entry `" + kind + "` places an order that lives across bars; the simulator runs `at_close` only (tickets that live across bars come in a later slice)."));
        }

        var stop = side.Stop;
        if (stop.Kind is not (StopKind.OrOpposite or StopKind.SwingLow or StopKind.SwingHigh or StopKind.BarLow or StopKind.BarHigh))
        {
            problems.Add(Refusal(name + ".stop", SimulatorCodes.UnsupportedStop, "The stop `" + StopName(stop.Kind) + "` is not run yet: the simulator runs a level (`or_opposite`, `swing_low`, `swing_high`, `bar_low`, `bar_high`) with no buffer (the other kinds come with P3-05)."));
        }
        else if (stop.BufferAtr > 0)
        {
            problems.Add(Refusal(name + ".stop.buffer_atr", SimulatorCodes.UnsupportedStop, "A `buffer_atr` on the stop `" + StopName(stop.Kind) + "` is not run yet (P3-05)."));
        }
        else if (stop.Timeframe != side.EvaluationTimeframe)
        {
            problems.Add(Refusal(name + ".stop.timeframe", SimulatorCodes.UnsupportedStop, "The stop `" + StopName(stop.Kind) + "` is read on " + stop.Timeframe.ToCode() + ", not on the evaluation timeframe " + side.EvaluationTimeframe.ToCode() + "; the simulator reads it on the evaluation timeframe (P3-05)."));
        }

        if (side.Exits.Count != 1 || side.Exits[0].Policy != ExitPolicy.FixedRr || !side.Exits[0].Values.TryGetValue("rr", out var rr) || rr.Count != 1)
        {
            problems.Add(Refusal(name + ".exits", SimulatorCodes.UnsupportedExit, "The simulator runs exactly one `FIXED_RR` exit with exactly one `rr` value; other policies and several `rr` values are P4's comparisons on the same entries."));
        }

        for (var i = 0; i < side.Overlays.Count; i++)
        {
            problems.Add(Refusal(name + ".overlays[" + i + "]", SimulatorCodes.UnsupportedExit, "The overlay exit `" + OverlayName(side.Overlays[i].Kind) + "` is not run yet (P4); ignoring it would make a result look like what it is not."));
        }

        foreach (var filter in side.Filters)
        {
            var at = Math.Max(0, definition.Filters.ToList().IndexOf(filter));
            problems.Add(Refusal("filters[" + at + "]", SimulatorCodes.FilterNotAvailable, "The filter `" + filter + "` is not evaluated yet (there is no results calendar, P15, and no liquidity data of the universe layer, doc 10 §2); the simulator refuses to run a strategy that names one rather than ignore it."));
        }

        if (side.NeedsContext)
        {
            problems.Add(Refusal(name, SimulatorCodes.NeedsContext, "The side reads the market context (the VIX, the index, the regime or breadth); the simulator has no context to give it (reading it is P3-01d)."));
        }

        if (side.NeedsExpiryDays)
        {
            problems.Add(Refusal(name, SimulatorCodes.NeedsExpiryDays, "The side reads the expiry dates (`is_expiry_day`); the simulator has none to give it (reading them is P3-01d)."));
        }
    }

    private static string StopName(StopKind kind) => kind switch
    {
        StopKind.FixedPct => "fixed_pct",
        StopKind.AtrMult => "atr_mult",
        StopKind.SwingLow => "swing_low",
        StopKind.SwingHigh => "swing_high",
        StopKind.BarLow => "bar_low",
        StopKind.BarHigh => "bar_high",
        StopKind.OrOpposite => "or_opposite",
        _ => kind.ToString(),
    };

    private static string OverlayName(OverlayKind kind) => kind switch
    {
        OverlayKind.TimeStop => "time_stop",
        OverlayKind.EventExit => "event_exit",
        _ => kind.ToString(),
    };

    /// <summary>A compiled side with what its walks need: the reward-to-risk of its one <c>FIXED_RR</c>, its square-off minute (<see cref="WalkRules.NoSquareOff"/> for a swing side) and, for a swing side, its longest hold in sessions.</summary>
    private sealed record SideRun(CompiledSide Compiled, double RewardRisk, int SquareOffMinute, int? MaxHoldSessions);
}
