using XDrishti.Application.Lab;

namespace XDrishti.Application.Abstractions.Lab;

/// <summary>The locked hold-out block (spec CF-06, D4): where it starts, over which history it was sized, and the settings it was sized under. A later block (set once by an owner act when more history exists) supersedes this one.</summary>
/// <remarks><c>PrevStart</c> is the start of the block before it in the chain, null for block 1 (HO-01). A block of the chain ends on <c>HistoryLast</c>.</remarks>
public sealed record HoldOutBlock(DateOnly Start, DateOnly HistoryFirst, DateOnly HistoryLast, int LengthMonths, string SettingHash, DateTimeOffset CreatedAt, DateOnly? PrevStart = null);

/// <summary>The release of a block (HO-01 D4), as stored in the release table: owner or auto, the setting snapshot, whether the block had a fresh look, and the block it opened.</summary>
public sealed record StoredRelease(DateOnly BlockStart, DateTimeOffset ReleasedAt, string Mode, string Actor, string SettingHash, bool Looked, DateOnly NextBlockStart);

/// <summary>A block of the chain with its release, when it has one.</summary>
public sealed record StoredBlock(HoldOutBlock Block, StoredRelease? Release);

/// <summary>What <see cref="IHoldOutStore.ReleaseAsync"/> did.</summary>
public enum ReleaseOutcome
{
    /// <summary>The release row and the next block were stored together.</summary>
    Released,

    /// <summary>The block was released already; nothing was written.</summary>
    AlreadyReleased,

    /// <summary>The next block could not be stored (its start is taken); nothing was written.</summary>
    ChainConflict,
}

/// <summary>One look at the hold-out, as stored in <c>lab.holdout_unlocks</c> (spec CF-06, D5). <c>Reason</c> is <c>not_predated</c> when the subject was not frozen before the block's horizon (HO-01 D5), else null.</summary>
public sealed record StoredUnlock(string StrategyId, int Version, string Kind, string Mode, string Actor, DateTimeOffset UnlockedAt, string SettingHash, bool ReusedWindow, DateOnly BlockStart, Guid RunId, HoldOutResultView Result, string? Reason = null);

/// <summary>
/// The hold-out blocks, their releases and the looks taken (<c>lab.holdout_unlocks</c>) behind a port (spec CF-06, HO-01). All are insert-only for the runtime role. This port reads the stored records of the hold-out, not market data: the sessions inside the hold-out are read only by the unlock
/// (<c>HoldOutPass</c>), which an architecture test pins.
/// </summary>
public interface IHoldOutStore
{
    /// <summary>The block in force: the earliest block that is not released (block 1 until the first release), or null when none is fixed. Every cut follows it.</summary>
    ValueTask<HoldOutBlock?> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>Every block of the chain, earliest first, with its release (HO-01).</summary>
    ValueTask<IReadOnlyList<StoredBlock>> ListBlocksAsync(CancellationToken cancellationToken);

    /// <summary>Whether a fresh (not re-used) look was taken at the block that starts on this date.</summary>
    ValueTask<bool> HasFreshLookAsync(DateOnly blockStart, CancellationToken cancellationToken);

    /// <summary>Stores the release row and the next block in one transaction (HO-01 D4); both or neither.</summary>
    ValueTask<ReleaseOutcome> ReleaseAsync(StoredRelease release, HoldOutBlock next, CancellationToken cancellationToken);

    /// <summary>Stores the block; false when a block with this start exists.</summary>
    ValueTask<bool> InsertBlockAsync(HoldOutBlock block, CancellationToken cancellationToken);

    /// <summary>The look of the version of this kind at the block that starts on this date (a version looks once per block and kind; CF-10 uses <c>model</c>, SR-03 <c>system</c>), or null.</summary>
    ValueTask<StoredUnlock?> GetUnlockAsync(string strategyId, int version, string kind, DateOnly blockStart, CancellationToken cancellationToken);

    /// <summary>The version's newest look of this kind across blocks, a fresh one before a re-used one (HO-01 D7), or null.</summary>
    ValueTask<StoredUnlock?> NewestUnlockAsync(string strategyId, int version, string kind, CancellationToken cancellationToken);

    /// <summary>Whether a fresh (not re-used) look of this kind was taken at the block that starts on this date.</summary>
    ValueTask<bool> HasFreshLookAsync(DateOnly blockStart, string kind, CancellationToken cancellationToken);

    /// <summary>The fresh looks at the block, at most one per kind (HO-01 D5).</summary>
    ValueTask<IReadOnlyList<StoredUnlock>> ListFreshLooksAsync(DateOnly blockStart, CancellationToken cancellationToken);

    /// <summary>When the first look of any kind at the block was stored, or null when none was (the horizon of block 1, HO-01 D5).</summary>
    ValueTask<DateTimeOffset?> FirstLookAtAsync(DateOnly blockStart, CancellationToken cancellationToken);

    /// <summary>The looks of this kind whose subject id starts with <paramref name="idPrefix"/>, newest block first (a model family's looks across blocks, HO-01 D8).</summary>
    ValueTask<IReadOnlyList<StoredUnlock>> ListUnlocksByIdPrefixAsync(string kind, string idPrefix, CancellationToken cancellationToken);

    /// <summary>The unlocks, newest first, at most <paramref name="limit"/>.</summary>
    ValueTask<IReadOnlyList<StoredUnlock>> ListUnlocksAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Stores the unlock; false (nothing written) when the version already has one of this kind at the block, when the look is fresh and another fresh look of the kind at the block exists, or when the block is released already (the database decides; this closes the race between a look and a release).</summary>
    ValueTask<bool> InsertUnlockAsync(StoredUnlock unlock, CancellationToken cancellationToken);
}
