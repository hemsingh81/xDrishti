using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>A stored ramp event (spec P11-04).</summary>
public sealed record StoredRampEvent(long Id, Guid AccountId, string Kind, int? FromStep, int ToStep, bool Hold, string Actor, string Reason, string? GateHash, DateTimeOffset CreatedAt);

/// <summary>
/// The ramp of a live account (<c>trading.ramp_events</c>, spec P11-04, ADR 0148): insert-only; the state is the newest row of the account, and an account with none has no step and so no live allocation. The table refuses a move its rules do not allow.
/// </summary>
public interface IRampStore
{
    /// <summary>The state of one account, or null when it has no step.</summary>
    ValueTask<RampState?> CurrentAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>The state of every account that has a step.</summary>
    ValueTask<IReadOnlyDictionary<Guid, RampState>> CurrentAllAsync(CancellationToken cancellationToken);

    /// <summary>The events of the account, newest first.</summary>
    ValueTask<IReadOnlyList<StoredRampEvent>> HistoryAsync(Guid accountId, int limit, CancellationToken cancellationToken);

    /// <summary>Appends the event; null when the table refused it (the step changed meanwhile, or a rule of the table).</summary>
    ValueTask<StoredRampEvent?> AppendAsync(RampEvent ramp, CancellationToken cancellationToken);
}
