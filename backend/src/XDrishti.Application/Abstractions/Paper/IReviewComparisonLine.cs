namespace XDrishti.Application.Abstractions.Paper;

/// <summary>
/// The one line of the weekly review about the backtest comparison (spec P10-03; the comparison itself is P10-04). The default says it is not built; P10-04 replaces the registration. A failing line is shown as unavailable and the rest of the review is still produced.
/// </summary>
public interface IReviewComparisonLine
{
    /// <summary>One sentence about the epoch's position against the backtest band.</summary>
    ValueTask<string> LineAsync(long epochId, CancellationToken cancellationToken);
}
