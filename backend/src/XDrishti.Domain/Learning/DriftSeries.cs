namespace XDrishti.Domain.Learning;

/// <summary>Ordering shared by the drift monitors (P6-09): by the instant a result is known, then by key, so the input order never matters; and the test for the last of several results with one instant.</summary>
internal static class DriftSeries
{
    public static List<DriftObservation> Order(IReadOnlyList<DriftObservation> series) =>
        [.. series.OrderBy(o => o.KnownAtEpochSeconds).ThenBy(o => o.Key, StringComparer.Ordinal)];

    public static List<CalibrationObservation> Order(IReadOnlyList<CalibrationObservation> series) =>
        [.. series.OrderBy(o => o.KnownAtEpochSeconds).ThenBy(o => o.Key, StringComparer.Ordinal)];

    /// <summary>True when the next result is known later, so a check here has read everything known at this instant.</summary>
    public static bool EndsInstant(IReadOnlyList<DriftObservation> sorted, int index) =>
        index + 1 >= sorted.Count || sorted[index + 1].KnownAtEpochSeconds != sorted[index].KnownAtEpochSeconds;

    public static bool EndsInstant(IReadOnlyList<CalibrationObservation> sorted, int index) =>
        index + 1 >= sorted.Count || sorted[index + 1].KnownAtEpochSeconds != sorted[index].KnownAtEpochSeconds;
}
