using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>Pure conversion of monitor reports into lesson findings (spec P6-16). No clock, no database; the same reports give the same findings in the same order.</summary>
public static class LessonFindings
{
    /// <summary>The problem of each growing tag. The evidence key is the tag kind alone: a persisting problem is one finding while its lesson is not final; the counts are in the problem text.</summary>
    public static IReadOnlyList<LessonFinding> FromLossTags(LossTagReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var c = CultureInfo.InvariantCulture;
        return [.. report.Cells.SelectMany(cell => cell.Growing.Select(g => new LessonFinding(
            LessonMonitorKind.GrowingTag,
            cell.CellKey,
            g.Kind.ToString(),
            string.Create(c, $"{g.Kind} losses grew from {g.EarlierCount} of {g.Window} to {g.RecentCount} of {g.Window} in the last two windows of losses."))))
            .OrderBy(f => f.Subject, StringComparer.Ordinal).ThenBy(f => f.EvidenceKey, StringComparer.Ordinal)];
    }

    /// <summary>The alarms that are still on at the end of the replay: the last event of each monitor and subject, when it fired.</summary>
    public static IReadOnlyList<LessonFinding> FromDrift(DriftReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var events = report.Cells.SelectMany(c => c.Events).Concat(report.Calibration?.Events ?? []);
        return [.. events
            .GroupBy(e => (e.Detector, e.Subject))
            .Select(g => g.OrderBy(e => e.AtEpochSeconds).ThenBy(e => e.Check).Last())
            .Where(e => e.Fired)
            .Select(e => new LessonFinding(
                e.Detector switch
                {
                    DriftDetectorKind.RollingBand => LessonMonitorKind.RollingBand,
                    DriftDetectorKind.Cusum => LessonMonitorKind.Cusum,
                    _ => LessonMonitorKind.Calibration,
                },
                e.Subject,
                string.Create(CultureInfo.InvariantCulture, $"{e.AtEpochSeconds}:{e.Check}"),
                string.Create(CultureInfo.InvariantCulture, $"{e.Detector} alarm on {e.Subject}: {e.Evidence}")))
            .OrderBy(f => f.Monitor).ThenBy(f => f.Subject, StringComparer.Ordinal).ThenBy(f => f.EvidenceKey, StringComparer.Ordinal)];
    }
}
