namespace XDrishti.Application.Abstractions.Lab;

/// <summary>What a freeze did: whether this plan hash was new for the version and how many plans the version had frozen before it.</summary>
public sealed record PlanFreezeOutcome(bool Inserted, int PriorFreezes);

/// <summary><c>lab.plan_freezes</c> behind a port (spec CF-06, D2): the plan hash of the first full or robust run of a version, and of every later changed plan. Insert-only for the runtime role.</summary>
public interface IPlanFreezeStore
{
    /// <summary>Stores the plan hash for the version, in one statement: <c>Inserted</c> is false when the version already froze this very hash; <c>PriorFreezes</c> counts the other hashes it had. A re-plan is <c>Inserted</c> with <c>PriorFreezes</c> above 0.</summary>
    ValueTask<PlanFreezeOutcome> FreezeAsync(string strategyId, int version, string planHash, Guid firstRunId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The first plan hash the version froze, or null.</summary>
    ValueTask<string?> FirstAsync(string strategyId, int version, CancellationToken cancellationToken);

    /// <summary>When the version first froze a plan (the subject's freeze time for the pre-dating horizon of a hold-out look, HO-01 D5), or null.</summary>
    ValueTask<DateTimeOffset?> FirstFrozenAtAsync(string strategyId, int version, CancellationToken cancellationToken);
}
