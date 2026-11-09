namespace XDrishti.Domain.Platform.Services;

/// <summary>What happens to a missed occurrence in a plan (ADR 0240).</summary>
public enum RecoveryItemStatus
{
    /// <summary>Covered by a step of the plan.</summary>
    Planned,

    /// <summary>A later run of the same service replaces it (<see cref="RecoveryClass.LatestOnly"/>).</summary>
    Superseded,

    /// <summary>Never made up; the reason says why.</summary>
    NotRecoverable,

    /// <summary>Older than <c>Recovery:MaxSessions</c> sessions; the existing Data backfill is the way.</summary>
    OutOfReach,
}

public static class RecoveryItemStatusCodes
{
    public static string Code(RecoveryItemStatus status) => status switch
    {
        RecoveryItemStatus.Planned => "planned",
        RecoveryItemStatus.Superseded => "superseded",
        RecoveryItemStatus.NotRecoverable => "not_recoverable",
        RecoveryItemStatus.OutOfReach => "out_of_reach",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}

/// <summary>One missed occurrence of a service: the session (India date) it belongs to, the instant it was due, and what the plan does with it.</summary>
public sealed record RecoveryItem(string ServiceId, DateOnly Session, DateTimeOffset Due, RecoveryItemStatus Status, string? Reason);

/// <summary>One run to make: <see cref="Sessions"/> are the missed sessions it covers, oldest first (<see cref="From"/> to <see cref="To"/>).</summary>
public sealed record RecoveryStep(string ServiceId, RecoveryClass Class, IReadOnlyList<DateOnly> Sessions)
{
    public DateOnly From => Sessions[0];

    public DateOnly To => Sessions[^1];
}

/// <summary>The steps in the order they run, and every missed occurrence with what happens to it.</summary>
public sealed record RecoveryPlan(IReadOnlyList<RecoveryStep> Steps, IReadOnlyList<RecoveryItem> Items);

/// <summary>A registered service as the planner sees it: its definition, its effective settings and when a run of it last started (any kind and outcome), or null.</summary>
public sealed record RecoveryInput(ServiceDefinition Definition, ServiceSettings Settings, DateTimeOffset? LastRunStartedAt);
