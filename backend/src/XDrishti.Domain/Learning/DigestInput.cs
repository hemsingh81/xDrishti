using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Learning;

/// <summary>
/// Everything the weekly digest is built from (P7-15, ADR 0094): stored records already turned into <see cref="DigestItem"/> lines by the caller, in the order they should appear, plus the stored plan-grade report and the phase check's criteria.
/// No clock and no store here: the builder only lists and counts.
/// </summary>
/// <param name="Week">The week.</param>
/// <param name="BuiltAt">When the digest is built: printed in India time on the digest, and says whether the state read is that of a finished week.</param>
/// <param name="CellsAsOf">As-of date of the newest cell-statistics snapshot, or null before the first.</param>
/// <param name="CellChanges">Cell state changes (changelog rows) of the week.</param>
/// <param name="DriftProposals">Open or linked lessons raised by the drift and calibration monitors.</param>
/// <param name="LossTags">Open or linked lessons raised by the loss-tag trend monitor.</param>
/// <param name="LessonsResolved">Lessons closed or dismissed during the week.</param>
/// <param name="OpenLessonCount">Lessons in the open state (all monitors).</param>
/// <param name="LinkedLessonCount">Lessons in the linked state (all monitors).</param>
/// <param name="HypothesesAwaiting">Hypotheses proposed or judged: waiting for the owner.</param>
/// <param name="HypothesesInProgress">Hypotheses approved or running (counted; not an owner decision yet).</param>
/// <param name="Experiments">Experiments recorded during the week.</param>
/// <param name="Plans">The grade report over the plans of the week, or null when there is none to read.</param>
/// <param name="Models">The champion and the challengers.</param>
/// <param name="PhaseCriteria">The phase check's criteria (a build whose phase check cannot be read fails and stores nothing).</param>
/// <param name="PhaseLimits">The phase check's own limits.</param>
/// <param name="Truncated">Kinds of record whose read hit the limit, so there may be more.</param>
public sealed record DigestInput(
    DigestWeek Week,
    DateTimeOffset BuiltAt,
    DateOnly? CellsAsOf,
    IReadOnlyList<DigestItem> CellChanges,
    IReadOnlyList<DigestItem> DriftProposals,
    IReadOnlyList<DigestItem> LossTags,
    IReadOnlyList<DigestItem> LessonsResolved,
    int OpenLessonCount,
    int LinkedLessonCount,
    IReadOnlyList<DigestItem> HypothesesAwaiting,
    int HypothesesInProgress,
    IReadOnlyList<DigestItem> Experiments,
    GradeReportData? Plans,
    IReadOnlyList<DigestItem> Models,
    IReadOnlyList<PhaseCriterion> PhaseCriteria,
    IReadOnlyList<string> PhaseLimits,
    IReadOnlyList<string> Truncated);
