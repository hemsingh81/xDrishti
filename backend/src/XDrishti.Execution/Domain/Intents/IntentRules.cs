using XDrishti.Domain.Market;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Domain.Intents;

/// <summary>The settings the intent creator reads, from the newest good settings read: the entry window, P12-03's price collar, the canary flag and the age at which a live price is stale.</summary>
public sealed record IntentSettings(TimeOnly EntryWindowStart, TimeOnly EntryWindowEnd, decimal CollarPct, bool CanaryPending, TimeSpan QuoteMaxAge);

/// <summary>A last traded price and when the feed stamped it.</summary>
public sealed record LivePrice(decimal Last, DateTimeOffset At);

/// <summary>The reason codes the intent creator writes on a cancelled intent and the codes of a candidate it does not turn into an intent. Short words, never a price, an account or a name.</summary>
public static class IntentReasons
{
    public const string Void = "void";
    public const string MissedNotReady = "missed_not_ready";
    public const string Halted = "halted";
    public const string Window = "window";
    public const string ModeChanged = "mode_changed";
    public const string PlanInvalidated = "plan_invalidated";
    public const string PlanExpired = "plan_expired";
    public const string Triggered = "triggered";
    public const string Armed = "armed";
    public const string LevelsMissing = "levels_missing";
    public const string InstrumentUnknown = "instrument_unknown";
}

/// <summary>
/// The pure rules of the intent creator (spec P12-08 criterion 16). Every input is an argument; an unreadable or stale fact is the failing side.
/// </summary>
public static class IntentRules
{
    /// <summary>A planned intent is armed no earlier than this India time on its session, once the plan ticket is armed by the pre-open check.</summary>
    public static readonly TimeOnly ArmFrom = new(9, 10);

    /// <summary>
    /// The newest readiness check counts only while it is this young: the readiness loop stores a row at least every 15 minutes (ADR 0164), so an older row means the loop is not running, which is unavailable and blocks like not ready.
    /// </summary>
    public static readonly TimeSpan ReadinessMaxAge = TimeSpan.FromMinutes(16);

    /// <summary>The quantity of a new intent: the allocation, or one share while the canary is pending (P12-00; P12-03's <c>canary_cap</c> refuses more). The allocation must be positive.</summary>
    public static int Quantity(int allocation, bool canaryPending) => canaryPending ? Math.Min(allocation, 1) : allocation;

    public static bool IsFresh(LivePrice? price, DateTimeOffset now, TimeSpan maxAge) =>
        price is { Last: > 0 } p && p.At <= now + TimeSpan.FromSeconds(1) && now - p.At <= maxAge;

    /// <summary>Ready only on a ready verdict of this session that is younger than <see cref="ReadinessMaxAge"/>. No check, another session, a stale check, not ready and unavailable all fail.</summary>
    public static bool IsReady(ReadinessState? verdict, DateOnly? checkSession, DateTimeOffset? checkAt, DateOnly session, DateTimeOffset now) =>
        verdict == ReadinessState.Ready && checkSession == session && checkAt is { } at && at <= now + TimeSpan.FromSeconds(1) && now - at <= ReadinessMaxAge;

    /// <summary>Inside the entry window of <paramref name="session"/> (both ends included, as P12-03's <c>session_window</c> check).</summary>
    public static bool InWindow(DateTimeOffset now, DateOnly session, IntentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var ist = IndiaTime.ToIst(now);
        var time = TimeOnly.FromDateTime(ist.DateTime);
        return DateOnly.FromDateTime(ist.DateTime) == session && time >= settings.EntryWindowStart && time <= settings.EntryWindowEnd;
    }

    /// <summary>Past the end of the entry window of <paramref name="session"/>, or a later day: a trigger can no longer be acted on (no catch-up).</summary>
    public static bool WindowOver(DateTimeOffset now, DateOnly session, IntentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var ist = IndiaTime.ToIst(now);
        var today = DateOnly.FromDateTime(ist.DateTime);
        return today > session || (today == session && TimeOnly.FromDateTime(ist.DateTime) > settings.EntryWindowEnd);
    }

    public static bool MayArm(DateTimeOffset now, DateOnly session)
    {
        var ist = IndiaTime.ToIst(now);
        return DateOnly.FromDateTime(ist.DateTime) == session && TimeOnly.FromDateTime(ist.DateTime) >= ArmFrom;
    }

    /// <summary>
    /// What a crossing does: <c>null</c> moves the intent to checking; otherwise the reason it is cancelled. The strongest cause wins: a halt (or an unreadable halt record), then an account that may no longer act (mode or switch), then
    /// readiness, then the window.
    /// </summary>
    public static string? OnCrossing(bool halted, bool mayAct, bool ready, bool inWindow) =>
        halted ? IntentReasons.Halted
        : !mayAct ? IntentReasons.ModeChanged
        : !ready ? IntentReasons.MissedNotReady
        : !inWindow ? IntentReasons.Window
        : null;
}
