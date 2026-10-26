using XDrishti.Domain.Paper;

namespace XDrishti.Application.Abstractions.Paper;

/// <summary>
/// Everything the weekly paper review reads (spec P10-03, ADR 0121), as read-only members: the epochs, the tickets of a session with their decisions and stored grades, the stored paper trades and skips, and the journal's discretionary fills.
/// The review handler holds this port and the review store and nothing else, so it cannot reach a plan move, an approval, a setting, a hypothesis or a broker.
/// </summary>
public interface IPaperReviewSource
{
    /// <summary>The epochs with their counts, newest first.</summary>
    ValueTask<IReadOnlyList<EpochSummary>> EpochsAsync(CancellationToken cancellationToken);

    /// <summary>The tickets (primary and reserve) of the newest plan for <paramref name="session"/>; empty when there is no plan.</summary>
    ValueTask<IReadOnlyList<ReviewTicket>> TicketsAsync(DateOnly session, CancellationToken cancellationToken);

    /// <summary>Every stored paper trade of the epoch with its exit.</summary>
    ValueTask<IReadOnlyList<ReviewTrade>> TradesAsync(long epochId, CancellationToken cancellationToken);

    /// <summary>Every stored skip of the epoch.</summary>
    ValueTask<IReadOnlyList<ReviewSkip>> SkipsAsync(long epochId, CancellationToken cancellationToken);

    /// <summary>The discretionary real fills journaled for <paramref name="session"/> (one session per call).</summary>
    ValueTask<IReadOnlyList<ReviewFill>> DiscretionaryAsync(DateOnly session, CancellationToken cancellationToken);
}
