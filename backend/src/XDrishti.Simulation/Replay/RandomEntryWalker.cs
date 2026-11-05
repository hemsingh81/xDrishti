using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Replay;

/// <summary>
/// The random-entry walk of one instrument (spec CF-07, D1; ADR 0208). For each real trade of the plan on this instrument it draws, for each seed, an entry minute uniformly inside the strategy's own entry window (<c>first_entry</c> to <c>last_entry</c>, the
/// square-off being the end) on the same session, and walks it with the rules of the strategy through <see cref="EntryOverride"/>: the same side, the same initial risk as a fraction of the trigger close (the price the stop is placed from), the same fixed target multiple, the same square-off, the same
/// fill rules and tick, then costs it with the same sheet at its own quantity (<see cref="TradeCostApplier"/>). A draw that makes no trade (the limit is not reached, the entry runs through the stop, the cost is above the maximum, the size is under one share)
/// is drawn again up to <see cref="NullWalkPlan.Redraws"/> times and then dropped and counted.
/// <para>
/// <b>Reads.</b> Only the bars of the session of each real trade, adjusted on a copy exactly as the replay adjusts them, once per session and shared by all seeds; the walk of a forced entry reads strictly forward from its own minute and never past the end of that
/// session. A session after <see cref="InstrumentReplayInput.To"/> is refused, so a window cut to the development block (CF-06) cannot be left. Deterministic: the draws of a trade in a seed come from a generator seeded from the plan seed, the seed number and the
/// trade's plan position only, so the order in which instruments are walked changes nothing.
/// </para>
/// <para>
/// <b>What it covers.</b> Intraday strategies with a fixed reward-to-risk exit. A swing strategy, an instrument the replay would skip, a gross run (no cost sheet) and a session without bars are <c>unavailable</c> with the reason, never a number.
/// </para>
/// </summary>
public static class RandomEntryWalker
{
    private const ulong TradeMix = 0x9E37_79B9_7F4A_7C15UL;

    private const ulong SeedMix = 0xD1B5_4A32_D192_ED03UL;

    /// <summary>Walks the plan's trades that are on this instrument. <paramref name="tradeDone"/> is told 1 after each trade; a cancel is seen before each seed of a trade.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="OperationCanceledException">The token is cancelled.</exception>
    public static NullWalkResult Walk(PreparedStrategy strategy, InstrumentReplayInput input, NullWalkPlan plan, IProgress<int>? tradeDone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(plan);
        var mine = plan.Trades.Where(t => t.InstrumentId == input.InstrumentId).ToList();
        if (mine.Count == 0)
        {
            return new NullWalkResult([]);
        }

        NullWalkResult All(string reason) => new([.. mine.Select(t => new NullTradeDraws(t.Id, [], 0, reason))]);
        if (strategy.HoldsOvernight)
        {
            return All("a swing strategy holds positions over sessions: the random-entry null covers intraday strategies only");
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
            return All("the run is gross: there is no charge sheet to cost a random trade with");
        }

        var sessions = SessionsOf(input, [.. mine.Select(t => t.Date)]);
        var instrument = new InstrumentRef(input.InstrumentId, input.Symbol);
        var done = new List<NullTradeDraws>(mine.Count);
        foreach (var spec in mine)
        {
            done.Add(One(strategy, input, plan, instrument, spec, sessions, cancellationToken));
            tradeDone?.Report(1);
        }

        return new NullWalkResult(done);
    }

