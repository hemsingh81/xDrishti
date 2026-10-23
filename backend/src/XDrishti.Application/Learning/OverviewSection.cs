namespace XDrishti.Application.Learning;

/// <summary>One kind of learning record in the overview (P6-18): how many were counted, the split by role, outcome or state, and the newest record.</summary>
/// <param name="Counted">Records counted: at most the newest <see cref="GetLearningOverviewHandler.CountLimit"/>.</param>
/// <param name="Truncated">True when there may be more records than were counted.</param>
/// <param name="Split">The count by role (models), outcome (experiments) or state (hypotheses, lessons); only names that occur.</param>
/// <param name="Newest">The newest record, or null when none is stored.</param>
/// <param name="Champion">For models, the current champion; null otherwise or when there is none.</param>
public sealed record OverviewSection(int Counted, bool Truncated, IReadOnlyList<OverviewCount> Split, OverviewHeadline? Newest, OverviewHeadline? Champion);
