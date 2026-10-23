namespace XDrishti.Domain.Learning;

/// <summary>
/// The hypothesis state machine as a pure table (spec P6-10, ADR 0076; doc 09 section 5): the legal moves and for each the facts the caller must state. No clock, no database. Approval is the owner's unless the autonomy level is
/// <see cref="AutonomyLevel.Autonomous"/> (doc 16 <c>require_approval: per_autonomy</c>); starting, judging and accepting are always the owner's and needs a Better experiment; nothing here applies a change.
/// </summary>
public static class HypothesisLifecycle
{
    /// <summary>Doc 16 <c>hypothesis_loop.max_open_hypotheses</c>.</summary>
    public const int DefaultMaxOpen = 3;

    private static readonly (HypothesisState From, HypothesisState To)[] Legal =
    [
        (HypothesisState.Proposed, HypothesisState.Approved),
        (HypothesisState.Proposed, HypothesisState.Rejected),
        (HypothesisState.Proposed, HypothesisState.Expired),
        (HypothesisState.Approved, HypothesisState.Running),
        (HypothesisState.Approved, HypothesisState.Rejected),
        (HypothesisState.Approved, HypothesisState.Expired),
        (HypothesisState.Running, HypothesisState.Judged),
        (HypothesisState.Running, HypothesisState.Expired),
        (HypothesisState.Judged, HypothesisState.Accepted),
        (HypothesisState.Judged, HypothesisState.Rejected),
        (HypothesisState.Judged, HypothesisState.Expired),
    ];

    /// <summary>Every legal move.</summary>
    public static IReadOnlyList<(HypothesisState From, HypothesisState To)> Moves => Legal;

    /// <summary>The states reachable from <paramref name="from"/>.</summary>
    public static IReadOnlyList<HypothesisState> Next(HypothesisState from) => [.. Legal.Where(m => m.From == from).Select(m => m.To)];

    /// <summary>Whether the table has this move.</summary>
    public static bool IsLegal(HypothesisState from, HypothesisState to) => Legal.Contains((from, to));

    /// <summary>Open states count against the limit.</summary>
    public static bool IsOpen(HypothesisState state) => state is HypothesisState.Proposed or HypothesisState.Approved or HypothesisState.Running or HypothesisState.Judged;

    /// <summary>Why a new proposal is refused; empty when it may be written.</summary>
    public static IReadOnlyList<string> ProposalRefusals(HypothesisFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.OpenCount >= facts.MaxOpen
            ? [$"{facts.OpenCount} hypotheses are open and the limit is {facts.MaxOpen}; accept, reject or expire one first."]
            : [];
    }

    /// <summary>Why the move is refused; empty when it may be recorded.</summary>
    public static IReadOnlyList<string> Refusals(HypothesisState from, HypothesisState to, HypothesisFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!IsLegal(from, to))
        {
            var next = Next(from);
            return [next.Count == 0
                ? $"A {from.Code()} hypothesis cannot move: {from.Code()} is final."
                : $"A {from.Code()} hypothesis cannot move to {to.Code()}; it can move to {string.Join(" or ", next.Select(s => s.Code()))}."];
        }

        var reasons = new List<string>();
        switch (from, to)
        {
            case (HypothesisState.Proposed, HypothesisState.Approved) when !facts.ByOwner && facts.Autonomy != AutonomyLevel.Autonomous:
                reasons.Add($"At autonomy level {facts.Autonomy.ToString().ToLowerInvariant()} only the owner approves a hypothesis.");
                break;
            case (HypothesisState.Approved, HypothesisState.Running) when !facts.ByOwner:
                reasons.Add("Only the owner attaches a candidate run.");
                break;
            case (HypothesisState.Running, HypothesisState.Judged) when !facts.ByOwner:
                reasons.Add("Only the owner judges a hypothesis; a judgement is a trial that raises the bar.");
                break;
            case (HypothesisState.Judged, HypothesisState.Accepted):
                if (!facts.ByOwner)
                {
                    reasons.Add("Only the owner accepts a verdict.");
                }

                if (facts.Outcome != ExperimentOutcome.Better)
                {
                    reasons.Add("Only a Better experiment can be accepted.");
                }

                break;
            case (_, HypothesisState.Rejected) when !facts.ByOwner:
                reasons.Add("Only the owner rejects a hypothesis.");
                break;
            default:
                break;
        }

        return reasons;
    }
}
