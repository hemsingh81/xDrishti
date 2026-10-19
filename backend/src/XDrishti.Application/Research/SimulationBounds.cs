using XDrishti.Domain.Market;

namespace XDrishti.Application.Research;

/// <summary>
/// The bounds of the read of the stored bars for a simulation (spec P3-04a D3, shared with P3-04c C2): from 2000-01-01 00:00 to the last minute of <c>To</c>, India time, both as instants with offset 0 (Npgsql writes a
/// <c>DateTimeOffset</c> to <c>timestamptz</c> only with offset 0). The run and the second read for the trade cards use the same bounds, so that the hash of the bars is the hash of the same list.
/// </summary>
internal static class SimulationBounds
{
    private static readonly DateOnly FirstDay = new(SimulationRequestChecks.FirstYear, 1, 1);

    /// <summary>The last minute of the India day (bars start on whole minutes, so 23:59 is the last minute that can hold one).</summary>
    private static readonly TimeSpan LastMinuteOfDay = new(23, 59, 0);

    /// <summary>The start of the read.</summary>
    public static DateTimeOffset From { get; } = IndiaInstant(FirstDay, TimeSpan.Zero);

    /// <summary>The end of the read for a run that ends on <paramref name="to"/>.</summary>
    public static DateTimeOffset Until(DateOnly to) => IndiaInstant(to, LastMinuteOfDay);

    /// <summary>An India wall-clock time as an instant with offset 0.</summary>
    public static DateTimeOffset IndiaInstant(DateOnly date, TimeSpan timeOfDay) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, timeOfDay.Hours, timeOfDay.Minutes, 0, IndiaTime.Offset).ToUniversalTime();
}
