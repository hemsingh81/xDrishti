using System.Globalization;

namespace XDrishti.Domain.Research;

/// <summary>
/// The two thresholds of the data notes of a replay (spec P3-04, Scope 4 and 6), recorded in every run and part of the run key. Decided on research from the scan of the owner's 214 files on 2026-10-18; the owner may revert.
/// </summary>
/// <param name="MinSessionCompleteness">The share of the 375 minutes of a regular session a session must hold in its window to run: in (0, 1]; 0.90 by default. Exactly the threshold runs.</param>
/// <param name="DiscontinuityThreshold">The move of a day's first open from the previous close, as a share of that close, above which the day is flagged: above 0; 0.20 by default. Exactly the threshold is not flagged.</param>
public sealed record ReplayOptions(double MinSessionCompleteness = 0.90, double DiscontinuityThreshold = 0.20)
{
    /// <summary>The defaults, 0.90 and 0.20.</summary>
    public static ReplayOptions Default { get; } = new();

    /// <summary>Null when both thresholds are finite and in range, otherwise a sentence naming the first that is not (NaN and infinities are refused: a range test written with <c>&lt;</c> lets NaN through).</summary>
    public string? Problem()
    {
        if (!double.IsFinite(MinSessionCompleteness) || MinSessionCompleteness <= 0 || MinSessionCompleteness > 1)
        {
            return "MinSessionCompleteness must be a number above 0 and at most 1, not " + MinSessionCompleteness.ToString("R", CultureInfo.InvariantCulture) + ".";
        }

        if (!double.IsFinite(DiscontinuityThreshold) || DiscontinuityThreshold <= 0)
        {
            return "DiscontinuityThreshold must be a finite number above 0, not " + DiscontinuityThreshold.ToString("R", CultureInfo.InvariantCulture) + ".";
        }

        return null;
    }
}
