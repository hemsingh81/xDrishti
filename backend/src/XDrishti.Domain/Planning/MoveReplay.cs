namespace XDrishti.Domain.Planning;

/// <summary>The state a trail of moves ends in, and what is wrong with the trail.</summary>
/// <typeparam name="TState">The state type.</typeparam>
/// <param name="State">The state after the last move (the initial state without moves).</param>
/// <param name="Problems">A move that does not start where the one before ended, or is not in the table.</param>
public sealed record MoveReplayResult<TState>(TState State, IReadOnlyList<string> Problems)
    where TState : struct, Enum;

/// <summary>The state is the fold of the trail (spec P7-03, the idea of ADR 0073): no state column exists, so the stored state and the replay cannot disagree.</summary>
public static class MoveReplay
{
    /// <summary>Replays <paramref name="moves"/> in order from <paramref name="initial"/>.</summary>
    public static MoveReplayResult<TState> Fold<TState>(TState initial, IEnumerable<(TState From, TState To)> moves, Func<TState, TState, bool> isLegal)
        where TState : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(moves);
        ArgumentNullException.ThrowIfNull(isLegal);
        var state = initial;
        var problems = new List<string>();
        var n = 0;
        foreach (var (from, to) in moves)
        {
            n++;
            if (!EqualityComparer<TState>.Default.Equals(from, state))
            {
                problems.Add($"Move {n} starts at {from} but the ticket was at {state}.");
            }
            else if (!isLegal(from, to))
            {
                problems.Add($"Move {n} from {from} to {to} is not in the table.");
            }

            state = to;
        }

        return new MoveReplayResult<TState>(state, problems);
    }
}
