namespace XDrishti.Domain.Learning;

/// <summary>
/// The lesson state machine as a pure table (spec P6-16, ADR 0078): open to linked or dismissed; linked to closed or dismissed. Closed and dismissed are final. Linking, closing and dismissing are the owner's; closing needs a linked fix
/// whose experiment has been recorded, so the result always comes from stored numbers. Nothing here applies a change.
/// </summary>
public static class LessonLifecycle
{
    private static readonly (LessonState From, LessonState To)[] Legal =
    [
        (LessonState.Open, LessonState.Linked),
        (LessonState.Open, LessonState.Dismissed),
        (LessonState.Linked, LessonState.Closed),
        (LessonState.Linked, LessonState.Dismissed),
    ];

    /// <summary>Every legal move.</summary>
    public static IReadOnlyList<(LessonState From, LessonState To)> Moves => Legal;

    /// <summary>The states reachable from <paramref name="from"/>.</summary>
    public static IReadOnlyList<LessonState> Next(LessonState from) => [.. Legal.Where(m => m.From == from).Select(m => m.To)];

    /// <summary>Whether the table has this move.</summary>
    public static bool IsLegal(LessonState from, LessonState to) => Legal.Contains((from, to));

    /// <summary>Why the move is refused; empty when it may be recorded.</summary>
    public static IReadOnlyList<string> Refusals(LessonState from, LessonState to, LessonFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!IsLegal(from, to))
        {
            var next = Next(from);
            return [next.Count == 0
                ? $"A {from.Code()} lesson cannot move: {from.Code()} is final."
                : $"A {from.Code()} lesson cannot move to {to.Code()}; it can move to {string.Join(" or ", next.Select(s => s.Code()))}."];
        }

        var reasons = new List<string>();
        if (!facts.ByOwner)
        {
            reasons.Add($"Only the owner moves a lesson to {to.Code()}.");
        }

        if (to == LessonState.Closed && !facts.FixJudged)
        {
            reasons.Add(facts.HasFix
                ? "The linked fix has no recorded experiment yet; judge it first, or dismiss the lesson."
                : "A lesson is closed with the numbers of a linked fix.");
        }

        return reasons;
    }
}
