using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Application.Abstractions.Replay;

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
