using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Scanning;

/// <summary>
/// The ticket builder (spec P7-04, ADR 0088): classifies the setup of a stored strategy for an instrument from the bars up to the close of D, with Kleene logic, and states the levels as of that close. Pure and deterministic: no
/// clock, no I/O. <b>Bars at or after the close of D are dropped before anything is computed</b> (<see cref="TicketBars.Prepare"/>), so nothing after the close can change a ticket (the truncation property).
/// <para>
/// A condition is <i>known at the close</i> when it reads only 1D, 1W or 1M features and not the clock, a market context or expiry days. Every other condition is Unknown whatever the frame says at the last minute of D, because an
/// intraday value at that minute describes D, not tomorrow. A known condition that is Unknown is data not ready (also: a weekly or monthly part without a trading calendar). Such parts are enumerated as true and false: when the outcome
/// is not the same for every assignment the result is Unavailable, never a guess. The same condition text in two lists is one part.
/// </para>
/// </summary>
public static class TicketBuilder
{
    /// <summary>Sessions of turnover averaged.</summary>
    public const int TurnoverSessions = 20;

    /// <summary>Not-ready parts enumerated at most; above it the result is Unavailable.</summary>
    public const int MaxNotReadyParts = 8;

    /// <summary>Reads the stored text, checks its hash, compiles the side and builds the draft. Anything that fails is an Unavailable draft with the reason. Prefer <see cref="PreparedTicketStrategy.Prepare"/> once per cell.</summary>
    public static TicketDraft Classify(string sourceText, string expectedHash, Timeframe timeframe, Side side, string cellStyle, string cellExit, string symbol, DateOnly session, DateOnly forDate, IEnumerable<Bar> bars, TradingCalendar? calendar) =>
        Build(PreparedTicketStrategy.Prepare(sourceText, expectedHash, timeframe, side, cellStyle, cellExit), TicketBars.Prepare(bars, session, calendar), symbol, session, forDate, calendar);

    /// <summary>Builds the draft from a compiled side and raw bars (tests); bars at or after the close of <paramref name="session"/> are ignored.</summary>
    public static TicketDraft Build(CompiledSide side, string symbol, string strategyHash, string cellStyle, string cellExit, DateOnly session, DateOnly forDate, IEnumerable<Bar> bars, TradingCalendar? calendar)
    {
        ArgumentNullException.ThrowIfNull(bars);
        return Build(PreparedTicketStrategy.FromSide(side, strategyHash, cellStyle, cellExit), TicketBars.Prepare(bars, session, calendar), symbol, session, forDate, calendar);
    }

    /// <summary>Builds the draft of one instrument for one prepared cell strategy.</summary>
    public static TicketDraft Build(PreparedTicketStrategy strategy, TicketBars bars, string symbol, DateOnly session, DateOnly forDate, TradingCalendar? calendar)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(bars);
        if (strategy.Compiled is not { } side || strategy.Problem is not null)
        {
            return Unavailable(symbol, strategy.SideText, strategy.StrategyId, strategy.Hash, session, strategy.Problem ?? "the strategy is not usable");
        }

        if (!bars.IsComplete || bars.Session != session)
        {
            return Unavailable(symbol, strategy.SideText, side.StrategyId, strategy.Hash, session, "the session " + Date(session) + " is not complete in the bars (its last minute is missing)");
        }

        var created = FeatureFrame.Create(side.Features, new FrameOptions(calendar, Math.Max(side.HistoryDepth, 1)));
        if (created.IsFailure)
        {
            return Unavailable(symbol, strategy.SideText, side.StrategyId, strategy.Hash, session, "the feature frame was refused (" + created.Error.Code + ")");
        }

        var frame = created.Value;
        var bound = side.Bind(frame);
        var stepped = strategy.DailyOnly ? bars.Daily : bars.Minutes;
        for (var i = 0; i < stepped.Count; i++)
        {
            frame.Step(stepped[i]);
        }

