using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>Maps a block of the chain to the view served by <c>GET /api/lab/holdout</c> and the release (HO-01).</summary>
internal static class BlockViews
{
    public static HoldOutBlockView Of(BlockStatus status)
    {
        var block = status.Block;
        return new HoldOutBlockView(block.Start, block.HistoryFirst, block.HistoryLast, block.LengthMonths, block.SettingHash, block.CreatedAt, HoldOutChain.Word(status.State), status.Stored.Release?.Looked ?? status.FreshLook, status.Stored.Release?.ReleasedAt, status.Stored.Release?.Mode);
    }
}
