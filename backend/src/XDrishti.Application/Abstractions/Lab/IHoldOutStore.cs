using XDrishti.Application.Lab;

namespace XDrishti.Application.Abstractions.Lab;

/// <summary>The locked hold-out block (spec CF-06, D4): where it starts, over which history it was sized, and the settings it was sized under. A later block (set once by an owner act when more history exists) supersedes this one.</summary>
public sealed record HoldOutBlock(DateOnly Start, DateOnly HistoryFirst, DateOnly HistoryLast, int LengthMonths, string SettingHash, DateTimeOffset CreatedAt);

/// <summary>One look at the hold-out, as stored in <c>lab.holdout_unlocks</c> (spec CF-06, D5).</summary>
public sealed record StoredUnlock(string StrategyId, int Version, string Kind, string Mode, string Actor, DateTimeOffset UnlockedAt, string SettingHash, bool ReusedWindow, DateOnly BlockStart, Guid RunId, HoldOutResultView Result);

/// <summary>
/// <c>lab.holdout_blocks</c> and <c>lab.holdout_unlocks</c> behind a port (spec CF-06). Both are insert-only for the runtime role. This port reads the stored records of the hold-out, not market data: the sessions inside the hold-out are read only by the unlock
/// (<c>HoldOutPass</c>), which an architecture test pins.
/// </summary>
public interface IHoldOutStore
{
    /// <summary>The first block stored (there is one by design), or null when none is fixed.</summary>
    ValueTask<HoldOutBlock?> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>Stores the block; false when a block with this start exists.</summary>
    ValueTask<bool> InsertBlockAsync(HoldOutBlock block, CancellationToken cancellationToken);

    /// <summary>The unlock of the version of this kind (<c>strategy</c>; CF-10 uses <c>model</c>), or null.</summary>
    ValueTask<StoredUnlock?> GetUnlockAsync(string strategyId, int version, string kind, CancellationToken cancellationToken);

    /// <summary>Whether any look at the block that starts on this date was taken.</summary>
    ValueTask<bool> AnyUnlockAsync(DateOnly blockStart, CancellationToken cancellationToken);

    /// <summary>The unlocks, newest first, at most <paramref name="limit"/>.</summary>
    ValueTask<IReadOnlyList<StoredUnlock>> ListUnlocksAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Stores the unlock; false (nothing written) when the version already has one of this kind, or when the look is fresh and another fresh look at the block exists (the database decides).</summary>
    ValueTask<bool> InsertUnlockAsync(StoredUnlock unlock, CancellationToken cancellationToken);
}
