namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>A halt to store (spec P11-04, <c>trading.live_halts</c>): the period, the limit, what was measured and what the halt does.</summary>
public sealed record NewHalt(Guid AccountId, string Kind, DateOnly PeriodStart, decimal LimitR, decimal LimitPct, double MeasuredR, double? MeasuredPct, string MeasureState, string Action);

/// <summary>A stored halt event.</summary>
public sealed record StoredHalt(long Id, Guid AccountId, string Kind, string Event, DateOnly PeriodStart, decimal LimitR, decimal LimitPct, double? MeasuredR, double? MeasuredPct, string MeasureState, string Action, DateTimeOffset CreatedAt);

/// <summary>
/// The halts of live accounts (<c>trading.live_halts</c>, spec P11-04, ADR 0151): insert-only; a halt is opened once per account, kind and period, and the period decides how long it is in force (a day, or a week), so no row is ever updated or closed.
/// Nothing here reaches a broker.
/// </summary>
public interface ILiveHaltStore
{
    /// <summary>Stores the opened halt; null when the account already has one for the kind and period (the check is idempotent).</summary>
    ValueTask<StoredHalt?> OpenAsync(NewHalt halt, CancellationToken cancellationToken);

    /// <summary>The halts whose period starts in the range, newest first; the account may be null for all.</summary>
    ValueTask<IReadOnlyList<StoredHalt>> ListAsync(Guid? accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
