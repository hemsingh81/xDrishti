using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>A block of the chain with its derived state (HO-01, D2).</summary>
internal sealed record BlockStatus(StoredBlock Stored, HoldOutBlockState State, IReadOnlyList<StoredUnlock> FreshLooks)
{
    /// <summary>Whether any kind has a fresh look at the block.</summary>
    public bool FreshLook => FreshLooks.Count > 0;

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
            var looks = sealedBlock ? await store.ListFreshLooksAsync(stored.Block.Start, cancellationToken) : [];
            rows.Add(new BlockStatus(stored, HoldOutChain.StateOf(released, sealedBlock, !released && looks.Count > 0), looks));
        }

        return rows;
    }

    /// <summary>Whether the block that ends on <paramref name="end"/> is sealed now (no trading day is left up to its end).</summary>
    public async ValueTask<bool> IsSealedAsync(DateOnly end, CancellationToken cancellationToken)
    {
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        return HoldOutChain.IsSealed(calendar, calendar.LastClosedSession(time.GetUtcNow()), end);
    }

    /// <summary>How many trading sessions have closed after the block that ends on <paramref name="end"/> (0 while it is filling): the age of a sealed block.</summary>
    public async ValueTask<int> SealedSessionsAsync(DateOnly end, CancellationToken cancellationToken)
    {
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        return calendar.TradingDaysBetween(end, calendar.LastClosedSession(time.GetUtcNow()));
    }

    /// <summary>The block in force with its state, or null when none is fixed.</summary>
    public async ValueTask<BlockStatus?> InForceAsync(CancellationToken cancellationToken) =>
        (await StatusAsync(cancellationToken)).FirstOrDefault(b => b.State != HoldOutBlockState.Released);

    /// <summary>The released blocks as (start, end) pairs, for the <c>seen_holdout</c> labels.</summary>
    public ValueTask<IReadOnlyList<(DateOnly Start, DateOnly End)>> ReleasedAsync(CancellationToken cancellationToken) => store.ReleasedAsync(cancellationToken);
}
