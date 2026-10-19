using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The engine side of the comparison (spec P3-08 D3, written by E): the one place where the oracle's records and the engine's types are mapped into each other. Two directions: an oracle case (the author's hand rows and its
/// <c>OracleSeries</c>) becomes engine inputs, and an engine case (the engine's own seeded generators) becomes oracle inputs; the results of the engine are mapped back into the oracle's records so that one comparator reads both. The
/// round-trip tests (<c>AdapterRoundTripTests</c>) hold this class to account field by field, with sentinels, because it is where a misreading of the engine could hollow the comparison.
/// </summary>
internal static class OracleAdapter
{
    /// <summary>The one invented instrument and strategy of the comparison.</summary>
    internal static readonly InstrumentRef Instrument = new(new Guid("00000000-0000-0000-0000-0000000000a8"), "ORACLE");

    internal const string StrategyId = "ORACLE_COMPARISON";

    /// <summary>The trigger bar the adapter gives a candidate that carries only the trigger close: the 15-minute bar that ends at the decision instant.</summary>
    internal const int TriggerMinutes = 15;

    private static readonly DateTimeOffset Stamp = DateTimeOffset.UnixEpoch;

    /// <summary>The fields of <c>SimTrade</c> that are echoes of the candidate and of the rules (the pass-through rows of the dry-read table): checked by <see cref="Echo"/> and not by the oracle, which has no field for them.</summary>
    internal static readonly IReadOnlyList<string> EchoFields =
    [
        nameof(SimTrade.SchemaVersion), nameof(SimTrade.StrategyId), nameof(SimTrade.InstrumentId), nameof(SimTrade.Symbol), nameof(SimTrade.Side), nameof(SimTrade.TriggerTimeframe), nameof(SimTrade.TriggerBar),
        nameof(SimTrade.TriggerBarMinutes), nameof(SimTrade.TriggerBarComplete), nameof(SimTrade.EntrySnapshotJson), nameof(SimTrade.Costs), nameof(SimTrade.Costing), nameof(SimTrade.TickFallback),
    ];

    // ---- oracle -> engine

    internal static Bar[] Bars(IReadOnlyList<OracleBar> bars) => [.. bars.Select(Bar)];

    internal static Bar Bar(OracleBar b) => new(b.EpochSeconds, b.Open, b.High, b.Low, b.Close, b.Volume);

    internal static Side Side(OracleSide side) => side == OracleSide.Buy ? Domain.Research.Side.Buy : Domain.Research.Side.Sell;

    /// <summary>The engine's candidate: the echo fields the oracle does not carry get fixed values (the walker reads none of them; the comparison reads them back as a pass-through).</summary>
    internal static EntryCandidate Candidate(OracleCandidate c) => new(
        StrategyId,
        Instrument,
        Side(c.Side),
        c.DecisionBarIndex,
        c.DecisionEpochSeconds,
        Timeframe.M15,
        TriggerBarOf(c),
        TriggerMinutes,
        true,
        c.StopLevel,
        DecisionTrace.Empty);

    private static Bar TriggerBarOf(OracleCandidate c) =>
        new(c.DecisionEpochSeconds - (TriggerMinutes * 60L), c.TriggerClose, c.TriggerClose, c.TriggerClose, c.TriggerClose, 1_000L * TriggerMinutes);

    internal static WalkRules Rules(OracleRules r)
    {
        var slippage = SlippageConfig.Create(r.Slippage.Ticks, r.Slippage.BarRangeFrac, r.Slippage.StopExitExtraTicks);
        slippage.IsSuccess.ShouldBeTrue("slippage: " + (slippage.IsFailure ? slippage.Error.Message : string.Empty));
        var fills = FillRules.Create(r.EntryLimitPercent, slippage.Value);
        fills.IsSuccess.ShouldBeTrue("fills: " + (fills.IsFailure ? fills.Error.Message : string.Empty));
        var tick = TickSpec.FromRupees(r.Tick);
        tick.IsSuccess.ShouldBeTrue("tick: " + (tick.IsFailure ? tick.Error.Message : string.Empty));
        SwingHold? hold = null;
        if (r.Hold is { } h)
        {
            var created = SwingHold.Create(h.MaxHoldSessions, Calendar(h.Calendar));
            created.IsSuccess.ShouldBeTrue("hold: " + (created.IsFailure ? created.Error.Message : string.Empty));
            hold = created.Value;
        }

        return new WalkRules(r.RewardRisk, r.SquareOffMinute, fills.Value, tick.Value, hold);
    }

    /// <summary>The engine's calendar of an oracle calendar; null stays null (the default week).</summary>
    internal static TradingCalendar? Calendar(OracleCalendar? c)
    {
        if (c is null)
        {
            return null;
        }

        return new TradingCalendar([
            .. c.Holidays.Select(d => TradingDay.Holiday(d, CalendarSource.Manual, CalendarStatus.Confirmed, null, Stamp)),
            .. c.SpecialSessions.Select(s => TradingDay.Special(s.Key, s.Value.FromMinute, s.Value.ToMinute, CalendarSource.Manual, CalendarStatus.Confirmed, null, Stamp)),
        ]);
    }

    // ---- engine -> oracle

    internal static OracleBar[] Bars(IReadOnlyList<Bar> bars) => [.. bars.Select(OracleBarOf)];

    internal static OracleBar OracleBarOf(Bar b) => new(b.EpochSeconds, b.Open, b.High, b.Low, b.Close, b.Volume);

    internal static OracleSide Side(Side side) => side == Domain.Research.Side.Buy ? OracleSide.Buy : OracleSide.Sell;

    internal static OracleCandidate Candidate(EntryCandidate c) => new(Side(c.Side), c.DecisionBarIndex, c.DecisionEpochSeconds, c.TriggerBar.Close, c.StopLevel);

    /// <summary>
    /// The oracle's rules of the engine's. The engine's calendar has no listing of its exceptions, so it is read the only way the walker reads it: by asking each date of the span (a few days before the first minute to forty-five days after the
    /// last: the walker looks at most thirty days ahead of a date it has read) whether it is a session and what its window is.
    /// </summary>
    internal static OracleRules Rules(WalkRules r, IReadOnlyList<Bar> bars)
    {
        OracleHold? hold = null;
        if (r.Hold is { } h)
        {
            hold = new OracleHold(h.MaxHoldSessions, h.Calendar is null ? null : Calendar(h.Calendar, bars));
        }

        var s = r.Fills.Slippage;
        return new OracleRules(r.RewardRisk, r.SquareOffMinute, r.Fills.EntryLimitPercent, new OracleSlippage(s.Ticks, s.BarRangeFrac, s.StopExitExtraTicks), r.Tick.Size, hold);
    }

    internal static OracleCalendar Calendar(TradingCalendar calendar, IReadOnlyList<Bar> bars)
    {
        var first = bars.Count == 0 ? new DateOnly(2026, 3, 2) : SessionClock.DateOf(bars[0].EpochSeconds);
        var last = bars.Count == 0 ? first : SessionClock.DateOf(bars[^1].EpochSeconds);
        var holidays = new List<DateOnly>();
        var specials = new Dictionary<DateOnly, OracleSession>();
        for (var date = first.AddDays(-7); date <= last.AddDays(45); date = date.AddDays(1))
        {
            var weekday = date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            var session = calendar.SessionOf(date);
            if (session is null)
            {
                if (weekday)
                {
                    holidays.Add(date);
                }
            }
            else if (!weekday || !session.Value.IsRegular)
            {
                specials[date] = new OracleSession(session.Value.FromMinute, session.Value.ToMinute);
            }
        }

        return new OracleCalendar(holidays, specials);
    }

    // ---- the engine's results, as the oracle's records

    /// <summary>The engine's outcome of one walk, in the oracle's records (the trace is empty: the engine has none). A failure of the walk is not an outcome: the test fails.</summary>
    internal static OracleResult Result(Result<WalkOutcome> walked)
    {
        walked.IsSuccess.ShouldBeTrue("the walk failed: " + (walked.IsFailure ? walked.Error.Code + " " + walked.Error.Message : string.Empty));
        return Result(walked.Value);
    }

    internal static OracleResult Result(WalkOutcome outcome) => new(
        outcome.Trade is { } t ? Trade(t) : null,
        outcome.NoTrade is { } n ? Enum.Parse<OracleNoTradeReason>(n.ToString()) : null,
        Counters(outcome.ToCounters()),
        new HashSet<string>(StringComparer.Ordinal),
        outcome.Trade is null ? null : outcome.HoldEndEpochSeconds);

    internal static OracleTrade Trade(SimTrade t) => new(
        Side(t.Side),
        t.TradeDate,
        t.DecisionEpochSeconds,
        t.EntryEpochSeconds,
        t.EntryPrice,
        t.StopPrice,
        t.TargetPrice,
        t.ExitEpochSeconds,
        t.ExitPrice,
        Enum.Parse<OracleExitReason>(t.ExitReason.ToString()),
        t.RExact,
        t.MaeR,
        t.MfeR,
        Enum.Parse<OracleFillBasis>(t.Fills.ToString()),
        t.RFilled,
        t.SlippageR,
        t.EntryFilledPrice,
        t.ExitFilledPrice,
        Enum.Parse<OracleEntryFill>(t.EntryFill.ToString()),
        t.SessionsHeld,
        t.SessionsUnseen);

    internal static OracleCounters Counters(WalkCounters w) => new()
    {
        Candidates = Int(w.Candidates),
        Trades = Int(w.Trades),
        NoFillBar = Int(w.NoFillBar),
        AfterSquareOff = Int(w.AfterSquareOff),
        EntryThroughStop = Int(w.EntryThroughStop),
        ExitsStop = Int(w.ExitsStop),
        ExitsTarget = Int(w.ExitsTarget),
        ExitsSquareOff = Int(w.ExitsSquareOff),
        ExitsEndOfData = Int(w.ExitsEndOfData),
        StopGapExits = Int(w.StopGapExits),
        TargetGapExits = Int(w.TargetGapExits),
        EntriesAfterMissingMinutes = Int(w.EntriesAfterMissingMinutes),
        BarsExamined = Int(w.BarsExamined),
        LimitNotReached = Int(w.LimitNotReached),
        EntriesAtOpen = Int(w.EntriesAtOpen),
        EntriesAtLimit = Int(w.EntriesAtLimit),
        AmbiguousMinutes = Int(w.AmbiguousMinutes),
        LateSquareOffs = Int(w.LateSquareOffs),
        ZeroVolumeMinutes = Int(w.ZeroVolumeMinutes),
        RiskUnderFourTicks = Int(w.RiskUnderFourTicks),
        PositionOpen = Int(w.PositionOpen),
        ExitsMaxHold = Int(w.ExitsMaxHold),
        MaxHoldAtLastClose = Int(w.MaxHoldAtLastClose),
        OvernightStopGaps = Int(w.OvernightStopGaps),
        OvernightTargetGaps = Int(w.OvernightTargetGaps),
        SessionsHeld = Int(w.SessionsHeld),
        SessionsUnseen = Int(w.SessionsUnseen),
        OvernightEntries = Int(w.OvernightEntries),
        OrderPendingAtEnd = Int(w.OrderPendingAtEnd),
    };

    private static int Int(long value) => checked((int)value);

    // ---- the engine's walks

    /// <summary>
    /// The engine's answer for the candidates of one instrument and side in decision order: each walked alone, or, when the rules hold a position overnight, through the real <see cref="PositionGate"/>. The echo fields of every trade
    /// (the pass-through rows of the dry-read table) are checked against the candidate that was walked and returned as mismatches.
    /// </summary>
    internal static (IReadOnlyList<OracleResult> Results, IReadOnlyList<Mismatch> Echoes, IReadOnlyList<bool> Blocked) Walk(IReadOnlyList<Bar> bars, IReadOnlyList<EntryCandidate> candidates, WalkRules rules)
    {
        var results = new List<OracleResult>();
        var echoes = new List<Mismatch>();
        var blocked = new List<bool>();
        if (rules.Hold is not null)
        {
            var gated = PositionGate.Apply(candidates, c => TradeWalker.Walk(c, bars, rules));
            gated.IsSuccess.ShouldBeTrue("the gate failed: " + (gated.IsFailure ? gated.Error.Code + " " + gated.Error.Message : string.Empty));
            foreach (var one in gated.Value)
            {
                results.Add(Result(one.Walk));
                blocked.Add(one.Walk.NoTrade == NoTradeReason.PositionOpen);
                echoes.AddRange(Echo(one.Walk.Trade, one.Candidate, rules));
            }
        }
        else
        {
            foreach (var candidate in candidates)
            {
                var walked = TradeWalker.Walk(candidate, bars, rules);
                results.Add(Result(walked));
                blocked.Add(false);
                echoes.AddRange(Echo(walked.Value.Trade, candidate, rules));
            }
        }

        return (results, echoes, blocked);
    }

    /// <summary>The pass-through rows of the dry-read table: what the trade must echo of its candidate and its rules (schema 4, the strategy and instrument, the trigger bar, no snapshot, no costing, the tick flag).</summary>
    internal static IEnumerable<Mismatch> Echo(SimTrade? trade, EntryCandidate candidate, WalkRules rules)
    {
        if (trade is null)
        {
            yield break;
        }

        var expected = new (string Field, object? Value)[]
        {
            (nameof(SimTrade.SchemaVersion), SimTrade.CurrentSchemaVersion),
            (nameof(SimTrade.StrategyId), candidate.StrategyId),
            (nameof(SimTrade.InstrumentId), candidate.Instrument.Id),
            (nameof(SimTrade.Symbol), candidate.Instrument.Symbol),
            (nameof(SimTrade.Side), candidate.Side),
            (nameof(SimTrade.TriggerTimeframe), candidate.TriggerTimeframe),
            (nameof(SimTrade.TriggerBar), candidate.TriggerBar),
            (nameof(SimTrade.TriggerBarMinutes), candidate.TriggerBarMinutes),
            (nameof(SimTrade.TriggerBarComplete), candidate.TriggerBarComplete),
            (nameof(SimTrade.EntrySnapshotJson), null),
            (nameof(SimTrade.Costs), CostBasis.None),
            (nameof(SimTrade.Costing), null),
            (nameof(SimTrade.TickFallback), rules.Tick.FromFallback),
        };
        var actual = new Dictionary<string, object?>
        {
            [nameof(SimTrade.SchemaVersion)] = trade.SchemaVersion,
            [nameof(SimTrade.StrategyId)] = trade.StrategyId,
            [nameof(SimTrade.InstrumentId)] = trade.InstrumentId,
            [nameof(SimTrade.Symbol)] = trade.Symbol,
            [nameof(SimTrade.Side)] = trade.Side,
            [nameof(SimTrade.TriggerTimeframe)] = trade.TriggerTimeframe,
            [nameof(SimTrade.TriggerBar)] = trade.TriggerBar,
            [nameof(SimTrade.TriggerBarMinutes)] = trade.TriggerBarMinutes,
            [nameof(SimTrade.TriggerBarComplete)] = trade.TriggerBarComplete,
            [nameof(SimTrade.EntrySnapshotJson)] = trade.EntrySnapshotJson,
            [nameof(SimTrade.Costs)] = trade.Costs,
            [nameof(SimTrade.Costing)] = trade.Costing,
            [nameof(SimTrade.TickFallback)] = trade.TickFallback,
        };
        foreach (var (field, value) in expected)
        {
            if (!Equals(value, actual[field]))
            {
                yield return new Mismatch(field, Show(value), Show(actual[field]));
            }
        }
    }

    internal static string Show(object? value) => value switch
    {
        null => "null",
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };
}
