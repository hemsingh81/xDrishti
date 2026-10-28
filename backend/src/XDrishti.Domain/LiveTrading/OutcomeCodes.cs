namespace XDrishti.Domain.LiveTrading;

/// <summary>The stored words of the live capture (spec P11-01, ADR 0149). Never rename: they are in the table, the API and the screen.</summary>
public static class OutcomeCodes
{
    public const string Filled = "filled";
    public const string Partial = "partial";
    public const string OverAllocation = "over_allocation";
    public const string NotTriggered = "not_triggered";
    public const string MissedNoOrder = "missed_no_order";
    public const string MissedUnfilled = "missed_unfilled";
    public const string Rejected = "rejected";
    public const string MissedUnknown = "missed_unknown";
    public const string Unknown = "unknown";

    public static IReadOnlyList<string> Outcomes { get; } = [Filled, Partial, OverAllocation, NotTriggered, MissedNoOrder, MissedUnfilled, Rejected, MissedUnknown, Unknown];

    /// <summary>The two outcomes that carry a missed R.</summary>
    public static bool CarriesMissedR(string outcome) => outcome is MissedNoOrder or MissedUnfilled;

    public const string FlagOverAllocation = "over_allocation";
    public const string FlagSizeBelowAllocation = "size_below_allocation";
    public const string FlagChasedEntry = "chased_entry";
    public const string FlagUnprotected = "unprotected";

    public const string Protected = "protected";
    public const string Unprotected = "unprotected";
    public const string ProtectionUnknown = "protection_unknown";

    /// <summary>The ticket has no entry fill, so there is no position to protect.</summary>
    public const string NotApplicable = "not_applicable";

    /// <summary>Every order-dependent field when the session has no order evidence: not a pass, not a guess.</summary>
    public const string Unavailable = "unavailable";

    public const string EvidencePresent = "present";
    public const string EvidenceUnavailable = "unavailable";

    public const string KindPoll = "poll";
    public const string KindSettle = "settle";

    public const string RunOk = "ok";
    public const string RunPartial = "partial";
    public const string RunTokenExpired = "token_expired";
    public const string RunRateLimited = "rate_limited";
    public const string RunUnavailable = "unavailable";
    public const string RunRejected = "rejected";

    public static IReadOnlyList<string> RunOutcomes { get; } = [RunOk, RunPartial, RunTokenExpired, RunRateLimited, RunUnavailable, RunRejected];
}
