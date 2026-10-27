using System.Text;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Market.Context;

/// <summary>
/// The hash of the stored context days a run could read (P3-lift, ADR 0145): SHA-256 of the canonical text of the days dated on or before the end of the run's window. A decision on or before that date can read no later day, so a day appended afterwards
/// leaves the fingerprint unchanged (the prefix property), while a restated value (a breadth rebuild) changes it. Pure.
/// </summary>
public static class ContextFingerprint
{
    /// <summary>The fingerprint of <paramref name="days"/> (any order) dated on or before <paramref name="through"/>; the hash of the empty text when none is.</summary>
    public static string Of(IReadOnlyList<ContextDayRecord> days, DateOnly through)
    {
        ArgumentNullException.ThrowIfNull(days);
        var text = new StringBuilder();
        foreach (var day in days.Where(d => d.Date <= through).OrderBy(d => d.Date))
        {
            text.Append(day.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
                .Append('|').Append(CanonicalText.Number(day.IndexHigh))
                .Append('|').Append(CanonicalText.Number(day.IndexLow))
                .Append('|').Append(CanonicalText.Number(day.IndexClose))
                .Append('|').Append(day.VixClose is { } vix ? CanonicalText.Number(vix) : "-")
                .Append('|').Append(day.Breadth is { } breadth ? CanonicalText.Number(breadth) : "-")
                .Append('\n');
        }

        return CanonicalText.Sha256Hex(text.ToString());
    }
}
