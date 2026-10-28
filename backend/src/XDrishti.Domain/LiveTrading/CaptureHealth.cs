using XDrishti.Domain.Market;

namespace XDrishti.Domain.LiveTrading;

/// <summary>One run of the capture or the settle, as the health reads it.</summary>
public sealed record CaptureRun(string Kind, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Outcome);

/// <summary>
/// The health of a day's capture (spec P11-01 criterion 10): <c>gap</c> when no poll succeeded for more than 30 minutes of the session (09:10 to 15:40 India time; a poll that read the trade book but not the order book counts) or the latest settle
/// failed; <c>not_run</c> when the day has no run at all; otherwise <c>healthy</c>. Pure.
/// </summary>
public static class CaptureHealth
{
    public const string Healthy = "healthy";
    public const string Gap = "gap";
    public const string NotRun = "not_run";

    public static readonly TimeOnly WindowStart = new(9, 10);
    public static readonly TimeOnly WindowEnd = new(15, 40);
    public static readonly TimeSpan MaxSilence = TimeSpan.FromMinutes(30);

    public static string Evaluate(IReadOnlyList<CaptureRun> runs, DateOnly date, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(runs);
        if (runs.Count == 0)
        {
            return NotRun;
        }

        var start = At(date, WindowStart);
        var end = At(date, WindowEnd);
        if (now < end)
        {
            end = now;
        }

        var ok = runs.Where(r => r.Kind == OutcomeCodes.KindPoll && r.Outcome is OutcomeCodes.RunOk or OutcomeCodes.RunPartial)
            .Select(r => r.FinishedAt ?? r.StartedAt)
            .Where(t => t >= start && t <= end).Order().ToList();
        var points = new List<DateTimeOffset> { start };
        points.AddRange(ok);
        points.Add(end);
        var silent = points.Zip(points.Skip(1), (a, b) => b - a).Any(d => d > MaxSilence);
        var settle = runs.Where(r => r.Kind == OutcomeCodes.KindSettle).OrderBy(r => r.StartedAt).LastOrDefault();
        var settleFailed = settle is not null && settle.Outcome is not (OutcomeCodes.RunOk or OutcomeCodes.RunPartial);
        return silent || settleFailed ? Gap : Healthy;
    }

    private static DateTimeOffset At(DateOnly date, TimeOnly time) => new(date.ToDateTime(time), IndiaTime.Offset);
}
