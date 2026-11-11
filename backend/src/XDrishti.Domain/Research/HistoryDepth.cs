using System.Globalization;
using System.Text.RegularExpressions;

namespace XDrishti.Domain.Research;

/// <summary>
/// How deep the stored bars of a run go, per instrument, and what the depth does not repair (CF-04 Part C, ADR 0271). Computed only from the run's own instrument records (first bar read to the last bar read, never past the run's last date), so it reads nothing
/// that is not already in the record and holds no look-ahead. A run whose instruments are all under <see cref="BackfilledYears"/> years deep (every run before the backfill) gets no lines, so its labels, record and exports are what they were.
/// </summary>
public static class HistoryDepth
{
    /// <summary>The depth from which a run counts as resting on backfilled history: the stored history before the backfill is about 2.1 years.</summary>
    public const double BackfilledYears = 3.0;

    /// <summary>Days per year of every depth figure (days / 365.25).</summary>
    public const double DaysPerYear = 365.25;

    /// <summary>The survivorship text for a run with the single-snapshot (or no) basis.</summary>
    public const string SurvivorshipSnapshot = "survivorship: members as of today; instruments delisted in the window are absent (Dhan serves active instruments only; ISS-22)";

    /// <summary>The survivorship text for a run on dated members: the members are per day, but 1-minute bars of instruments Dhan no longer serves cannot be had.</summary>
    public const string SurvivorshipDated = "survivorship: members are dated, but instruments Dhan no longer serves have no 1-minute bars and are absent (ISS-22)";

    /// <summary>The depth of each instrument that ran, in years, in run order.</summary>
    public static IReadOnlyList<double> YearsOf(SimulationRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return [.. Ran(run).Select(i => (Math.Min(i.LastBar!.Value.DayNumber, run.To.DayNumber) - i.FirstBar!.Value.DayNumber) / DaysPerYear)];
    }

    /// <summary>The counts of instruments that ran: under 2 years, 2 to under 3, 3 to under 5, and 5 or more.</summary>
    public static (int Under2, int From2, int From3, int From5) Buckets(IReadOnlyList<double> years)
    {
        ArgumentNullException.ThrowIfNull(years);
        return (years.Count(y => y < 2), years.Count(y => y is >= 2 and < 3), years.Count(y => y is >= 3 and < 5), years.Count(y => y >= 5));
    }

    /// <summary>The depth line and the survivorship line; none for a run with no instrument of <see cref="BackfilledYears"/> years or more.</summary>
    public static string[] Lines(SimulationRun run)
    {
        var years = YearsOf(run);
        if (!years.Any(y => y >= BackfilledYears))
        {
            return [];
        }

        var (u2, f2, f3, f5) = Buckets(years);
        var first = Ran(run).Min(i => i.FirstBar!.Value);
        return
        [
            string.Create(CultureInfo.InvariantCulture, $"depth: history from {first:yyyy-MM-dd}; instruments by years of stored bars: under 2 {u2}, 2 to 3 {f2}, 3 to 5 {f3}, 5 or more {f5} (per instrument: firstBar in the record)"),
            run.UniverseBasis?.IsDatedBasis() == true ? SurvivorshipDated : SurvivorshipSnapshot,
        ];
    }

    private static readonly Regex StoredSpan = new(@"Stored data runs from (\d{4}-\d{2}-\d{2}) to (\d{4}-\d{2}-\d{2})", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The years the stored data of a Lab run span, up to the window end only: (min(last, to) - first) / 365.25.</summary>
    public static double StoredYears(DateOnly first, DateOnly last, DateOnly to) => (Math.Min(last.DayNumber, to.DayNumber) - first.DayNumber) / DaysPerYear;

    /// <summary>The stored-data span a Lab run recorded in its labels ("Stored data runs from A to B"); null for a run that has no such label.</summary>
    public static (DateOnly First, DateOnly Last)? StoredSpanOf(IEnumerable<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        foreach (var label in labels)
        {
            var m = StoredSpan.Match(label);
            if (m.Success && DateOnly.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var a) && DateOnly.TryParseExact(m.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var b))
            {
                return (a, b);
            }
        }

        return null;
    }

    /// <summary>The depth and survivorship lines of a Lab run, from the stored-data span it reads (no per-instrument records exist at post time); none when the span is under <see cref="BackfilledYears"/> years, so every current Lab run keeps its labels.</summary>
    public static string[] LabLines(DateOnly first, DateOnly last, DateOnly to, bool datedMembers)
    {
        if (StoredYears(first, last, to) < BackfilledYears)
        {
            return [];
        }

        return
        [
            string.Create(CultureInfo.InvariantCulture, $"depth: stored history from {first:yyyy-MM-dd} to {last:yyyy-MM-dd}, about {StoredYears(first, last, to):0.0} years (the first bar of each instrument is in its simulation record)"),
            datedMembers ? SurvivorshipDated : SurvivorshipSnapshot,
        ];
    }

    private static IEnumerable<InstrumentRun> Ran(SimulationRun run) => run.Instruments.Where(i => i.Skipped is null && i.FirstBar is not null && i.LastBar is not null);
}
