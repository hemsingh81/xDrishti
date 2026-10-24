using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// How much of a report one answer may hold (P8-06, non-functional): a lens of three dimensions over a few hundred instruments can name tens of thousands of groups, and an answer that large is neither read nor cheap to make. Over the cap the answer is refused with the count
/// and what to do, never cut silently (a cut answer would read as the whole).
/// </summary>
internal static class ResponseCaps
{
    /// <summary>The most groups of one answer.</summary>
    public const int Groups = 2000;

    /// <summary>The most cells of one pivot.</summary>
    public const int PivotCells = 10000;

    /// <summary>The most distributions of one answer (each holds 22 bins).</summary>
    public const int DistributionGroups = 200;

    /// <summary>The most cells of one "what works" answer.</summary>
    public const int Cells = 2000;

    /// <summary>The refusal for an answer of <paramref name="count"/> <paramref name="noun"/> over a cap of <paramref name="cap"/>, or null when it fits.</summary>
    /// <param name="count">What the answer would hold.</param>
    /// <param name="cap">The cap.</param>
    /// <param name="noun">What is counted, in the plural.</param>
    public static Error? Exceeded(int count, int cap, string noun) =>
        count <= cap
            ? null
            : ReportErrors.Invalid(string.Create(CultureInfo.InvariantCulture, $"The lens has {count} {noun}; at most {cap} are returned at once. Filter the report or use a coarser lens."));
}
