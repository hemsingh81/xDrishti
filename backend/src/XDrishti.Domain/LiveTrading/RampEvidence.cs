namespace XDrishti.Domain.LiveTrading;

/// <summary>One closed round trip of the step. <see cref="R"/> is the gross result in R of the planned risk per share, null when it cannot be fixed (then <see cref="Uncertain"/>). <see cref="ChargesShare"/> is its recorded charges as a share of 1 R, when known.</summary>
public sealed record TripFact(double? R, bool Uncertain, double? ChargesShare);

/// <summary>
/// Everything the gates and the breach rules read, already counted by the evidence reader (spec P11-04). Any number the sources could not give is null (or the flag says so): the gate then reads <c>unavailable</c>, never a pass.
/// All counts are of the sessions after the move that set the current step.
/// </summary>
public sealed record RampEvidence
{
    /// <summary>Trading sessions completed since the step was set.</summary>
    public int Sessions { get; init; }

    /// <summary>Closed round trips of tickets (graded or uncertain).</summary>
    public IReadOnlyList<TripFact> Trips { get; init; } = [];

    /// <summary>Total slippage in R per measured entry, one reference kind only (the kind with the most entries).</summary>
    public IReadOnlyList<double> SlippageR { get; init; } = [];

    public string? SlippageKind { get; init; }

    /// <summary>Symbol-days of fills that matched no ticket.</summary>
    public int DiscretionaryTrades { get; init; }

    public int OverAllocations { get; init; }

    public int ChasedEntries { get; init; }

    /// <summary>Entries that carry an outcome with fills.</summary>
    public int Entries { get; init; }

    public int IntradayEntries { get; init; }

    public int ProtectedIntraday { get; init; }

    /// <summary>Intraday entries of sessions with no order evidence: the protection is unavailable, not a pass.</summary>
    public int ProtectionUnavailable { get; init; }

    /// <summary>Delivery entries whose stop cannot be read (forever orders are not read); excluded and counted.</summary>
    public int ProtectionUnknown { get; init; }

    /// <summary>Drawdown of the closed trades in R (peak to trough of the running sum); null without a closed trade whose R is fixed.</summary>
    public double? DrawdownR { get; init; }

    public int AfterHaltFills { get; init; }

    public int DailyHalts { get; init; }

    /// <summary>Sessions whose capture was <c>healthy</c>; <see cref="CaptureSessions"/> is how many were looked at.</summary>
    public int HealthySessions { get; init; }

    public int CaptureSessions { get; init; }

    /// <summary>Of the last 5 sessions, those that were <c>gap</c> or <c>not_run</c>.</summary>
    public int BadCaptureLast5 { get; init; }

    /// <summary>Of the last 5 sessions, those that were <c>gap</c> (G6 allows none).</summary>
    public int GapLast5 { get; init; }

    /// <summary>Settle runs of the last 10 sessions that did not agree with the trade book or could not read the history.</summary>
    public int HistoryDisagreementsLast10 { get; init; }

    public int AfterHaltLast10 { get; init; }

    public int OverAllocationLast10 { get; init; }

    public int UnprotectedLast10 { get; init; }

    public int AfterHaltLast5 { get; init; }

    public int OverAllocationLast5 { get; init; }

    public int UnprotectedLast5 { get; init; }

    public int WeeklyHaltsLast8Weeks { get; init; }
}
