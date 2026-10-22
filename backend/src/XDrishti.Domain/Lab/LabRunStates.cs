namespace XDrishti.Domain.Lab;

/// <summary>The words of the states and the legal moves between them (spec P5-02, D1): a run only ever goes forward, a finished run never changes.</summary>
public static class LabRunStates
{
    /// <summary>The stored and served word of a state.</summary>
    public static string Code(LabRunState state) => state switch
    {
        LabRunState.Queued => "queued",
        LabRunState.Running => "running",
        LabRunState.Succeeded => "succeeded",
        LabRunState.Failed => "failed",
        LabRunState.Cancelled => "cancelled",
        LabRunState.Interrupted => "interrupted",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>The state of a word, or false.</summary>
    public static bool TryParse(string? code, out LabRunState state)
    {
        foreach (var candidate in Enum.GetValues<LabRunState>())
        {
            if (string.Equals(Code(candidate), code, StringComparison.Ordinal))
            {
                state = candidate;
                return true;
            }
        }

        state = default;
        return false;
    }

    /// <summary>True when no move is legal any more.</summary>
    public static bool IsTerminal(LabRunState state) => state is not (LabRunState.Queued or LabRunState.Running);

    /// <summary>True for the states in which a second post of the same input returns the run instead of making one (D3): queued, running and succeeded.</summary>
    public static bool HoldsKey(LabRunState state) => state is LabRunState.Queued or LabRunState.Running or LabRunState.Succeeded;

    /// <summary>True when the owner may still cancel the run.</summary>
    public static bool IsCancellable(LabRunState state) => state is LabRunState.Queued or LabRunState.Running;

    /// <summary>True when a run may move from one state to the other.</summary>
    public static bool CanMove(LabRunState from, LabRunState to) => from switch
    {
        LabRunState.Queued => to is LabRunState.Running or LabRunState.Cancelled,
        LabRunState.Running => to is LabRunState.Succeeded or LabRunState.Failed or LabRunState.Cancelled or LabRunState.Interrupted,
        _ => false,
    };
}
