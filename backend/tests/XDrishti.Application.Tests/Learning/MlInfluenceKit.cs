using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>Builders and fakes for the influence gate tests (CF-10 part 10c-2).</summary>
internal static class MlInfluenceKit
{
    public static readonly DateOnly BlockStart = new(2026, 5, 1);

    public static StoredModel Model(long id = 7, ModelRole role = ModelRole.Champion, bool heldOut = true, string featureVersion = "tf-1", char hash = 'e', DateOnly? holdOutStart = null) =>
        new(id, role, new ModelToRegister(
            new string('f', 64), new string(hash, 64), 3, "FastTree", null, new string('a', 64), featureVersion, 3, 30, 10, 40, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), heldOut ? holdOutStart ?? BlockStart : null, ValidationOutcome.Eligible, 6, 4, 4, true, 3.5, 0.6, 0.2,
            string.Empty, "report", "params", [0, 1], [0.4, 0.6], [0.5, 0.5, 0.5], 1, 3, DateTimeOffset.UnixEpoch, "t"),
        1);

    public static HoldOutBlock Block(DateOnly? start = null) => new(start ?? BlockStart, new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 31), 6, new string('c', 64), DateTimeOffset.UnixEpoch);

    /// <summary>A hold-out store in memory that keeps the two uniqueness rules of the database: one look per (id, version, kind), and one fresh look per block.</summary>
    public sealed class FakeHoldOutStore(HoldOutBlock? block) : IHoldOutStore
    {
        public List<StoredUnlock> Unlocks { get; } = [];

        /// <summary>The current block; a test advances it by setting this.</summary>
        public HoldOutBlock? Block { get; set; } = block;

        /// <summary>Runs after <see cref="AnyUnlockAsync"/> has answered, to let another look in between the read and the insert.</summary>
        public Action? AfterAnyUnlock { get; set; }

        public ValueTask<HoldOutBlock?> CurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Block);

        public ValueTask<bool> InsertBlockAsync(HoldOutBlock b, CancellationToken cancellationToken) => ValueTask.FromResult(false);

        public ValueTask<StoredUnlock?> GetUnlockAsync(string strategyId, int version, string kind, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Unlocks.FirstOrDefault(u => u.StrategyId == strategyId && u.Version == version && u.Kind == kind));

        public ValueTask<bool> AnyUnlockAsync(DateOnly blockStart, CancellationToken cancellationToken)
        {
            var any = Unlocks.Any(u => u.BlockStart == blockStart);
            AfterAnyUnlock?.Invoke();
            return ValueTask.FromResult(any);
        }

        public ValueTask<IReadOnlyList<StoredUnlock>> ListUnlocksAsync(int limit, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<StoredUnlock>>([.. Unlocks]);

        public ValueTask<bool> InsertUnlockAsync(StoredUnlock unlock, CancellationToken cancellationToken)
        {
            if (Unlocks.Any(u => u.StrategyId == unlock.StrategyId && u.Version == unlock.Version && u.Kind == unlock.Kind) || (!unlock.ReusedWindow && Unlocks.Any(u => !u.ReusedWindow && u.BlockStart == unlock.BlockStart)))
            {
                return ValueTask.FromResult(false);
            }

            Unlocks.Add(unlock);
            return ValueTask.FromResult(true);
        }
    }
}
