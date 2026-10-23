namespace XDrishti.Domain.Learning;

/// <summary>
/// The lifecycle of a cell as a pure table (doc 09 section 7, spec P6-08, ADR 0073): the seven legal moves, and for each the facts the caller must state. Nothing here reads the clock, the database or a trade.
/// Nothing becomes <see cref="CellState.Active"/> without the paper period (doc 16 <c>paper_days_before_active</c>) and the owner; Paper and Active are records only until paper data exist (P10) and live exists (P7+).
/// </summary>
public static class CellLifecycle
{
    /// <summary>Doc 16 <c>paper_days_before_active</c>.</summary>
    public const int DefaultPaperDaysBeforeActive = 20;

    private static readonly (CellState From, CellState To)[] Legal =
    [
        (CellState.Research, CellState.Candidate),
        (CellState.Candidate, CellState.Paper),
        (CellState.Paper, CellState.Active),
        (CellState.Active, CellState.Paused),
        (CellState.Paused, CellState.Paper),
        (CellState.Active, CellState.Retired),
        (CellState.Research, CellState.Retired),
    ];

    /// <summary>Every legal move, in the order of doc 09 section 7.</summary>
    public static IReadOnlyList<(CellState From, CellState To)> Moves => Legal;

    /// <summary>The states a cell may move to from <paramref name="from"/>.</summary>
    public static IReadOnlyList<CellState> Next(CellState from) => [.. Legal.Where(m => m.From == from).Select(m => m.To)];

    /// <summary>Whether the table has this move.</summary>
    public static bool IsLegal(CellState from, CellState to) => Legal.Contains((from, to));

    /// <summary>Whether the move needs evidence ids (everything but the owner's approval of a candidate).</summary>
    public static bool NeedsEvidence(CellState from, CellState to) => IsLegal(from, to) && !(from == CellState.Candidate && to == CellState.Paper);

    /// <summary>Why the move is refused; empty when it may be recorded.</summary>
    /// <param name="from">The current state.</param>
    /// <param name="to">The wanted state.</param>
    /// <param name="facts">What the caller states.</param>
    /// <param name="paperDaysBeforeActive">The paper days needed before Active.</param>
    public static IReadOnlyList<string> Refusals(CellState from, CellState to, TransitionFacts facts, int paperDaysBeforeActive = DefaultPaperDaysBeforeActive)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!IsLegal(from, to))
        {
            var next = Next(from);
            return [next.Count == 0
                ? $"A {from.Code()} cell cannot move: {from.Code()} is final."
                : $"A {from.Code()} cell cannot move to {to.Code()}; it can move to {string.Join(" or ", next.Select(s => s.Code()))}."];
        }

        var reasons = new List<string>();
        switch (from, to)
        {
            case (CellState.Research, CellState.Candidate) when !facts.GatesPassed:
                reasons.Add("The out-of-sample qualification gates have not passed.");
                break;
            case (CellState.Candidate, CellState.Paper) when !facts.OwnerApproved:
                reasons.Add("Only the owner moves a candidate to paper.");
                break;
            case (CellState.Paper, CellState.Active):
                if (!facts.OwnerApproved)
                {
                    reasons.Add("Only the owner makes a cell active.");
                }

                if (!facts.WithinBand)
                {
                    reasons.Add("The paper results are not within the backtest band.");
                }

                if (facts.PaperDays < paperDaysBeforeActive)
                {
                    reasons.Add($"The paper period is {facts.PaperDays} days; {paperDaysBeforeActive} are needed before a cell can be active.");
                }

                break;
            case (CellState.Paused, CellState.Paper) when !facts.Recovered:
                reasons.Add("The paused cell has not recovered in shadow.");
                break;
            default:
                break;
        }

        return reasons;
    }

    /// <summary>
    /// Why a revert that would restore <paramref name="restored"/> is refused. A revert is a correction, not a lifecycle move, so it is not checked against the table, but it may never restore Active (that needs the paper period again).
    /// Empty when it may.
    /// </summary>
    /// <param name="restored">The state the revert would restore.</param>
    public static IReadOnlyList<string> RevertRefusals(CellState restored) =>
        restored == CellState.Active ? ["A revert cannot make a cell active again; it needs the paper period and the owner (Paper to Active)."] : [];
}
