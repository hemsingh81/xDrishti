namespace XDrishti.Application.Abstractions.Platform;

/// <summary>A holdings sync run as the Services screen shows it. <c>Trigger</c> is scheduled or manual; <c>Status</c> running, succeeded, failed or skipped.</summary>
public sealed record AccountSyncRun(Guid Id, string Trigger, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Status, string? Message);

/// <summary>The state of the per-account holdings sync (ADR 0013) over the accounts that count: whether any refresh is on, the latest run and the earliest next automatic run.</summary>
public sealed record AccountSyncState(bool AnyOn, AccountSyncRun? Last, DateTimeOffset? NextRunAt);

/// <summary>
/// The per-account sync, seen from the services framework (ADR 0018 §8: a read-only row of the Services screen). Owned by the platform module and implemented by the portfolio module, which keeps its schedule
/// and records to itself.
/// </summary>
public interface IAccountSyncSource
{
    ValueTask<AccountSyncState> GetAsync(CancellationToken cancellationToken);
}