        var truths = new Truth[side.Items.Count];
        bound.Evaluate(frame, truths);
        var parts = new List<TicketPart>(truths.Length);
        for (var i = 0; i < truths.Length; i++)
        {
            parts.Add(Classify(side.Items[i], truths[i], calendar is not null));
        }

        var setupCount = side.Setup?.Items.Count ?? 0;
        var triggerCount = side.Trigger.Items.Count;
        var setupMode = side.Setup?.Mode;
        var invalidateMode = side.Invalidate?.Mode;
        SetupTruth Lists(Func<TicketPart, SetupTruth> view) =>
            Kleene.And(
                Kleene.And(Fold(parts.Take(setupCount), setupMode, view, SetupTruth.True), Fold(parts.Skip(setupCount).Take(triggerCount), side.Trigger.Mode, view, SetupTruth.True)),
                Kleene.Not(Fold(parts.Skip(setupCount + triggerCount), invalidateMode, view, SetupTruth.False)));

        var setup = Fold(parts.Take(setupCount), setupMode, p => p.Truth, SetupTruth.True);
        var trigger = Fold(parts.Skip(setupCount).Take(triggerCount), side.Trigger.Mode, p => p.Truth, SetupTruth.True);
        var invalidate = Fold(parts.Skip(setupCount + triggerCount), invalidateMode, p => p.Truth, SetupTruth.False);

        // The parts that should be known but are not ready, one per condition text (the same text in two lists is the same part). Every assignment of true and false is tried: a part in the setup helps when true, one in the
        // invalidate list helps when false, so no single forcing is the best or the worst case for all.
        var notReady = parts.Where(p => p.KnownAtClose && p.Truth == SetupTruth.Unknown).Select(p => p.Text).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (notReady.Count > MaxNotReadyParts)
        {
            return Unavailable(symbol, strategy.SideText, side.StrategyId, strategy.Hash, session, "not ready at the close of " + Date(session) + ": " + notReady.Count + " conditions");
        }

        var outcomes = new HashSet<SetupTruth>();
        for (var mask = 0; mask < (1 << notReady.Count); mask++)
        {
            var current = mask;
            outcomes.Add(Lists(p => p.KnownAtClose && p.Truth == SetupTruth.Unknown ? ((current >> notReady.IndexOf(p.Text)) & 1) == 1 ? SetupTruth.True : SetupTruth.False : p.Truth));
        }

        var common = new Common(symbol, strategy.SideText, side.StrategyId, strategy.Hash, session, setup, trigger, invalidate, outcomes.Count == 1 ? outcomes.Single() : SetupTruth.Unknown, parts);
        if (outcomes.Count == 1 && outcomes.Single() == SetupTruth.False)
        {
            var why = parts.FirstOrDefault(p => p.Truth == SetupTruth.False);
            return Draft(common, TicketVerdict.Rejected, why is null ? "the invalidate list holds, or no way to satisfy the conditions, at the close of " + Date(session) : "known at the close of " + Date(session) + ": " + why.Path + " is false", side, strategy.Exit, frame, bars, forDate, "no ticket");
        }

        if (outcomes.Count > 1)
        {
            return Unavailable(symbol, strategy.SideText, side.StrategyId, strategy.Hash, session, "not ready at the close of " + Date(session) + ": " + string.Join(", ", parts.Where(p => p.KnownAtClose && p.Truth == SetupTruth.Unknown).Select(p => p.Path)));
        }

