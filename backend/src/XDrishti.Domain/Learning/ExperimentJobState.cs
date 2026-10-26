namespace XDrishti.Domain.Learning;

/// <summary>The state of a replay job started from the screen (spec P8-18 Part B). A final state is final.</summary>
public enum ExperimentJobState
{
    /// <summary>Waiting for the one place there is.</summary>
    Queued,

    /// <summary>Being replayed.</summary>
    Running,

    /// <summary>Replayed, stored and judged (the judgement is in the ledger).</summary>
    Succeeded,

    /// <summary>Ended without a judgement (an expected refusal, a timeout, a crash): not a trial.</summary>
    Failed,

    /// <summary>Stopped on the owner's request: nothing stored, not a trial.</summary>
    Cancelled,

    /// <summary>The host stopped while it ran; never resumed.</summary>
    Interrupted,
}

/// <summary>The words and the legal moves of <see cref="ExperimentJobState"/>.</summary>
public static class ExperimentJobStates
{
    public static string Code(ExperimentJobState state) => state switch
    {
        ExperimentJobState.Queued => "queued",
        ExperimentJobState.Running => "running",
        ExperimentJobState.Succeeded => "succeeded",
        ExperimentJobState.Failed => "failed",
        ExperimentJobState.Cancelled => "cancelled",
        ExperimentJobState.Interrupted => "interrupted",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static ExperimentJobState? Parse(string? code) => code switch
    {
        "queued" => ExperimentJobState.Queued,
        "running" => ExperimentJobState.Running,
        "succeeded" => ExperimentJobState.Succeeded,
        "failed" => ExperimentJobState.Failed,
        "cancelled" => ExperimentJobState.Cancelled,
        "interrupted" => ExperimentJobState.Interrupted,
        _ => null,
    };

    public static bool IsActive(ExperimentJobState state) => state is ExperimentJobState.Queued or ExperimentJobState.Running;

    public static bool IsFinal(ExperimentJobState state) => !IsActive(state);

    /// <summary>Queued goes to running or cancelled; running to a final state; a final state goes nowhere.</summary>
    public static bool CanMove(ExperimentJobState from, ExperimentJobState to) => from switch
    {
        ExperimentJobState.Queued => to is ExperimentJobState.Running or ExperimentJobState.Cancelled,
        ExperimentJobState.Running => to is ExperimentJobState.Succeeded or ExperimentJobState.Failed or ExperimentJobState.Cancelled or ExperimentJobState.Interrupted,
        _ => false,
    };
}
