using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>A block of the chain with its derived state (HO-01, D2).</summary>
internal sealed record BlockStatus(StoredBlock Stored, HoldOutBlockState State, bool FreshLook)
{
    public HoldOutBlock Block => Stored.Block;

    /// <summary>The last date of the block.</summary>
    public DateOnly End => Stored.Block.HistoryLast;
}

/// <summary>
/// Reads the chain of hold-out blocks and derives each block's state from the stored blocks and releases, the stored fresh looks and the trading calendar (HO-01, D2). Reads only stored records and the calendar, never a bar. A read that fails throws, so every caller
/// fails closed.
/// </summary>
internal sealed class HoldOutEpochs(IHoldOutStore store, ITradingCalendar tradingCalendar, TimeProvider time)
{
    /// <summary>Every block of the chain, earliest first.</summary>
    public async ValueTask<IReadOnlyList<BlockStatus>> StatusAsync(CancellationToken cancellationToken)
    {
        var blocks = await store.ListBlocksAsync(cancellationToken);
        if (blocks.Count == 0)
        {
            return [];
        }

        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(time.GetUtcNow());
        var rows = new List<BlockStatus>(blocks.Count);
        foreach (var stored in blocks)
        {
            var released = stored.Release is not null;
            var sealedBlock = HoldOutChain.IsSealed(calendar, lastClosed, stored.Block.HistoryLast);
            var fresh = !released && sealedBlock && await store.HasFreshLookAsync(stored.Block.Start, cancellationToken);
            rows.Add(new BlockStatus(stored, HoldOutChain.StateOf(released, sealedBlock, fresh), fresh));
        }

        return rows;
    }

    /// <summary>The block in force with its state, or null when none is fixed.</summary>
    public async ValueTask<BlockStatus?> InForceAsync(CancellationToken cancellationToken) =>
        (await StatusAsync(cancellationToken)).FirstOrDefault(b => b.State != HoldOutBlockState.Released);

    /// <summary>The released blocks as (start, end) pairs, for the <c>seen_holdout</c> labels.</summary>
    public async ValueTask<IReadOnlyList<(DateOnly Start, DateOnly End)>> ReleasedAsync(CancellationToken cancellationToken) =>
        [.. (await store.ListBlocksAsync(cancellationToken)).Where(b => b.Release is not null).Select(b => (b.Block.Start, b.Block.HistoryLast))];
}
