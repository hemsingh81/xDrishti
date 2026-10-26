using XDrishti.Domain.Paper;

namespace XDrishti.Application.Abstractions.Paper;

/// <summary>The raw reads of the paper record behind the weekly review (spec P10-03): trades with their exits and skips of one epoch. Select only; the port has no write member.</summary>
public interface IPaperReviewData
{
    /// <summary>Every stored trade of the epoch with its exit, oldest entry first.</summary>
    ValueTask<IReadOnlyList<ReviewTrade>> TradesAsync(long epochId, CancellationToken cancellationToken);

    /// <summary>Every stored skip of the epoch.</summary>
    ValueTask<IReadOnlyList<ReviewSkip>> SkipsAsync(long epochId, CancellationToken cancellationToken);
}
