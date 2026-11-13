using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>Maps a block of the chain to the view served by <c>GET /api/lab/holdout</c> and the release (HO-01).</summary>
internal static class BlockViews
{
    /// <param name="status">The block.</param>
    /// <param name="open">For the block in force: what each kind can still do (HO-01b); null for the other blocks.</param>
    /// <param name="blockedBy">For the block in force: what keeps the auto release from acting, or null.</param>
    public static HoldOutBlockView Of(BlockStatus status, IReadOnlyDictionary<string, LookAvailability>? open = null, string? blockedBy = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        var block = status.Block;
        var looks = new HoldOutLooksView(LookOf(status, "strategy", open), LookOf(status, "model", open), LookOf(status, HoldOutHorizon.SystemKind, open));
        return new HoldOutBlockView(block.Start, block.HistoryFirst, block.HistoryLast, block.LengthMonths, block.SettingHash, block.CreatedAt, HoldOutChain.Word(status.State), looks, status.Stored.Release?.ReleasedAt, status.Stored.Release?.Mode, blockedBy);
    }

    private static HoldOutLookView LookOf(BlockStatus status, string kind, IReadOnlyDictionary<string, LookAvailability>? open)
    {
        if (status.FreshLooks.FirstOrDefault(l => string.Equals(l.Kind, kind, StringComparison.Ordinal)) is { } look)
        {
            return new HoldOutLookView("fresh", kind == "strategy" ? $"{look.StrategyId} v{look.Version}" : look.StrategyId, look.Result.Passed, look.Result.Status, look.UnlockedAt, look.Mode);
        }

        return open is not null && open.TryGetValue(kind, out var availability)
            ? new HoldOutLookView(availability switch
            {
                LookAvailability.Available => "available",
                LookAvailability.Unavailable => "unavailable",
                _ => "no_subject",
            })
            : new HoldOutLookView("none");
    }
}
