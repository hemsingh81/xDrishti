using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.File;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Scanning;

/// <summary>
/// The ticket builder (spec P7-04, ADR 0088): classifies the setup of a stored strategy for an instrument from the 1-minute bars up to the close of D, with Kleene logic, and states the levels as of that close. Pure and
/// deterministic: no clock, no I/O. <b>Bars at or after the close of D are dropped before anything is computed</b>, so nothing after the close can change a ticket (the truncation property).
/// <para>
/// A condition is <i>known at the close</i> when it reads only 1D, 1W or 1M features and not the clock, a market context or expiry days. Every other condition is Unknown whatever the frame says at the last minute of D, because an
/// intraday value at that minute describes D, not tomorrow. A known condition that is Unknown is data not ready: when that could change the outcome, the result is Unavailable, never a guess.
/// </para>
/// </summary>
public static class TicketBuilder
{
    /// <summary>Sessions of turnover averaged.</summary>
    public const int TurnoverSessions = 20;

    private const long IndiaOffsetSeconds = 19800;

    /// <summary>Reads the stored text, checks its hash, compiles the side for the timeframe and builds the draft. Anything that fails is an Unavailable draft with the reason.</summary>
    public static TicketDraft Classify(string sourceText, string expectedHash, Timeframe timeframe, Side side, string symbol, DateOnly session, DateOnly forDate, IEnumerable<Bar> bars, TradingCalendar? calendar)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(expectedHash);
        ArgumentNullException.ThrowIfNull(bars);
        var sideText = side == Side.Buy ? "BUY" : "SELL";
        var read = StrategyFile.Read(sourceText);
        if (read.Definition is not { } definition || read.HasErrors)
        {
            return Unavailable(symbol, sideText, string.Empty, expectedHash, session, "the stored strategy text does not read");
        }

        if (!string.Equals(CanonicalText.Sha256Hex(CanonicalStrategy.Json(definition)), expectedHash, StringComparison.Ordinal))
        {
            return Unavailable(symbol, sideText, definition.Id, expectedHash, session, "the stored text does not give the stored hash");
        }

        var compiled = StrategyCompiler.Compile(definition, timeframe, side);
        if (compiled.Compiled is not { } compiledSide)
        {
            return Unavailable(symbol, sideText, definition.Id, expectedHash, session, "the strategy does not compile for " + timeframe.ToCode() + " " + sideText);
        }

