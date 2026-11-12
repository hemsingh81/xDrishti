using System.Globalization;

namespace XDrishti.Domain.Research;

/// <summary>One reason pre-seam bars were not admitted and how many instruments of the run it applies to.</summary>
public sealed record BackfillExclusion(string Reason, int Count);

/// <summary>
/// What the run did with the backfilled pre-seam bars of the instruments that have a seam row (CF-04d decision 10): how many were admitted and how many were excluded, by first failing reason. Given only when the admission switch was ON and at least one instrument of the run has a seam
/// row; null otherwise, so every other run is as before. It feeds the label line only (it is not part of the run key; the key has the <c>seam=</c> lines).
/// </summary>
public sealed record BackfillAdmissionRecord(int Admitted, IReadOnlyList<BackfillExclusion> Excluded)
{
    /// <summary>The label line: <c>backfill: pre-seam bars admitted for A instruments (source-aware); excluded for E (reason n, ...)</c>, the reasons in ordinal order.</summary>
    public string Label()
    {
        var excluded = Excluded.Sum(e => e.Count);
        var text = string.Create(CultureInfo.InvariantCulture, $"backfill: pre-seam bars admitted for {Admitted} instruments (source-aware); excluded for {excluded}");
        return excluded == 0
            ? text
            : text + " (" + string.Join(", ", Excluded.OrderBy(e => e.Reason, StringComparer.Ordinal).Select(e => e.Reason + " " + e.Count.ToString(CultureInfo.InvariantCulture))) + ")";
    }
}
