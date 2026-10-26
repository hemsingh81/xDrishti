using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Paper;

/// <summary>One criterion of the paper phase check (spec P10-06): a verdict, the fact in words, the facts behind it and what stops it.</summary>
/// <param name="Id">C1 to C4 and I.</param>
/// <param name="Title">The criterion.</param>
/// <param name="Verdict">MET, NOT MET or NOT EVALUABLE.</param>
/// <param name="Label">The call and its reason in one line.</param>
/// <param name="Facts">The facts behind it.</param>
/// <param name="Blockers">What stops it and whose act it is; empty when nothing does.</param>
public sealed record PhaseCriterion(string Id, string Title, string Verdict, string Label, IReadOnlyList<string> Facts, IReadOnlyList<string> Blockers);

/// <summary>One ISO week of the epoch.</summary>
/// <param name="Week">The ISO week, e.g. 2026-W45.</param>
/// <param name="Sessions">Trading sessions of the week inside the window.</param>
/// <param name="PlansPublished">Sessions with a published plan.</param>
/// <param name="FailedClosed">Failed-closed nightly attempts.</param>
/// <param name="ManualRuns">Nightly attempts a person started.</param>
/// <param name="CaptureFinished">Sessions whose capture finished with nothing pending.</param>
/// <param name="TradesCounted">Trades counted that week (by exit day).</param>
/// <param name="CumulativeCounted">Counted trades up to the end of the week.</param>
/// <param name="CumulativeR">Their summed R.</param>
/// <param name="FrozenChanges">Frozen changes found that week.</param>
/// <param name="RiskDownChanges">Risk-down changes found that week.</param>
public sealed record PhaseWeekLine(string Week, int Sessions, int PlansPublished, int FailedClosed, int ManualRuns, int CaptureFinished, int TradesCounted, int CumulativeCounted, double CumulativeR, int FrozenChanges, int RiskDownChanges);

/// <summary>An epoch of the history, with whether it is the one judged.</summary>
/// <param name="Id">The id.</param>
/// <param name="StartedOn">The first session.</param>
/// <param name="Kind">first, detected_change or owner_restart.</param>
/// <param name="Reason">Why.</param>
/// <param name="Counted">Entries that count, as stored.</param>
/// <param name="UnexplainedChanges">Changes with no explanation.</param>
/// <param name="Current">True for the epoch judged; earlier epochs are never pooled into it.</param>
public sealed record PhaseEpochLine(long Id, DateOnly StartedOn, string Kind, string Reason, int Counted, int UnexplainedChanges, bool Current);

/// <summary>A thing the check never judges, and the tests that prove it.</summary>
/// <param name="Name">The property.</param>
/// <param name="Verdict">Always NOT EVALUABLE here.</param>
/// <param name="Proof">The test names (or where it is settled).</param>
public sealed record PhaseNotJudged(string Name, string Verdict, string Proof);

/// <summary>What is printed beside the criteria and never judged by them.</summary>
/// <param name="DisciplineLines">The discipline lines of P10-03, one each.</param>
/// <param name="SensitivityMean">The plan-price mean over all primary proposals (P10-04), in words.</param>
/// <param name="AutoApproved">Counted trades auto-approved.</param>
/// <param name="OwnerApproved">Counted trades the owner approved.</param>
/// <param name="PlansWithoutPrimary">Sessions whose plan had no primary ticket.</param>
/// <param name="ManualRuns">Manual nightly attempts in the window.</param>
/// <param name="P7">The P7 phase check's label, or why it is unavailable.</param>
/// <param name="ChargeSheetConfirmed">Whether the owner confirmed the charge sheet(s) of the counted trades.</param>
/// <param name="Services">The pipeline's services.</param>
/// <param name="Switches">The settings.</param>
/// <param name="Blockers">Every blocker, with whose act it is.</param>
/// <param name="NotJudged">What the check does not judge.</param>
public sealed record PhasePrinted(
    IReadOnlyList<string> DisciplineLines,
    string SensitivityMean,
    int AutoApproved,
    int OwnerApproved,
    int PlansWithoutPrimary,
    int ManualRuns,
    string P7,
    bool ChargeSheetConfirmed,
    IReadOnlyList<PhaseService> Services,
    IReadOnlyList<PhaseSwitch> Switches,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<PhaseNotJudged> NotJudged);

/// <summary>The paper phase check (spec P10-06, ADR 0123): the P10 exit judged from stored records, with its caveats in the label.</summary>
/// <param name="Epoch">The epoch judged; null before the first capture.</param>
/// <param name="Through">The last closed session counted.</param>
/// <param name="ElapsedDays">Days from the epoch start to <paramref name="Through"/>, both included.</param>
/// <param name="Criteria">C1 to C4 and I.</param>
/// <param name="ExitVerdict">The bare word: MET, NOT MET or NOT EVALUABLE.</param>
/// <param name="ExitCriterion">The same call with its caveats, e.g. MET (point estimate; ...).</param>
/// <param name="Weeks">One line per ISO week.</param>
/// <param name="Epochs">Every epoch of the history.</param>
/// <param name="Printed">What is printed and not judged.</param>
/// <param name="Limits">The honest limits; never empty.</param>
public sealed record PaperPhaseReport(
    PhaseEpochLine? Epoch,
    DateOnly Through,
    int ElapsedDays,
    IReadOnlyList<PhaseCriterion> Criteria,
    string ExitVerdict,
    string ExitCriterion,
    IReadOnlyList<PhaseWeekLine> Weeks,
    IReadOnlyList<PhaseEpochLine> Epochs,
    PhasePrinted Printed,
    IReadOnlyList<string> Limits);
