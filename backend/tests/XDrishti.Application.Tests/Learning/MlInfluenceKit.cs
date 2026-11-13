using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>Builders and fakes for the influence gate tests (CF-10 part 10c-2).</summary>
internal static class MlInfluenceKit
{
    public static readonly DateOnly BlockStart = new(2026, 5, 1);

    public static StoredModel Model(long id = 7, ModelRole role = ModelRole.Champion, bool heldOut = true, string featureVersion = "tf-1", char hash = 'e', DateOnly? holdOutStart = null, DateTimeOffset? registeredAt = null) =>
        new(id, role, new ModelToRegister(
            new string('f', 64), new string(hash, 64), 3, "FastTree", null, new string('a', 64), featureVersion, 3, 30, 10, 40, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), heldOut ? holdOutStart ?? BlockStart : null, ValidationOutcome.Eligible, 6, 4, 4, true, 3.5, 0.6, 0.2,
            string.Empty, "report", "params", [0, 1], [0.4, 0.6], [0.5, 0.5, 0.5], 1, 3, registeredAt ?? DateTimeOffset.UnixEpoch, "t"),
        1);

    public static HoldOutBlock Block(DateOnly? start = null) => new(start ?? BlockStart, new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 31), 6, new string('c', 64), DateTimeOffset.UnixEpoch);

    /// <summary>A hold-out store in memory that keeps the two uniqueness rules of the database: one look per (id, version, kind, block), and one fresh look per block and kind.</summary>
    public sealed class FakeHoldOutStore(HoldOutBlock? block) : IHoldOutStore
    {
        public List<StoredUnlock> Unlocks { get; } = [];

        /// <summary>The current block; a test advances it by setting this.</summary>
        public HoldOutBlock? Block { get; set; } = block;

        /// <summary>Runs after the fresh-look read has answered, to let another look in between the read and the insert.</summary>
        public Action? AfterAnyUnlock { get; set; }

        public ValueTask<HoldOutBlock?> CurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Block);

        public ValueTask<IReadOnlyList<StoredBlock>> ListBlocksAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<StoredBlock>>(Block is null ? [] : [new StoredBlock(Block, null)]);

        public ValueTask<bool> HasFreshLookAsync(DateOnly blockStart, CancellationToken cancellationToken) => ValueTask.FromResult(Unlocks.Any(u => u.BlockStart == blockStart && !u.ReusedWindow));

        public ValueTask<ReleaseOutcome> ReleaseAsync(StoredRelease release, HoldOutBlock next, CancellationToken cancellationToken) => ValueTask.FromResult(ReleaseOutcome.ChainConflict);

        public ValueTask<bool> InsertBlockAsync(HoldOutBlock b, CancellationToken cancellationToken) => ValueTask.FromResult(false);

        public ValueTask<StoredUnlock?> GetUnlockAsync(string strategyId, int version, string kind, DateOnly blockStart, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Unlocks.FirstOrDefault(u => u.StrategyId == strategyId && u.Version == version && u.Kind == kind && u.BlockStart == blockStart));

        public ValueTask<StoredUnlock?> NewestUnlockAsync(string strategyId, int version, string kind, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Unlocks.Where(u => u.StrategyId == strategyId && u.Version == version && u.Kind == kind).OrderBy(u => u.ReusedWindow).ThenByDescending(u => u.UnlockedAt).FirstOrDefault());

        public ValueTask<bool> HasFreshLookAsync(DateOnly blockStart, string kind, CancellationToken cancellationToken)
        {
            var any = Unlocks.Any(u => u.BlockStart == blockStart && u.Kind == kind && !u.ReusedWindow);
            AfterAnyUnlock?.Invoke();
            return ValueTask.FromResult(any);
        }

        public ValueTask<IReadOnlyList<StoredUnlock>> ListFreshLooksAsync(DateOnly blockStart, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredUnlock>>([.. Unlocks.Where(u => u.BlockStart == blockStart && !u.ReusedWindow)]);

        public ValueTask<DateTimeOffset?> FirstLookAtAsync(DateOnly blockStart, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Unlocks.Where(u => u.BlockStart == blockStart).Select(u => (DateTimeOffset?)u.UnlockedAt).Min());

        public ValueTask<IReadOnlyList<StoredUnlock>> ListUnlocksByIdPrefixAsync(string kind, string idPrefix, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredUnlock>>([.. Unlocks.Where(u => u.Kind == kind && u.StrategyId.StartsWith(idPrefix, StringComparison.Ordinal)).OrderByDescending(u => u.BlockStart)]);

        public ValueTask<IReadOnlyList<StoredUnlock>> ListUnlocksAsync(int limit, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<StoredUnlock>>([.. Unlocks]);

        public ValueTask<bool> InsertUnlockAsync(StoredUnlock unlock, CancellationToken cancellationToken)
        {
            if (Unlocks.Any(u => u.StrategyId == unlock.StrategyId && u.Version == unlock.Version && u.Kind == unlock.Kind && u.BlockStart == unlock.BlockStart) || (!unlock.ReusedWindow && Unlocks.Any(u => !u.ReusedWindow && u.BlockStart == unlock.BlockStart && u.Kind == unlock.Kind)))
            {
                return ValueTask.FromResult(false);
            }

            Unlocks.Add(unlock);
            return ValueTask.FromResult(true);
        }
    }
}