        return Build(compiledSide, symbol, expectedHash, session, forDate, bars, calendar);
    }

    /// <summary>Builds the draft from a compiled side and the bars; bars at or after the close of <paramref name="session"/> are ignored.</summary>
    public static TicketDraft Build(CompiledSide side, string symbol, string strategyHash, DateOnly session, DateOnly forDate, IEnumerable<Bar> bars, TradingCalendar? calendar)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(bars);
        var sideText = side.Side == Side.Buy ? "BUY" : "SELL";
        var cut = new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.FromSeconds(IndiaOffsetSeconds)).AddMinutes(SessionClock.CloseMinute).ToUnixTimeSeconds();
        var kept = bars.Where(b => b.EpochSeconds < cut).OrderBy(b => b.EpochSeconds).ToList();
        if (kept.Count == 0 || kept[^1].EpochSeconds != cut - 60)
        {
            return Unavailable(symbol, sideText, side.StrategyId, strategyHash, session, "the session " + Date(session) + " is not complete in the bars (its last minute is missing)");
        }

        var created = FeatureFrame.Create(side.Features, new FrameOptions(calendar, Math.Max(side.HistoryDepth, 1)));
        if (created.IsFailure)
        {
            return Unavailable(symbol, sideText, side.StrategyId, strategyHash, session, "the feature frame was refused (" + created.Error.Code + ")");
        }

        var frame = created.Value;
        var bound = side.Bind(frame);
        foreach (var bar in kept)
        {
            frame.Step(bar);
        }

        var truths = new Truth[side.Items.Count];
        bound.Evaluate(frame, truths);
        var parts = new List<TicketPart>(truths.Length);
        for (var i = 0; i < truths.Length; i++)
        {
            parts.Add(Classify(side.Items[i], truths[i]));
        }

        var setupCount = side.Setup?.Items.Count ?? 0;
        var triggerCount = side.Trigger.Items.Count;
        SetupTruth Lists(Func<TicketPart, SetupTruth> view) =>
            Kleene.And(
                Kleene.And(Fold(parts.Take(setupCount), side.Setup?.Mode, view), Fold(parts.Skip(setupCount).Take(triggerCount), side.Trigger.Mode, view)),
                Kleene.Not(Fold(parts.Skip(setupCount + triggerCount), side.Invalidate?.Mode, view, empty: SetupTruth.False)));

        var setup = Fold(parts.Take(setupCount), side.Setup?.Mode, p => p.Truth);
        var trigger = Fold(parts.Skip(setupCount).Take(triggerCount), side.Trigger.Mode, p => p.Truth);
        var invalidate = Fold(parts.Skip(setupCount + triggerCount), side.Invalidate?.Mode, p => p.Truth, empty: SetupTruth.False);
        var overall = Lists(p => p.Truth);
        var notReady = parts.Where(p => p.KnownAtClose && p.Truth == SetupTruth.Unknown).ToList();
        var common = new Common(symbol, sideText, side.StrategyId, strategyHash, session, setup, trigger, invalidate, overall, parts);

        if (overall == SetupTruth.False)
        {
            var why = parts.FirstOrDefault(p => p.Truth == SetupTruth.False);
            return Draft(common, TicketVerdict.Rejected, why is null ? "the invalidate list holds at the close of " + Date(session) : "known at the close of " + Date(session) + ": " + why.Path + " is false", side, frame, kept, forDate, [], "no ticket");
        }

        // Data not ready matters only when forcing those parts True or False would change the outcome.
        if (notReady.Count > 0 && Lists(p => Forced(p, SetupTruth.True)) != Lists(p => Forced(p, SetupTruth.False)))
        {
            return Unavailable(symbol, sideText, side.StrategyId, strategyHash, session, "not ready at the close of " + Date(session) + ": " + string.Join(", ", notReady.Select(p => p.Path)));
        }

        var reason = overall == SetupTruth.True
            ? "known at the close of " + Date(session) + ": the setup and the trigger hold"
            : "setup not false at the close of " + Date(session) + "; still to watch: " + parts.Count(p => p.Truth == SetupTruth.Unknown && !p.KnownAtClose) + " condition(s) knowable only tomorrow";
        return Draft(common, TicketVerdict.Conditional, reason, side, frame, kept, forDate, [], null);
    }

    private static SetupTruth Forced(TicketPart part, SetupTruth value) => part.KnownAtClose && part.Truth == SetupTruth.Unknown ? value : part.Truth;

    private static SetupTruth Fold(IEnumerable<TicketPart> part, ListMode? mode, Func<TicketPart, SetupTruth> view, SetupTruth empty = SetupTruth.True) =>
        mode is null ? empty : Kleene.Fold(part.Select(view), mode == ListMode.All);

    private static TicketPart Classify(CompiledItem item, Truth truth)
    {
        var c = item.Condition;
        var known = !c.ReadsClock && !c.NeedsContext && !c.NeedsExpiryDays && c.Features.All(f => f.Timeframe >= Timeframe.D1);
        if (!known)
        {
            return new TicketPart(item.Path, c.Source, SetupTruth.Unknown, false, "knowable only on tomorrow's bars (reads intraday data, the clock or a market context)");
        }

        return truth switch
        {
            Truth.True => new TicketPart(item.Path, c.Source, SetupTruth.True, true, "true at the close"),
            Truth.False => new TicketPart(item.Path, c.Source, SetupTruth.False, true, "false at the close"),
            _ => new TicketPart(item.Path, c.Source, SetupTruth.Unknown, true, "data not ready at the close"),
        };
    }

    private sealed record Common(string Symbol, string Side, string StrategyId, string Hash, DateOnly Session, SetupTruth Setup, SetupTruth Trigger, SetupTruth Invalidate, SetupTruth Overall, IReadOnlyList<TicketPart> Parts);

    private static TicketDraft Draft(Common c, TicketVerdict verdict, string reason, CompiledSide side, FeatureFrame frame, List<Bar> kept, DateOnly forDate, IReadOnlyList<string> unavailable, string? onlyText)
    {
        var buy = side.Side == Side.Buy;
        var entry = kept[^1].Close;
        var (stopText, stopLevel) = Stop(side, frame, buy, entry);
        var (targetText, rr, targetLevel) = Target(side, buy, entry, stopLevel);
        var watch = c.Parts.Where(p => p.Truth == SetupTruth.Unknown && !p.KnownAtClose).Select(p => p.Text).ToList();
        var mode = side.Trigger.Mode == ListMode.All ? "all" : "any";
        var trigger = onlyText ?? (watch.Count == 0
            ? "nothing left to watch: the signal holds at the close of " + Date(c.Session) + "; the entry is at the next session's open within the entry limit"
            : "known at the close of " + Date(c.Session) + ": " + c.Parts.Count(p => p.Truth == SetupTruth.True) + " true. Watch tomorrow (" + mode + " of the trigger, other parts as listed): " + string.Join("; ", watch));
        var levels = stopLevel is null && targetLevel is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (indicative from the close {entry:0.##})");
        return new TicketDraft(
            verdict, c.Symbol, c.Side, c.StrategyId, c.Hash, c.Session, c.Setup, c.Trigger, c.Invalidate, c.Overall, c.Parts, reason,
            trigger, stopText, targetText + levels, Validity(side, forDate), stopLevel, entry, rr, Turnover(kept), unavailable);
    }

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
            + (level is { } v ? string.Create(CultureInfo.InvariantCulture, $", level {v:0.##} as of the close") : ", level set on tomorrow's data");
        return (text, level);
    }

    private static (string Text, double? RewardRisk, double? Level) Target(CompiledSide side, bool buy, double entry, double? stopLevel)
    {
        var exit = side.Exits.FirstOrDefault(e => e.Policy == ExitPolicy.FixedRr);
        if (exit is not null && FirstValue(exit) is { } rr && rr > 0)
        {
            double? level = stopLevel is { } s ? entry + ((buy ? 1 : -1) * rr * Math.Abs(entry - s)) : null;
            var text = string.Create(CultureInfo.InvariantCulture, $"target {rr:0.##} x the risk from the entry") + (level is { } l ? string.Create(CultureInfo.InvariantCulture, $", level {l:0.##}") : string.Empty);
            return (text, rr, level);
        }

        return (side.Exits.Count == 0 ? "target per the cell's exit" : "exit policy " + side.Exits[0].Policy + " (a rule; no fixed target level)", null, null);
    }

    private static double? FirstValue(ExitPlan exit)
    {
        var first = exit.Values.Values.FirstOrDefault();
        return first is { Count: > 0 } ? first[0] : null;
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

    private static double? Turnover(List<Bar> kept)
    {
        var days = kept.GroupBy(b => SessionClock.DateOf(b.EpochSeconds)).OrderBy(g => g.Key).ToList();
        if (days.Count < TurnoverSessions)
        {
            return null;
        }

        return days.TakeLast(TurnoverSessions).Average(g => g.Sum(b => b.Close * b.Volume));
    }

    private static TicketDraft Unavailable(string symbol, string side, string strategyId, string hash, DateOnly session, string reason) =>
        new(TicketVerdict.Unavailable, symbol, side, strategyId, hash, session, SetupTruth.Unknown, SetupTruth.Unknown, SetupTruth.Unknown, SetupTruth.Unknown, [], reason, string.Empty, string.Empty, string.Empty, string.Empty, null, null, null, null, ["setup"]);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