        var overall = outcomes.Single();
        var reason = overall == SetupTruth.True
            ? "known at the close of " + Date(session) + ": the setup and the trigger hold"
            : "setup not false at the close of " + Date(session) + "; still to watch: " + parts.Count(p => p.Truth == SetupTruth.Unknown && !p.KnownAtClose) + " condition(s) knowable only tomorrow";
        return Draft(common, TicketVerdict.Conditional, reason, side, strategy.Exit, frame, bars, forDate, null);
    }

    private static SetupTruth Fold(IEnumerable<TicketPart> part, ListMode? mode, Func<TicketPart, SetupTruth> view, SetupTruth empty) =>
        mode is null ? empty : Kleene.Fold(part.Select(view), mode == ListMode.All);

    private static TicketPart Classify(CompiledItem item, Truth truth, bool hasCalendar)
    {
        var c = item.Condition;
        var known = !c.ReadsClock && !c.NeedsContext && !c.NeedsExpiryDays && c.Features.All(f => f.Timeframe >= Timeframe.D1);
        if (!known)
        {
            return new TicketPart(item.Path, c.Source, SetupTruth.Unknown, false, "knowable only on tomorrow's bars (reads intraday data, the clock or a market context)");
        }

        if (!hasCalendar && c.Features.Any(f => f.Timeframe is Timeframe.W1 or Timeframe.Mo1))
        {
            return new TicketPart(item.Path, c.Source, SetupTruth.Unknown, true, "weekly or monthly data needs the trading calendar to close its bars");
        }

        return truth switch
        {
            Truth.True => new TicketPart(item.Path, c.Source, SetupTruth.True, true, "true at the close"),
            Truth.False => new TicketPart(item.Path, c.Source, SetupTruth.False, true, "false at the close"),
            _ => new TicketPart(item.Path, c.Source, SetupTruth.Unknown, true, "data not ready at the close"),
        };
    }

    private sealed record Common(string Symbol, string Side, string StrategyId, string Hash, DateOnly Session, SetupTruth Setup, SetupTruth Trigger, SetupTruth Invalidate, SetupTruth Overall, IReadOnlyList<TicketPart> Parts);

    private static TicketDraft Draft(Common c, TicketVerdict verdict, string reason, CompiledSide side, ExitMatch? exit, FeatureFrame frame, TicketBars bars, DateOnly forDate, string? onlyText)
    {
        var buy = side.Side == Side.Buy;
        var entry = bars.Minutes[^1].Close;
        var (stopText, stopLevel) = Stop(side, frame, buy, entry);
        var (targetText, rr, targetLevel) = Target(exit, buy, entry, stopLevel);
        var watch = c.Parts.Where(p => p.Truth == SetupTruth.Unknown && !p.KnownAtClose).Select(p => p.Text).ToList();
        var mode = side.Trigger.Mode == ListMode.All ? "all" : "any";
        var trigger = onlyText ?? (watch.Count == 0
            ? "nothing left to watch: the signal holds at the close of " + Date(c.Session) + "; the entry is at the next session's open within the entry limit"
            : "known at the close of " + Date(c.Session) + ": " + c.Parts.Count(p => p.Truth == SetupTruth.True) + " true. Watch tomorrow (" + mode + " of the trigger, other parts as listed): " + string.Join("; ", watch));
        var levels = stopLevel is null && targetLevel is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (indicative from the close {entry:0.##})");
        var unavailable = exit is null ? new[] { "target" } : [];
        return new TicketDraft(
            verdict, c.Symbol, c.Side, c.StrategyId, c.Hash, c.Session, c.Setup, c.Trigger, c.Invalidate, c.Overall, c.Parts, reason,
            trigger, stopText + (stopLevel is null ? string.Empty : levels), targetText + (targetLevel is null ? string.Empty : levels), Validity(side, forDate), stopLevel, entry, rr, bars.Turnover, unavailable,
            targetLevel, StopAnchorOf(side.Stop.Kind, stopLevel));
    }

    // Percent and ATR stops hang on the close of D; structure stops are market levels; the opening range is intraday and has no number to store (P7-19).
    private static StopAnchor? StopAnchorOf(StopKind kind, double? level) => level is null ? null : kind switch
    {
        StopKind.FixedPct or StopKind.AtrMult => StopAnchor.CloseOfD,
        StopKind.OrOpposite => null,
        _ => StopAnchor.Level,
    };

    private static (string Text, double? Level) Stop(CompiledSide side, FeatureFrame frame, bool buy, double entry)
    {
        var stop = side.Stop;
        var buffer = stop.BufferAtr;
        double? Read(FeatureSpec? spec)
        {
            if (spec is not { } s || s.Timeframe < Timeframe.D1)
            {
                return null;
            }

            var handle = frame.HandleOf(s);
            return handle < 0 || double.IsNaN(frame[handle]) ? null : frame[handle];
        }

        var atr = Read(stop.Atr);
        var sign = buy ? -1 : 1;
        var name = stop.Kind switch
        {
            StopKind.FixedPct => "fixed percentage of the entry price",
            StopKind.AtrMult => "multiple of the ATR from the entry price",
            StopKind.SwingLow => "below the lowest low of the lookback",
            StopKind.SwingHigh => "above the highest high of the lookback",
            StopKind.BarLow => "below the low of the trigger bar",
            StopKind.BarHigh => "above the high of the trigger bar",
            _ => "beyond the opposite end of the opening range",
        };
        double? level = stop.Kind switch
        {
            StopKind.FixedPct when stop.Pct is { } pct => entry * (1 + (sign * pct / 100)),
            StopKind.AtrMult when stop.Mult is { } mult && atr is { } a => entry + (sign * mult * a),
            StopKind.SwingLow or StopKind.SwingHigh or StopKind.BarLow or StopKind.BarHigh or StopKind.OrOpposite when Read(stop.Level) is { } l && (buffer <= 0 || atr is not null) => l + (sign * buffer * (atr ?? 0)),
            _ => null,
        };
        var text = "stop " + name + (stop.BufferAtr > 0 ? string.Create(CultureInfo.InvariantCulture, $" plus {buffer:0.##} ATR") : string.Empty)
            + (level is { } v ? string.Create(CultureInfo.InvariantCulture, $", indicative level {v:0.##} as of the close") : ", level set on tomorrow's data");
        return (text, level);
    }

    private static (string Text, double? RewardRisk, double? Level) Target(ExitMatch? exit, bool buy, double entry, double? stopLevel)
    {
        if (exit is null)
        {
            return ("target unavailable: the cell's exit is not one of the strategy's exits", null, null);
        }

        if (exit.RewardRisk is { } rr)
        {
            double? level = stopLevel is { } s ? entry + ((buy ? 1 : -1) * rr * Math.Abs(entry - s)) : null;
            var text = string.Create(CultureInfo.InvariantCulture, $"target {rr:0.##} x the risk from the entry ({exit.Id})") + (level is { } l ? string.Create(CultureInfo.InvariantCulture, $", indicative level {l:0.##}") : string.Empty);
            return (text, rr, level);
        }

        return ("exit " + exit.Id + " (a rule; no fixed target level)", null, null);
    }

    private static string Validity(CompiledSide side, DateOnly forDate)
    {
        var rules = side.TimeRules;
        var date = Date(forDate);
        var hold = rules.MaxHoldSessions is { } n ? string.Create(CultureInfo.InvariantCulture, $"; held {n} sessions at most") : string.Empty;
        if (side.Style == Style.Swing)
        {
            return "valid for " + date + " only; entry at the next session's open within the entry limit" + hold;
        }

        string Minute(int m) => string.Create(CultureInfo.InvariantCulture, $"{m / 60:00}:{m % 60:00}");
        var window = (rules.FirstEntryMinute, rules.LastEntryMinute) switch
        {
            ({ } a, { } b) => "entries " + Minute(a) + " to " + Minute(b),
            ({ } a, null) => "entries from " + Minute(a),
            (null, { } b) => "entries until " + Minute(b),
            _ => "entries during the session",
        };
        return "valid for " + date + " only; " + window + (rules.SquareOffMinute is { } q ? ", square off " + Minute(q) : string.Empty) + hold;
    }

    private static TicketDraft Unavailable(string symbol, string side, string strategyId, string hash, DateOnly session, string reason) =>
        new(TicketVerdict.Unavailable, symbol, side, strategyId, hash, session, SetupTruth.Unknown, SetupTruth.Unknown, SetupTruth.Unknown, SetupTruth.Unknown, [], reason, string.Empty, string.Empty, string.Empty, string.Empty, null, null, null, null, ["setup"]);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
