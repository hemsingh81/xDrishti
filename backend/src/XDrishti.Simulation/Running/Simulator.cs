using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Strength;
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
/// (<c>or_opposite</c>, <c>swing_low</c>, <c>swing_high</c>, <c>bar_low</c> or <c>bar_high</c>, no buffer, read on the evaluation timeframe), exactly one exit candidate (<c>FIXED_RR</c>, <c>PARTIAL_BE</c> or <c>PARTIAL_TRAIL</c> from P4-02, from P4-03 <c>ATR_TRAIL</c>, <c>STRUCTURE_TRAIL</c> or <c>EMA_TRAIL</c>, or from P4-04 <c>PYRAMID</c>, whose ladders are not candidate lists) with one value in each list, at most the <c>time_stop</c> overlay, no <c>min_turnover</c> filter (the filter <c>not_event_day</c> is evaluated from a results-calendar snapshot, P15-06) and no expiry dates. A side may read the daily market context (P3-lift, ADR 0145): <see cref="Run"/> is given a <see cref="ScanContext"/> and every decision reads the latest context day strictly before its date. A swing side is held up to its
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
    /// <summary>How often (a power of two minus one) the scan looks at the cancellation token.</summary>
    internal const int CancelMask = 4095;

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

    /// <summary>Some side reads the daily market context: <see cref="Run"/> needs one (the replay reads the stored days for such a strategy only).</summary>
    public bool ReadsContext => _sides.Any(s => s.Compiled.NeedsContext);

    /// <summary>Some side names the filter <c>not_event_day</c>: <see cref="Run"/> needs a results-calendar snapshot and a trading calendar, and a run is refused with <c>simulation.filter_not_available</c> without them (P15-06, ADR 0182).</summary>
    public bool ReadsEvents => _sides.Any(s => s.Compiled.NeedsEventCalendar);

    /// <summary>A side names the filter <c>min_turnover</c> (CF-05, ADR 0230): the fill rules must carry its threshold, and a run gives the replay the turnover lookup.</summary>
    public bool ReadsTurnover => _sides.Any(s => s.Compiled.NeedsTurnover);

    /// <summary>Some side names <c>days_to_results</c>, <c>days_since_results</c>, <c>results_sue_q</c> or <c>results_car3_q</c>, or lists the <c>event_exit</c> overlay (P15-09): <see cref="Run"/> needs the results-days inputs and a trading calendar, and a run is refused with <c>simulation.filter_not_available</c> without them (P15-05, ADR 0185).</summary>
    public bool ReadsResultsDays => _sides.Any(s => ResultsDaysScan.Reads(s.Compiled));

    /// <summary>Some side names <c>results_sue_q</c> or <c>results_car3_q</c> (P15-09): the results-days inputs must also carry the surprise numbers, or the quintiles read NaN and nothing enters (a replay skips such an instrument first).</summary>
    public bool ReadsSurprise => _sides.Any(s => ResultsDaysScan.ReadsSurprise(s.Compiled));

    /// <summary>Some side names <c>mom_rank</c>, <c>rs_vs_index</c> or <c>sector_rs</c> (BK-09): <see cref="Run"/> needs the stored rows of the instrument and refuses without them.</summary>
    public bool ReadsRelativeStrength => _sides.Any(s => RelativeStrengthScan.Reads(s.Compiled));

    /// <summary>The sides this simulator runs, Buy first.</summary>
    public IReadOnlyList<Side> Sides => _sides.Select(s => s.Compiled.Side).ToList().AsReadOnly();

    /// <summary>The compiled side that decides entries for <paramref name="side"/>, or null when the strategy has none (P3-04d).</summary>
    internal CompiledSide? CompiledOf(Side side) => _sides.FirstOrDefault(s => s.Compiled.Side == side)?.Compiled;

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
            var plan = side.Exits[0];   // exactly one candidate with one value in each list: RefuseWhatItCannotRun refused every other shape above
            double? rr = plan.Policy switch
            {
                ExitPolicy.FixedRr => plan.Values["rr"][0],
                ExitPolicy.PartialBe or ExitPolicy.PartialTrail => plan.Values["t1_rr"][0],   // T1 is the target of a partial policy
                _ => null,
            };
            var trail = plan.Policy is ExitPolicy.AtrTrail or ExitPolicy.StructureTrail or ExitPolicy.EmaTrail or ExitPolicy.Pyramid ? plan : null;
            var policy = PolicyOf(side);
            if (swing && hold is { } sessions)
            {
                runs.Add(new SideRun(side, rr, WalkRules.NoSquareOff, sessions, trail, policy));
            }
            else if (!swing && square is { } minute)
            {
                runs.Add(new SideRun(side, rr, minute, null, trail, policy));
            }
            else
            {
                problems.Add(Refusal("time_rules", SimulationErrors.RulesInvalidCode, swing ? "A swing side needs `max_hold_sessions` (1 to 5, or 1 to 20 for a side that reads days_since_results)." : "An intraday side needs a `square_off` time."));
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
    /// <param name="quantity">The full size <c>Q</c> of ADR 0042 for an exact entry and an initial risk, for a <c>PYRAMID</c> to see an add of no whole share (P4-04, D3); null in a gross run, where no add is skipped for its quantity.</param>
    /// <param name="context">The daily market context the context features read (P3-lift, ADR 0145); required when a side reads it (<see cref="ReadsContext"/>), ignored otherwise.</param>
    /// <param name="events">The results-calendar snapshot the filter <c>not_event_day</c> reads (P15-06); required, with a <paramref name="calendar"/>, when a side names the filter (<see cref="ReadsEvents"/>), ignored otherwise.</param>
    /// <param name="resultsDays">What the features <c>days_to_results</c> and <c>days_since_results</c> read (P15-05); required, with a <paramref name="calendar"/>, when a side names either (<see cref="ReadsResultsDays"/>), ignored otherwise.</param>
    /// <param name="turnoverOn">The 20-day median traded value in rupees on an entry date (CF-05, ADR 0230); read when a side names <c>min_turnover</c>; null or a null answer blocks the entry.</param>
    /// <param name="relativeStrength">The stored relative-strength rows of the instrument that <c>mom_rank</c>, <c>rs_vs_index</c> and <c>sector_rs</c> read (BK-09); required when a side names one.</param>
    /// <param name="memberOn">Whether the instrument is a member of the basket on an entry date (BK-11b, dated members); null for every other run. A date that is not a member date takes no new entry.</param>
    /// <param name="tierOn">The liquidity tier of the instrument on an entry date (CF-05, D2); read only when the fill rules ask for conservative slippage, and null then excludes every trade.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bars"/> or <paramref name="tick"/> is null.</exception>
    public Result<SimulationResult> Run(InstrumentRef instrument, IReadOnlyList<Bar> bars, TickSpec tick, TradingCalendar? calendar = null, Func<decimal, decimal, long>? quantity = null, ScanContext? context = null, EventCalendarSnapshot? events = null, ResultsDayInputs? resultsDays = null, Func<DateOnly, LiquidityTier>? tierOn = null, Func<DateOnly, decimal?>? turnoverOn = null, RelativeStrengthInputs? relativeStrength = null, Func<DateOnly, bool>? memberOn = null) =>
        RunUntilCancelled(instrument, bars, tick, calendar, quantity, context, events, resultsDays, tierOn, turnoverOn, CancellationToken.None, relativeStrength, memberOn);

    /// <summary>As <c>Run</c>, looking at <paramref name="cancellationToken"/> every few thousand minutes of the scan; a stop raises <see cref="OperationCanceledException"/> (AU-12). A separate overload so that existing callers are unchanged.</summary>
    public Result<SimulationResult> RunUntilCancelled(InstrumentRef instrument, IReadOnlyList<Bar> bars, TickSpec tick, TradingCalendar? calendar, Func<decimal, decimal, long>? quantity, ScanContext? context, EventCalendarSnapshot? events, ResultsDayInputs? resultsDays, Func<DateOnly, LiquidityTier>? tierOn, Func<DateOnly, decimal?>? turnoverOn, CancellationToken cancellationToken, RelativeStrengthInputs? relativeStrength = null, Func<DateOnly, bool>? memberOn = null)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(tick);
        if (ReadsResultsDays && (resultsDays is null || calendar is null))
        {
            return Result.Failure<SimulationResult>(ResultsDaysNotGivenError());
        }

        if (ReadsContext && context is null)
        {
            return Result.Failure<SimulationResult>(ContextNotGivenError());
        }

        if (ReadsRelativeStrength && relativeStrength is null)
        {
            return Result.Failure<SimulationResult>(Error.Validation(SimulatorCodes.FilterNotAvailable, "The strategy reads mom_rank, rs_vs_index or sector_rs and no stored relative-strength rows were given (BK-09)."));
        }

        if (ReadsEvents && (events is null || calendar is null))
        {
            return Result.Failure<SimulationResult>(EventsNotGivenError());
        }

        if (NotAscending(bars) is { } unordered)
        {
            return Result.Failure<SimulationResult>(unordered);
        }

        var outcomes = new List<CandidateOutcome>();
        var scans = new List<ScanCounters>(_sides.Length);
        var walk = WalkCounters.Empty;
        var series = ReadsResultsDays ? ResultsDaysScan.For(resultsDays!, instrument.Id, bars, calendar!) : null;
        var strength = relativeStrength is null ? null : RelativeStrengthScan.For(relativeStrength);
        foreach (var side in _sides)
        {
            var created = EntryScanner.Create(side.Compiled, instrument, calendar, context, series, strength);
            if (created.IsFailure)
            {
                return Result.Failure<SimulationResult>(created.Error);
            }

            var scanner = created.Value;
            for (var i = 0; i < bars.Count; i++)
            {
                if ((i & CancelMask) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested(); // a stop is seen every few thousand minutes, so a long replay can be abandoned (AU-12)
                }

                scanner.Step(bars[i]);
            }

            scans.Add(scanner.Counters);

            // D11: a swing side keeps one open position at a time, so its candidates go through the gate; an intraday trade leaves on its entry date and is not gated.
            var walked = WalkSide(side, scanner, bars, tick, calendar, quantity, HoldsOvernight, events, series, tierOn, turnoverOn, memberOn);
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

    /// <summary>The error of a context-reading side run with no context.</summary>
    internal static Error ContextNotGivenError() => Error.Validation(SimulatorCodes.ContextNotGiven, "The strategy reads the market context and none was given; the replay skips such an instrument with `context_unavailable` before it runs.");

    /// <summary>The error of a side that names <c>not_event_day</c> run with no results-calendar snapshot or no trading calendar: the replay skips such an instrument with <c>event_calendar_unavailable</c> first, and the use case refuses a run with no rows at all.</summary>
    internal static Error EventsNotGivenError() => Error.Validation(SimulatorCodes.FilterNotAvailable, "The strategy names the filter `not_event_day` and no results calendar snapshot or trading calendar was given (P15-04).");

    /// <summary>The error of a side that names <c>days_to_results</c> or <c>days_since_results</c> run with no results-days inputs or no trading calendar: the replay skips such an instrument first, and the use case refuses a run with neither a calendar row nor a filing.</summary>
    internal static Error ResultsDaysNotGivenError() => Error.Validation(SimulatorCodes.FilterNotAvailable, "no results calendar or filings for the window (P15)");

    /// <summary>The refusal for minutes that are not strictly ascending, or null.</summary>
    internal static Error? NotAscending(IReadOnlyList<Bar> bars)
    {
        for (var i = 1; i < bars.Count; i++)
        {
            if (bars[i].EpochSeconds <= bars[i - 1].EpochSeconds)
            {
                return Error.Validation(SimulatorCodes.BarsNotAscending, "The minutes are not strictly ascending at position " + i + ": two equal epochs or a descending pair.");
            }
        }

        return null;
    }

    /// <summary>
    /// Walks every candidate of a scanned side under this simulator's one exit (P4-05: the scanner may belong to another simulator of a comparison, whose tape holds the features of every candidate; the column of this exit is found by name).
    /// With <paramref name="gated"/> the candidates go through the <see cref="PositionGate"/>, else every candidate is walked.
    /// </summary>
    internal Result<IReadOnlyList<CandidateOutcome>> WalkSide(SideRun side, EntryScanner scanner, IReadOnlyList<Bar> bars, TickSpec tick, TradingCalendar? calendar, Func<decimal, decimal, long>? quantity, bool gated, EventCalendarSnapshot? events = null, ResultsDaysScan? series = null, Func<DateOnly, LiquidityTier>? tierOn = null, Func<DateOnly, decimal?>? turnoverOn = null, Func<DateOnly, bool>? memberOn = null)
    {
        if (side.Compiled.NeedsTurnover && _fills.MinTurnoverCrore is null)
        {
            // never run a side that names the filter without its threshold: that would take every entry as if the filter passed
            return Result.Failure<IReadOnlyList<CandidateOutcome>>(Error.Validation(SimulatorCodes.FilterNotAvailable, "the filter min_turnover has no threshold in the fill rules (CF-05, ADR 0230)"));
        }

        var rules = RulesOf(side, _fills, tick, calendar, series, tierOn, turnoverOn, memberOn);
        if (rules.IsFailure)
        {
            return Result.Failure<IReadOnlyList<CandidateOutcome>>(rules.Error);
        }

        var sideRules = rules.Value;
        var rule = side.Trail is { } trail ? TrailRules.Of(trail, scanner.TapeColumnOf(trail)) : null;
        var exitContext = rule is null && side.Policy is null ? null : new ExitContext(rule, scanner.Tape, side.Policy, quantity);
        Func<EntryCandidate, Result<WalkOutcome>> walk = candidate => TradeWalker.Walk(candidate, bars, sideRules, exitContext);
        if (side.Compiled.NeedsEventCalendar && (events is null || calendar is null))
        {
            // Never run a side that names the filter without the data it needs: that would take every entry as if the filter passed.
            return Result.Failure<IReadOnlyList<CandidateOutcome>>(EventsNotGivenError());
        }

        if (side.Compiled.NeedsEventCalendar && events is not null && calendar is not null)
        {
            // P15-06: the filter decides before the walk, from the rows visible at the decision instant; a blocked or unavailable candidate opens no position, so the gate is not occupied by it.
            var hold = side.MaxHoldSessions ?? 0;
            var walkIt = walk;
            walk = candidate => EventOutcome(events, calendar, candidate, hold) is { } refused ? Result.Success(refused) : walkIt(candidate);
        }

        return gated
            ? PositionGate.Apply(scanner.Candidates, walk)
            : WalkAll(scanner.Candidates, walk);
    }

    /// <summary>The no-trade of an entry the filter <c>not_event_day</c> does not let through, or null when the entry is taken. An unavailable answer is never a pass.</summary>
    private static WalkOutcome? EventOutcome(EventCalendarSnapshot events, TradingCalendar calendar, EntryCandidate candidate, int holdSessions) =>
        EventDayFilter.Evaluate(events, candidate.Instrument.Id, DateTimeOffset.FromUnixTimeSeconds(candidate.DecisionEpochSeconds), calendar, holdSessions) switch
        {
            WindowOutcome.Outside => null,
            WindowOutcome.Inside => WalkResults.NoTrade(NoTradeReason.EventBlocked, 0, 0).Value,
            _ => WalkResults.NoTrade(NoTradeReason.EventUnavailable, 0, 0).Value,
        };

    /// <summary>The side runs of this simulator, Buy first (for a comparison, which walks them against a scanner of its own).</summary>
    internal IReadOnlyList<SideRun> SideRuns => _sides;

    /// <summary>The one place the rules of a walk are built: the exit rules of the side, the fill rules, the tick of the instrument and, for a swing side, the hold counted with the calendar.</summary>
    private static Result<WalkRules> RulesOf(SideRun side, FillRules fills, TickSpec tick, TradingCalendar? calendar, ResultsDaysScan? series, Func<DateOnly, LiquidityTier>? tierOn, Func<DateOnly, decimal?>? turnoverOn, Func<DateOnly, bool>? memberOn)
    {
        if (side.MaxHoldSessions is not { } sessions)
        {
            return Result.Success(new WalkRules(side.RewardRisk, side.SquareOffMinute, fills, tick, TierOn: tierOn, TurnoverOn: turnoverOn, MemberOn: memberOn));
        }

        var hold = SwingHold.Create(sessions, calendar);
        if (hold.IsFailure)
        {
            return Result.Failure<WalkRules>(hold.Error);
        }

        // P15-09: the event_exit overlay reads days_to_results of the instrument's series at the close of each session; a side that lists it is never run without the series (Run refuses that first).
        var overlay = side.Compiled.Overlays.FirstOrDefault(o => o.Kind == OverlayKind.EventExit);
        var plan = overlay is not null && series is not null ? new EventExitPlan((int)overlay.Values["days_before_results"][0], series.DaysToResultsAt) : null;
        return Result.Success(new WalkRules(side.RewardRisk, WalkRules.NoSquareOff, fills, tick, plan is null ? hold.Value : hold.Value with { EventExit = plan }, tierOn, turnoverOn, memberOn));
    }

    private static Result<IReadOnlyList<CandidateOutcome>> WalkAll(IReadOnlyList<EntryCandidate> candidates, Func<EntryCandidate, Result<WalkOutcome>> walk)
    {
        var outcomes = new List<CandidateOutcome>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var walked = walk(candidate);
            if (walked.IsFailure)
            {
                return Result.Failure<IReadOnlyList<CandidateOutcome>>(walked.Error);
            }

            outcomes.Add(new CandidateOutcome(candidate, walked.Value));
        }

        return Result.Success<IReadOnlyList<CandidateOutcome>>(outcomes);
    }

    /// <summary>The one place the header is built: the first two labels come from the description of the fill rules, the other three are fixed.</summary>
    internal SimulationHeader HeaderOf() =>
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
        if (stop.Kind is not (StopKind.OrOpposite or StopKind.SwingLow or StopKind.SwingHigh or StopKind.BarLow or StopKind.BarHigh or StopKind.AtrMult))
        {
            problems.Add(Refusal(name + ".stop", SimulatorCodes.UnsupportedStop, "The stop `" + StopName(stop.Kind) + "` is not run yet: the simulator runs a level (`or_opposite`, `swing_low`, `swing_high`, `bar_low`, `bar_high`) with no buffer, or `atr_mult`."));
        }
        else if (stop.BufferAtr > 0)
        {
            problems.Add(Refusal(name + ".stop.buffer_atr", SimulatorCodes.UnsupportedStop, "A `buffer_atr` on the stop `" + StopName(stop.Kind) + "` is not run yet."));
        }
        else if (stop.Timeframe != side.EvaluationTimeframe)
        {
            problems.Add(Refusal(name + ".stop.timeframe", SimulatorCodes.UnsupportedStop, "The stop `" + StopName(stop.Kind) + "` is read on " + stop.Timeframe.ToCode() + ", not on the evaluation timeframe " + side.EvaluationTimeframe.ToCode() + "; the simulator reads it on the evaluation timeframe."));
        }

        RefuseExits(side, name, problems);

        foreach (var filter in side.Filters.Where(f => !CompiledSide.IsEvaluatedFilter(f)))
        {
            var at = Math.Max(0, definition.Filters.ToList().IndexOf(filter));
            problems.Add(Refusal("filters[" + at + "]", SimulatorCodes.FilterNotAvailable, "The filter `" + filter + "` is not evaluated yet (it is not one the simulator knows how to evaluate); the simulator refuses to run a strategy that names one rather than ignore it."));
        }

        if (side.NeedsExpiryDays)
        {
            problems.Add(Refusal(name, SimulatorCodes.NeedsExpiryDays, "The side reads the expiry dates (`is_expiry_day`). No expiry calendar is stored and none can be derived from stored data: the instrument master holds no option expiries, there is no options data yet (P14), and NSE has changed the weekday of its weekly and monthly expiries more than once, so a rule would be a guess. Remove `is_expiry_day` or wait for the options data."));
        }
    }

    /// <summary>
    /// D10 of P4-02: exactly one candidate of <c>FIXED_RR</c>, <c>PARTIAL_BE</c>, <c>PARTIAL_TRAIL</c> or a trail with one value in each list parameter, and at most the <c>time_stop</c> overlay. The path is `buy.exits` for the exits (the policy and,
    /// for a list, the parameter are named in the message) and `buy.overlays[i]` for an overlay.
    /// </summary>
    private static void RefuseExits(CompiledSide side, string name, List<LocatedProblem> problems)
    {
        if (side.Exits.Count != 1)
        {
            problems.Add(Refusal(name + ".exits", SimulatorCodes.UnsupportedExit, "The simulator runs exactly one exit candidate in a run (`FIXED_RR`, `PARTIAL_BE`, `PARTIAL_TRAIL`, `ATR_TRAIL`, `STRUCTURE_TRAIL`, `EMA_TRAIL` or `PYRAMID`), not " + side.Exits.Count + "; comparing candidates on the same entries is a later slice (P4-05)."));
        }
        else
        {
            var exit = side.Exits[0];
            var label = PolicyName(exit.Policy);
            if (exit.Policy is not (ExitPolicy.FixedRr or ExitPolicy.PartialBe or ExitPolicy.PartialTrail or ExitPolicy.AtrTrail or ExitPolicy.StructureTrail or ExitPolicy.EmaTrail or ExitPolicy.Pyramid))
            {
                problems.Add(Refusal(name + ".exits", SimulatorCodes.UnsupportedExit, "The exit `" + label + "` is not run yet (P4); the simulator runs `FIXED_RR`, `PARTIAL_BE`, `PARTIAL_TRAIL`, `ATR_TRAIL`, `STRUCTURE_TRAIL`, `EMA_TRAIL` and `PYRAMID`."));
            }
            else if (exit.Values.FirstOrDefault(v => v.Value.Count != 1 && !(exit.Policy == ExitPolicy.Pyramid && v.Key is "add_at_rr" or "add_pct")) is { Key: { } list })
            {
                problems.Add(Refusal(name + ".exits", SimulatorCodes.UnsupportedExit, "The exit `" + label + "` has " + exit.Values[list].Count + " values in `" + list + "`; the simulator runs one value of each parameter in a run (the comparison of candidate values is P4-05)."));
            }
        }

        for (var i = 0; i < side.Overlays.Count; i++)
        {
            if (side.Overlays[i].Kind is not (OverlayKind.TimeStop or OverlayKind.EventExit))
            {
                problems.Add(Refusal(name + ".overlays[" + i + "]", SimulatorCodes.UnsupportedExit, "The overlay exit `" + OverlayName(side.Overlays[i].Kind) + "` is not run yet (P4); ignoring it would make a result look like what it is not."));
            }
            else if (side.Overlays[i].Kind == OverlayKind.EventExit && side.Style != Style.Swing)
            {
                problems.Add(Refusal(name + ".overlays[" + i + "]", SimulatorCodes.UnsupportedExit, "The overlay exit `event_exit` is run by the swing walk only; an intraday side leaves at its square-off the same day, so ignoring it would make a result look like what it is not."));
            }
        }
    }

    /// <summary>The policy of the walk for a side that was accepted (RefuseExits): the partial and the time stop as decimals, or null when the side is a plain <c>FIXED_RR</c>.</summary>
    private static LegPolicy? PolicyOf(CompiledSide side)
    {
        var exit = side.Exits[0];
        var partial = exit.Policy switch
        {
            ExitPolicy.PartialBe => PartialKind.Be,
            ExitPolicy.PartialTrail => PartialKind.Trail,
            _ => PartialKind.None,
        };
        var stop = side.Overlays.FirstOrDefault(o => o.Kind == OverlayKind.TimeStop);
        if (partial == PartialKind.None && stop is null && exit.Policy != ExitPolicy.Pyramid)
        {
            return null;
        }

        var pyramid = exit.Policy == ExitPolicy.Pyramid
            ? new PyramidPlan(Decimal(exit.Values["initial_pct"][0]) / 100m, [.. exit.Values["add_at_rr"].Select(Decimal)], [.. exit.Values["add_pct"].Select(v => Decimal(v) / 100m)], (int)exit.Values["max_adds"][0])
            : null;

        var book = partial == PartialKind.None ? 0m : Decimal(exit.Values["book_pct"][0]) / 100m;
        var t2 = partial == PartialKind.Be ? Decimal(exit.Values["t2_rr"][0]) : 0m;
        var mult = partial == PartialKind.Trail ? Decimal(exit.Values["trail_atr_mult"][0]) : 0m;
        return new LegPolicy(
            partial,
            book,
            t2,
            mult,
            stop is null ? null : Decimal(stop.Values["min_progress_rr"][0]),
            stop is not null && stop.Values.TryGetValue("intraday_bars", out var bars) ? (int)bars[0] : 0,
            stop is not null && stop.Values.TryGetValue("swing_sessions", out var sessions) ? (int)sessions[0] : 0,
            pyramid);
    }

    private static decimal Decimal(double value) => TickGrid.TryConvert(value, out var converted) ? converted : 0m;

    private static string PolicyName(ExitPolicy policy) => policy switch
    {
        ExitPolicy.FixedRr => "FIXED_RR",
        ExitPolicy.PartialBe => "PARTIAL_BE",
        ExitPolicy.PartialTrail => "PARTIAL_TRAIL",
        ExitPolicy.AtrTrail => "ATR_TRAIL",
        ExitPolicy.StructureTrail => "STRUCTURE_TRAIL",
        ExitPolicy.EmaTrail => "EMA_TRAIL",
        ExitPolicy.Pyramid => "PYRAMID",
        _ => policy.ToString(),
    };

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

    /// <summary>A compiled side with what its walks need: the reward-to-risk of its target (the one <c>FIXED_RR</c>, or T1 of a partial policy; null for a trail), the plan of its trail (<c>ATR_TRAIL</c>, <c>STRUCTURE_TRAIL</c> or <c>EMA_TRAIL</c>; P4-03), the policy of the partial and the time stop (P4-02), its square-off minute (<see cref="WalkRules.NoSquareOff"/> for a swing side) and, for a swing side, its longest hold in sessions.</summary>
    internal sealed record SideRun(CompiledSide Compiled, double? RewardRisk, int SquareOffMinute, int? MaxHoldSessions, ExitPlan? Trail, LegPolicy? Policy);
}
