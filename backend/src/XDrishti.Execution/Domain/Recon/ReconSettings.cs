using System.Globalization;

namespace XDrishti.Execution.Domain.Recon;

/// <summary>
/// The numbers of the update paths and the reconciliation (spec P12-06). <see cref="FromValues"/> returns null when any value is out of its range: the caller then runs nothing on a guess (the poll keeps its compiled defaults, never a looser one, and the
/// reconciliation reports unavailable).
/// </summary>
public sealed record ReconSettings(TimeSpan PollBusy, TimeSpan PollIdle, TimeSpan ReconInterval, TimeSpan Grace, TimeSpan BackoffMax, TimeSpan RecoveryMax)
{
    public static ReconSettings Default { get; } = FromValues(new Dictionary<string, string>())!;

    public static ReconSettings? FromValues(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        TimeSpan? Seconds(string key)
        {
            var setting = ExecSettingRegistry.Find(key);
            var raw = values.TryGetValue(key, out var v) ? v : setting?.Default;
            return setting is not null && raw is not null && ExecSettingRegistry.IsValid(setting, raw) ? TimeSpan.FromSeconds((double)decimal.Parse(raw, CultureInfo.InvariantCulture)) : null;
        }

        return Seconds("exec.poll_busy_s") is { } busy && Seconds("exec.poll_idle_s") is { } idle && Seconds("exec.recon_interval_s") is { } interval
            && Seconds("exec.recon_grace_s") is { } grace && Seconds("exec.stream_backoff_max_s") is { } backoff && Seconds("exec.recovery_max_s") is { } recovery
            ? new ReconSettings(busy, idle, interval, grace, backoff, recovery)
            : null;
    }

    /// <summary>The wait before the next poll: busy while a position or a working order exists, idle otherwise, none outside the session (the closing runs are scheduled separately).</summary>
    public TimeSpan? NextPoll(bool exposedOrWorking, bool inSession) => !inSession ? null : exposedOrWorking ? PollBusy : PollIdle;

    /// <summary>Reconnect backoff 1, 2, 4, 8, 16 seconds up to the cap, then the cap; <paramref name="jitter"/> in [-1, 1] moves it by up to 20 %.</summary>
    public TimeSpan Backoff(int attempt, double jitter)
    {
        var seconds = Math.Min(Math.Pow(2, Math.Clamp(attempt, 0, 10)), BackoffMax.TotalSeconds);
        return TimeSpan.FromSeconds(seconds * (1 + (0.2 * Math.Clamp(jitter, -1, 1))));
    }
}
