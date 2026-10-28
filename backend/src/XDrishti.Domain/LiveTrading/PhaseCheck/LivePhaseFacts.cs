namespace XDrishti.Domain.LiveTrading;

/// <summary>One trading session of the window as the check reads it (spec P11-06): plain stored facts, already counted by the reader. A fact the sources could not give is null, never zero.</summary>
/// <param name="Date">The session.</param>
/// <param name="Tickets">Approved live tickets of the session (stored outcomes).</param>
/// <param name="Fills">Captured fills of the session.</param>
/// <param name="Capture"><c>healthy</c>, <c>gap</c> or <c>not_run</c> (<see cref="CaptureHealth"/>); healthy is the spec's "complete".</param>
/// <param name="Scales">The risk scale of every live allocation of the session's plan.</param>
/// <param name="StepFactor">The ceiling of the ramp step in force that session; null when none is known.</param>
/// <param name="Step">The ramp step in force (0 when none).</param>
/// <param name="Outcomes">Ticket outcomes by class.</param>
/// <param name="RoundTrips">Graded round trips closed that session.</param>
/// <param name="Halts">Halts opened for the session.</param>
/// <param name="Holds">Hold or step-down events written that session.</param>
/// <param name="Breaches">Entries of the session that broke a discipline rule (after halt, over-allocation, unprotected).</param>
public sealed record LiveSessionFact(
    DateOnly Date,
    int Tickets,
    int Fills,
    string Capture,
    IReadOnlyList<double> Scales,
    double? StepFactor,
    int Step,
    IReadOnlyDictionary<string, int> Outcomes,
    int RoundTrips,
    int Halts,
    int Holds,
    int Breaches);

/// <summary>The live-versus-paper facts of the window (the P11-03 report). Null in <see cref="LivePhaseFacts.Pairs"/> means the paper record could not be read.</summary>
public sealed record LivePairFacts(
    int Pairs,
    string Verdict,
    double? Mean,
    double? Lower95,
    double? Upper95,
    double? UpperOneSided95,
    int LeftOut,
    int PaperIncompleteSessions,
    bool ChargesConfirmed,
    bool PairsDistinct = true);

/// <summary>One compliance kind as the newest record stands (null dates: no record).</summary>
public sealed record LiveComplianceFact(string Kind, DateOnly? DecidedOn, DateOnly? NextReviewOn);

/// <summary>The broker allow-list read through the P7-18 port: the pinned list equals the declared one and every order, position and fund probe is refused.</summary>
public sealed record LiveAllowListFact(bool Intact, string Detail);

/// <summary>A label and a value as printed; a value that does not exist reads as words, never a digit.</summary>
public sealed record LivePrintedLine(string Label, string Value);

/// <summary>A service or switch of the setup and whether it is on; <paramref name="Blocks"/> is true when the phase cannot be evidenced while it is off (the auto switches do not block: they default OFF by design).</summary>
public sealed record LiveSetupItem(string Name, bool On, string Kind, bool Blocks = true);

/// <summary>
/// Everything the small-live phase check reads (spec P11-06, ADR 0153), already counted from the stored records by the reader. The check never reads a store itself; a null or a flag below means the source could not give the fact, and the criterion
/// then reads NOT EVALUABLE, never MET.
/// </summary>
public sealed record LivePhaseFacts
{
    public DateOnly Through { get; init; }

    /// <summary>The day of the first live entry fill on or after the first <c>set_step</c>; null without one.</summary>
    public DateOnly? WindowStart { get; init; }

    public bool ThroughIsTradingSession { get; init; } = true;

    public bool OneLiveAccount { get; init; } = true;

    public IReadOnlyList<LiveSessionFact> Sessions { get; init; } = [];

    /// <summary>Graded live round trips of the window.</summary>
    public int GradedRoundTrips { get; init; }

    public LivePairFacts? Pairs { get; init; }

    /// <summary>Total entry slippage in R of one reference kind (the kind with the most entries).</summary>
    public IReadOnlyList<double> SlippageR { get; init; } = [];

    public string? SlippageKind { get; init; }

    public bool HoldActive { get; init; }

    /// <summary>How many sessions ago each step-down was written (0 is the last session).</summary>
    public IReadOnlyList<int> StepDownSessionsAgo { get; init; } = [];

    public int StepDownsOrHolds { get; init; }

    public int AfterHaltUncleared { get; init; }

    public int IntradayEntries { get; init; }

    /// <summary>Intraday entries whose protected share or order evidence is unavailable.</summary>
    public int OrderEvidenceUnavailable { get; init; }

    public int OrderEvidenceMissingSessions { get; init; }

    /// <summary>Of the last 4 ISO weeks of the window, the weeks in which the weekly estimates ran.</summary>
    public int WeeksWithEstimates { get; init; }

    /// <summary>The newest capacity score state (<c>value</c>, <c>insufficient</c> or <c>empty</c>); null when it could not be read.</summary>
    public string? CapacityState { get; init; }

    public IReadOnlyList<LiveComplianceFact> Compliance { get; init; } = [];

    public bool CaptureRan { get; init; }

    public int Disagreements { get; init; }

    /// <summary>Ticket outcomes still <c>unknown</c> after 5 sessions.</summary>
    public int UnknownOutcomesOld { get; init; }

    public int DoubleJournaled { get; init; }

    public LiveAllowListFact? AllowList { get; init; }

    public IReadOnlyList<LivePrintedLine> Printed { get; init; } = [];

    public IReadOnlyList<LiveSetupItem> Setup { get; init; } = [];
}
