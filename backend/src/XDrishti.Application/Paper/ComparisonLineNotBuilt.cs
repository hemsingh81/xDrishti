using XDrishti.Application.Abstractions.Paper;

namespace XDrishti.Application.Paper;

/// <summary>The comparison line until P10-04 is built: says so, in the review's own words.</summary>
internal sealed class ComparisonLineNotBuilt : IReviewComparisonLine
{
    public ValueTask<string> LineAsync(long epochId, CancellationToken cancellationToken) =>
        ValueTask.FromResult("unavailable: the backtest comparison (P10-04) is not built, so no position against the band is given.");
}
