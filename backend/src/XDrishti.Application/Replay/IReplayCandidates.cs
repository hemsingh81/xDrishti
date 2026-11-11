using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay;

/// <summary>What the candidate source found for one session.</summary>
/// <param name="Found">The conditional tickets.</param>
/// <param name="Rejected">Pairs with a part false at the close.</param>
/// <param name="Unavailable">Pairs that could not be classified.</param>
/// <param name="Universe">Members of the universe.</param>
/// <param name="IncompleteBars">Members whose bars of the session are not complete.</param>
/// <param name="NoVersionCells">Cell keys whose strategy has no version under the rule.</param>
/// <param name="Survivorship">Whether the universe used the earliest known membership.</param>
/// <param name="Notes">Plain notes of the facts (for example the lifecycle label).</param>
public sealed record ReplayCandidateSet(
    IReadOnlyList<TicketCandidate> Found,
    int Rejected,
    int Unavailable,
    int Universe,
    int IncompleteBars,
    IReadOnlyList<string> NoVersionCells,
    bool Survivorship,
    IReadOnlyList<string> Notes);

/// <summary>Builds the conditional tickets of the next session from the as-of facts only (the way the live ticket source does, through <see cref="IReplayFacts"/>). Read only; the implementation runs the pure ticket builder.</summary>
public interface IReplayCandidates
{
    /// <summary>The tickets for <paramref name="forDate"/> built from data up to the close of <paramref name="session"/>.</summary>
    ValueTask<ReplayCandidateSet> ReadAsync(ReplayParams parameters, DateOnly session, DateOnly forDate, CancellationToken cancellationToken);
}
