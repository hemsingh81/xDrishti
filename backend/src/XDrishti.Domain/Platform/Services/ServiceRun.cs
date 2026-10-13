using XDrishti.Domain.Common;

namespace XDrishti.Domain.Platform.Services;

/// <summary>
/// One run of a service, or one attempt of it (ADR 0018 §5): what started it, which attempt (1 for the first, then one per retry), when it began and ended, how it ended and why. History
/// only; a run is never deleted. At most one run of a service is <see cref="ServiceRunStatus.Running"/>. <see cref="DataJobId"/> links the data job a run started, so its progress can be read.
/// </summary>
public sealed class ServiceRun : Entity<Guid>
{
    public const int MaxMessageLength = 500;

    /// <summary>The message of a run that ended because it ran past its timeout starts with this (no retry follows).</summary>
    public const string TimedOutPrefix = "Timed out";

    /// <summary>The message of a run that ended because the application stopped or crashed while it ran starts with this (no retry follows).</summary>
    public const string InterruptedPrefix = "Interrupted";

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private ServiceRun(Guid id, string serviceId, ServiceRunTrigger trigger, int attempt, DateTimeOffset startedAt)
        : base(id)
    {
        ServiceId = serviceId;
        Trigger = trigger;
        Attempt = attempt;
        StartedAt = startedAt;
        Status = ServiceRunStatus.Running;
    }

    public string ServiceId { get; private set; }

    public ServiceRunTrigger Trigger { get; private set; }

    public int Attempt { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public ServiceRunStatus Status { get; private set; }

    /// <summary>Why the run failed or was skipped, or what it reported; short and safe to show.</summary>
    public string? Message { get; private set; }

    public Guid? DataJobId { get; private set; }

    public bool IsRunning => Status == ServiceRunStatus.Running;

    /// <summary>Failed because it ran past its timeout. A timeout is never retried.</summary>
    public bool IsTimedOut => Status == ServiceRunStatus.Failed && (Message?.StartsWith(TimedOutPrefix, StringComparison.Ordinal) ?? false);

    /// <summary>Failed because the application stopped or crashed while it ran. Such a run is not retried.</summary>
    public bool IsInterrupted => Status == ServiceRunStatus.Failed && (Message?.StartsWith(InterruptedPrefix, StringComparison.Ordinal) ?? false);

    public static ServiceRun Start(string serviceId, ServiceRunTrigger trigger, int attempt, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), serviceId, trigger, Math.Max(1, attempt), now);

    /// <summary>A run that was not carried out: recorded as skipped and finished at once, with the reason.</summary>
    public static ServiceRun Skipped(string serviceId, ServiceRunTrigger trigger, string reason, DateTimeOffset now)
    {
        var run = Start(serviceId, trigger, 1, now);
        run.End(ServiceRunStatus.Skipped, reason, now);
        return run;
    }

    public void LinkDataJob(Guid dataJobId) => DataJobId = dataJobId;

    public void Succeed(DateTimeOffset now, string? message = null) => End(ServiceRunStatus.Succeeded, message, now);

    public void Fail(DateTimeOffset now, string message) => End(ServiceRunStatus.Failed, message, now);

    public void Skip(DateTimeOffset now, string reason) => End(ServiceRunStatus.Skipped, reason, now);

    private void End(ServiceRunStatus status, string? message, DateTimeOffset now)
    {
        Status = status;
        FinishedAt = now;
        Message = Limit(message);
    }

    /// <summary>The text cut to the column size on one line.</summary>
    public static string? Limit(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var line = message.ReplaceLineEndings(" ").Trim();
        return line.Length <= MaxMessageLength ? line : line[..(MaxMessageLength - 1)] + "…";
    }
}