    /// <summary>The costed random trade of every seed for one real trade of the plan (null where the draw was dropped), or the reason the trade cannot be restated. For tests and diagnosis: <see cref="Walk"/> keeps only the net R. Public because the project exposes no internals to its tests.</summary>
    public static (IReadOnlyList<SimTrade?> Trades, int Redraws, string? Unavailable) RandomTrades(PreparedStrategy strategy, InstrumentReplayInput input, NullWalkPlan plan, NullTradeSpec spec)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(spec);
        var sessions = SessionsOf(input, [spec.Date]);
        return Draws(strategy, input, plan, new InstrumentRef(input.InstrumentId, input.Symbol), spec, sessions, CancellationToken.None);
    }

    private static NullTradeDraws One(PreparedStrategy strategy, InstrumentReplayInput input, NullWalkPlan plan, InstrumentRef instrument, NullTradeSpec spec, Dictionary<DateOnly, Bar[]> sessions, CancellationToken cancellationToken)
    {
        var (trades, redraws, refused) = Draws(strategy, input, plan, instrument, spec, sessions, cancellationToken);
        return refused is not null ? new NullTradeDraws(spec.Id, [], 0, refused) : new NullTradeDraws(spec.Id, [.. trades.Select(t => t is null ? double.NaN : t.Costing!.RNet)], redraws, null);
    }

    private static (IReadOnlyList<SimTrade?> Trades, int Redraws, string? Unavailable) Draws(PreparedStrategy strategy, InstrumentReplayInput input, NullWalkPlan plan, InstrumentRef instrument, NullTradeSpec spec, Dictionary<DateOnly, Bar[]> sessions, CancellationToken cancellationToken)
    {
        (IReadOnlyList<SimTrade?>, int, string?) Refused(string reason) => ([], 0, reason);
        if (strategy.HoldsOvernight || input.PresetSkip is not null || input.Tick is null || input.Costs is null)
        {
            return Refused("the instrument or the run cannot be restated (swing, skipped, no tick or no charge sheet)");
        }

        if (spec.Date > input.To)
        {
            return Refused("the session is after the end of the window");
        }

        if (!sessions.TryGetValue(spec.Date, out var bars) || bars.Length == 0)
        {
            return Refused("no bars are stored for the session");
        }

        if (strategy.Simulator.CompiledOf(spec.Side) is not { } side)
        {
            return Refused("the strategy has no " + (spec.Side == Side.Buy ? "buy" : "sell") + " side");
        }

        if (side.TimeRules.SquareOffMinute is not { } squareOff)
        {
            return Refused("the strategy has no square-off time");
        }

        if (!(spec.RiskFraction > 0 && double.IsFinite(spec.RiskFraction) && spec.RiskFraction < 1 && spec.RewardRisk > 0 && double.IsFinite(spec.RewardRisk)))
        {
            return Refused("the real trade has no usable risk or target");
        }

        // the decision clock of a forced entry is the start of its minute plus one, as the keyword `time` reads it (ADR 0034, decision 4)
        var first = side.TimeRules.FirstEntryMinute ?? SessionClock.OpenMinute + 1;
        var last = Math.Min(side.TimeRules.LastEntryMinute ?? int.MaxValue, squareOff - 1);
        var eligible = new List<int>(bars.Length);
        for (var i = 0; i < bars.Length; i++)
        {
            var clock = SessionClock.MinuteOfDay(bars[i].EpochSeconds) + 1;
            if (clock >= first && clock <= last)
            {
                eligible.Add(i);
            }
        }

        if (eligible.Count == 0)
        {
            return Refused("the session has no minute inside the entry window");
        }

        var rules = new WalkRules(spec.RewardRisk, squareOff, strategy.Fills, input.Tick);
        var results = new SimTrade?[plan.Draws];
        var redraws = 0;
        var tradeSeed = new SeededRandom(plan.Seed ^ ((ulong)(spec.Id + 1) * TradeMix)).NextBits();
        for (var d = 0; d < plan.Draws; d++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rng = new SeededRandom(tradeSeed ^ ((ulong)(d + 1) * SeedMix));
            for (var attempt = 0; attempt <= plan.Redraws; attempt++)
            {
                var index = eligible[rng.Next(eligible.Count)];
                var candidate = EntryOverride.Forced(instrument, strategy.Record.Id, bars, index, spec.Side, EntryOverride.StopAt(spec.Side, bars[index].Close, spec.RiskFraction));
                var walked = EntryOverride.Walk(candidate, bars, rules);
                if (walked.IsFailure)
                {
                    return Refused("the walk was refused: " + walked.Error.Code);
                }

                if (walked.Value.Trade is { } trade
                    && TradeCostApplier.Apply(trade, input.Costs, spec.Segment) is { IsSuccess: true, Value.Trade: { } costed })
                {
                    results[d] = costed;
                    break;
                }

                if (attempt < plan.Redraws)
                {
                    redraws++;   // a failed try that is followed by another draw; the last failed try drops the draw
                }
            }
        }

        return (results, redraws, null);
    }

    /// <summary>The bars of each wanted session, adjusted on a copy as the replay adjusts them; a date with no bar is absent.</summary>
    internal static Dictionary<DateOnly, Bar[]> SessionsOf(InstrumentReplayInput input, HashSet<DateOnly> wanted)
    {
        var found = new Dictionary<DateOnly, List<Bar>>();
        foreach (var bar in input.RawBars)
        {
            var date = SessionClock.DateOf(bar.EpochSeconds);
            if (date <= input.To && wanted.Contains(date))
            {
                if (!found.TryGetValue(date, out var list))
                {
                    found[date] = list = new List<Bar>(SessionClock.RegularBars);
                }

                list.Add(bar);
            }
        }

        var adjusts = input.RawBars.Count > 0 && input.Factors.ActionsAfter(SessionClock.DateOf(input.RawBars[0].EpochSeconds)).Count > 0;
        var sessions = new Dictionary<DateOnly, Bar[]>(found.Count);
        foreach (var (date, list) in found)
        {
            var factor = adjusts ? input.Factors.FactorAt(date) : 1.0;
            sessions[date] = factor == 1.0 ? [.. list] : [.. list.Select(b => ReplayBars.Adjusted(b, factor))];
        }

        return sessions;
    }
}
