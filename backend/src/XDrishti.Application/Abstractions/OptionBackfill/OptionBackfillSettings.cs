namespace XDrishti.Application.Abstractions.OptionBackfill;

/// <summary>
/// The numbers of the expired-options backfill (<c>Options:Backfill:*</c>, P14-03, documented in doc 16), checked at start by the host. <see cref="Enabled"/> is the deployment gate: with it false nothing reads credentials or builds a request.
/// <see cref="RequestsPerSecond"/> is 0.2 to 2 (Dhan's Data limit is 5 a second, shared with the day's calls).
/// </summary>
public sealed record OptionBackfillSettings(
    bool Enabled,
    double RequestsPerSecond,
    int MaxRequestsPerDay,
    int Years,
    int StockCount,
    int IndexOffsets,
    int StockOffsets,
    TimeSpan Timeout,
    long MaxStoredBytes,
    long MinFreeBytes)
{
    /// <summary>One gibibyte.</summary>
    public const long Gb = 1024L * 1024 * 1024;

    /// <summary>The lowest gap between two requests there is, whatever the setting says (2 a second).</summary>
    public static readonly TimeSpan MinSpacing = TimeSpan.FromMilliseconds(500);

    /// <summary>The gap between two requests at the configured rate, never under <see cref="MinSpacing"/>.</summary>
    public TimeSpan Spacing
    {
        get
        {
            var gap = TimeSpan.FromSeconds(1 / Math.Max(RequestsPerSecond, 0.0001));
            return gap < MinSpacing ? MinSpacing : gap;
        }
    }

    /// <summary>The defaults: OFF.</summary>
    public static OptionBackfillSettings Off { get; } = new(false, 1, 40_000, 5, 20, 10, 3, TimeSpan.FromSeconds(15), 3 * Gb, 20 * Gb);
}
