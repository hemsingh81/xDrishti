namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The verdict with the quantities that are missing (not evaluable) or failed (not a keep).</summary>
/// <param name="Verdict">The word.</param>
/// <param name="Missing">What is missing, named with its numbers, for NOT_EVALUABLE.</param>
/// <param name="Failed">What failed the keep rule, for DROP and INCONCLUSIVE.</param>
public sealed record ExtensionVerdictResult(ExtensionVerdict Verdict, IReadOnlyList<string> Missing, IReadOnlyList<string> Failed);
