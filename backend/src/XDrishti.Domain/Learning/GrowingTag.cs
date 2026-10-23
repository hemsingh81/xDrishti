namespace XDrishti.Domain.Learning;

/// <summary>A tag whose share of a cell's losses rose between two consecutive windows of losses (P6-15). A candidate lesson for P6-16; nothing is opened here.</summary>
/// <param name="CellKey">The cell.</param>
/// <param name="Kind">The tag.</param>
/// <param name="RecentCount">Latest window: losses with the tag.</param>
/// <param name="EarlierCount">The window before: losses with the tag.</param>
/// <param name="Window">Losses in each window.</param>
public sealed record GrowingTag(string CellKey, LossTagKind Kind, int RecentCount, int EarlierCount, int Window)
{
    /// <summary>Share of the latest window.</summary>
    public double RecentShare => (double)RecentCount / Window;

    /// <summary>Share of the earlier window.</summary>
    public double EarlierShare => (double)EarlierCount / Window;
}
