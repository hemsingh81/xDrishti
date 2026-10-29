namespace XDrishti.Execution.Domain;

/// <summary>The 14 states of an execution intent (P12-01). The names are stored by name.</summary>
public enum ExecState
{
    Planned,
    Armed,
    Checking,
    Rejected,
    Submitting,
    Open,
    Filled,
    Protected,
    Managing,
    Closing,
    Closed,
    Reconciled,
    Cancelled,
    Failed,
}
