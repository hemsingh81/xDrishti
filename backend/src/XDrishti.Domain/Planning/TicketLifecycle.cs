using XDrishti.Domain.Research;

namespace XDrishti.Domain.Planning;

/// <summary>
/// The ticket state machine as a pure table (spec P7-03, ADR 0083): the diagram of doc 04 §4.2 plus Modified to Rejected and Modified to Lapsed (an edited ticket can still be turned down or left unreviewed) and Approved to Expired
/// (the window ended before the pre-open check armed it). Thirteen moves; every other pair is refused naming the legal moves. Into Approved, Modified and Rejected only the owner moves; there is no move that a service can make into Approved,
/// and a Reserve ticket reaches Armed only through Approved. A rejected candidate is not a ticket and has no moves.
/// </summary>
public static class TicketLifecycle
{
    private static readonly (TicketState From, TicketState To)[] Legal =
    [
        (TicketState.Proposed, TicketState.Approved),
        (TicketState.Proposed, TicketState.Modified),
        (TicketState.Proposed, TicketState.Rejected),
        (TicketState.Proposed, TicketState.Lapsed),
        (TicketState.Modified, TicketState.Approved),
        (TicketState.Modified, TicketState.Rejected),
        (TicketState.Modified, TicketState.Lapsed),
        (TicketState.Approved, TicketState.Armed),
        (TicketState.Approved, TicketState.Invalidated),
        (TicketState.Approved, TicketState.Expired),
        (TicketState.Armed, TicketState.Triggered),
        (TicketState.Armed, TicketState.Expired),
        (TicketState.Triggered, TicketState.Closed),
    ];

    /// <summary>The states of a ticket the owner approved and that went on from there: the only tickets a fill can be matched to (the journal, P7-14) and a prompt about fills can be about (the bell, P8-15).</summary>
    public static IReadOnlyList<TicketState> ApprovedFamily { get; } = [TicketState.Approved, TicketState.Armed, TicketState.Triggered, TicketState.Closed];

    /// <summary>The side of a ticket (<c>BUY</c> or <c>SELL</c>) as a trade side; null for any other text.</summary>
    public static Side? ParseSide(string side) => side switch
    {
        "BUY" => Side.Buy,
        "SELL" => Side.Sell,
        _ => null,
    };

    /// <summary>Every legal move.</summary>
    public static IReadOnlyList<(TicketState From, TicketState To)> Moves => Legal;

    /// <summary>The states reachable from <paramref name="from"/>.</summary>
    public static IReadOnlyList<TicketState> Next(TicketState from) => [.. Legal.Where(m => m.From == from).Select(m => m.To)];

    /// <summary>Whether the table has this move.</summary>
    public static bool IsLegal(TicketState from, TicketState to) => Legal.Contains((from, to));

    /// <summary>Whether only the owner may move a ticket into this state (the decisions).</summary>
    public static bool IsOwnerDecision(TicketState to) => to is TicketState.Approved or TicketState.Modified or TicketState.Rejected;

    /// <summary>The state a ticket takes when the review cutoff passes without a decision: Lapsed, for a ticket nobody decided on, and no move for any other. Never an approval.</summary>
    public static TicketState? CutoffDefault(TicketState state) => state is TicketState.Proposed or TicketState.Modified ? TicketState.Lapsed : null;

    /// <summary>Why the move is refused; empty when it may be recorded.</summary>
    public static IReadOnlyList<string> Refusals(TicketState from, TicketState to, TicketFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.Group == TicketGroup.Rejected)
        {
            return ["A rejected candidate is not a ticket; it is kept with its reasons and graded, and has no moves."];
        }

        if (!IsLegal(from, to))
        {
            var next = Next(from);
            return [next.Count == 0
                ? $"A {from.Code()} ticket cannot move: {from.Code()} is final."
                : $"A {from.Code()} ticket cannot move to {to.Code()}; it can move to {string.Join(" or ", next.Select(s => s.Code()))}."];
        }

        var reasons = new List<string>();
        if (IsOwnerDecision(to) && facts.Actor != MoveActor.Owner)
        {
            reasons.Add($"Only the owner moves a ticket to {to.Code()}; nothing approves, modifies or rejects on the owner's behalf.");
        }
        else if (!IsOwnerDecision(to) && facts.Actor != MoveActor.System)
        {
            reasons.Add($"A ticket is moved to {to.Code()} by the services, not by hand.");
        }

        return reasons;
    }
}
