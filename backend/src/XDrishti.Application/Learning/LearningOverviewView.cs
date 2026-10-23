namespace XDrishti.Application.Learning;

/// <summary>
/// What the Learning screen needs first (P6-18, ADR 0080): counts and the newest verdicts of the stored learning records. Read from the stores; no statistic is computed and no model is scored.
/// </summary>
/// <param name="CellRows">Rows in the newest cell-statistics snapshot (all windows).</param>
/// <param name="CellsAsOf">The as-of date of that snapshot, or null before the first computation.</param>
/// <param name="Models">The registered models.</param>
/// <param name="Experiments">The recorded experiments.</param>
/// <param name="Hypotheses">The hypotheses (newest view of each).</param>
/// <param name="Lessons">The lessons (newest view of each).</param>
/// <param name="Changelog">The changelog rows.</param>
/// <param name="Limits">What the numbers are not.</param>
public sealed record LearningOverviewView(int CellRows, DateOnly? CellsAsOf, OverviewSection Models, OverviewSection Experiments, OverviewSection Hypotheses, OverviewSection Lessons, OverviewSection Changelog, IReadOnlyList<string> Limits);
