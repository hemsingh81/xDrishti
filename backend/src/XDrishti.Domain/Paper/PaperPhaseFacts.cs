using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Paper;

/// <summary>What is stored about one trading session of the epoch, for the paper phase check (spec P10-06). Plain data.</summary>
/// <param name="Date">The trading session.</param>
/// <param name="PlanPublished">Whether a plan was published for it.</param>
/// <param name="FailedClosedRuns">Nightly attempts that failed closed.</param>
/// <param name="ManualPlanRuns">Nightly attempts a person started.</param>
/// <param name="PrimaryTickets">Primary tickets of the published plan; null without a plan.</param>
/// <param name="CaptureRuns">Capture rows of the session.</param>
/// <param name="CaptureFinished">Whether the newest capture row is done with nothing pending.</param>
/// <param name="CapturePending">Whether the newest capture row left pairs for a later run.</param>
/// <param name="CaptureIntegrity">Stored entries a replay disagreed with, over all capture rows of the session.</param>
public sealed record PhaseSessionFact(DateOnly Date, bool PlanPublished, int FailedClosedRuns, int ManualPlanRuns, int? PrimaryTickets, int CaptureRuns, bool CaptureFinished, bool CapturePending, int CaptureIntegrity);

/// <summary>One epoch of the history.</summary>
/// <param name="Id">The id.</param>
/// <param name="StartedOn">The first session.</param>
/// <param name="Kind">first, detected_change or owner_restart.</param>
/// <param name="Reason">Why it started.</param>
/// <param name="Counted">Entries that count (as stored).</param>
/// <param name="UnexplainedChanges">Changes with no explanation.</param>
/// <param name="RiskDownChanges">Risk-down changes noted in the epoch.</param>
public sealed record PhaseEpochFact(long Id, DateOnly StartedOn, string Kind, string Reason, int Counted, int UnexplainedChanges, int RiskDownChanges);

/// <summary>A configuration change of the current epoch.</summary>
/// <param name="Session">The session of the capture run that found it; null when unknown.</param>
/// <param name="Class">frozen, risk_down or free.</param>
public sealed record PhaseChangeFact(DateOnly? Session, string Class);

/// <summary>The two exit statistics as P10-04 computed them, and the figures the labels need.</summary>
/// <param name="CompareEpochId">The epoch the comparison was computed for.</param>
/// <param name="Expectancy">E3.</param>
/// <param name="Drawdown">E4.</param>
/// <param name="IntervalLow">Low end of the paper mean's 90% interval; null when not a figure.</param>
/// <param name="IntervalHigh">High end of the interval; null when not a figure.</param>
/// <param name="EarlyWarning">The early warning sentence, or null.</param>
/// <param name="SensitivityMean">The plan-price sensitivity mean over all primary proposals, in words (a figure or a state).</param>
/// <param name="ChargeSheetConfirmed">Whether the owner confirmed the charge sheet(s) of the counted trades.</param>
public sealed record PhaseExitFacts(long? CompareEpochId, CriterionResult Expectancy, CriterionResult Drawdown, double? IntervalLow, double? IntervalHigh, string? EarlyWarning, string SensitivityMean, bool ChargeSheetConfirmed);

/// <summary>Everything the paper phase check reads, as plain facts (spec P10-06, ADR 0123). The handler gathers them from stored records; <see cref="PaperPhaseCheck"/> judges them.</summary>
/// <param name="Epoch">The epoch judged; null before the first capture.</param>
/// <param name="History">Every epoch, oldest first.</param>
/// <param name="Through">The last closed session counted (already rounded down).</param>
/// <param name="ThroughIsSession">Whether <paramref name="Through"/> is a trading session.</param>
/// <param name="Sessions">The trading sessions from the epoch start to <paramref name="Through"/>.</param>
/// <param name="Counted">The counted trades of the epoch in exit order.</param>
/// <param name="Exit">E3 and E4 from the backtest comparison; null when it could not be read.</param>
/// <param name="Changes">The configuration changes of the epoch.</param>
/// <param name="P7Verdict">The P7 phase check's bare verdict (MET, NOT MET or NOT EVALUABLE); null when it could not be read.</param>
/// <param name="P7Label">Its label, or the reason it is unavailable.</param>
/// <param name="DisciplineLines">The discipline lines of the epoch, one each.</param>
/// <param name="Services">The services the pipeline needs.</param>
/// <param name="Switches">The settings the check prints.</param>
public sealed record PaperPhaseFacts(
    PhaseEpochFact? Epoch,
    IReadOnlyList<PhaseEpochFact> History,
    DateOnly Through,
    bool ThroughIsSession,
    IReadOnlyList<PhaseSessionFact> Sessions,
    IReadOnlyList<CountedTrade> Counted,
    PhaseExitFacts? Exit,
    IReadOnlyList<PhaseChangeFact> Changes,
    string? P7Verdict,
    string P7Label,
    IReadOnlyList<string> DisciplineLines,
    IReadOnlyList<PhaseService> Services,
    IReadOnlyList<PhaseSwitch> Switches);
