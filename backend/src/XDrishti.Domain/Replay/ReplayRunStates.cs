namespace XDrishti.Domain.Replay;

/// <summary>The states of a stored replay run (SR-01 part B). A run is <c>queued</c> until the job claims it, <c>running</c> while it steps its sessions, <c>cancelling</c> between a cancel and the end of the session in flight; the other three are final.</summary>
public static class ReplayRunStates
{
    /// <summary>Stored, waiting for the job.</summary>
    public const string Queued = "queued";

    /// <summary>The job is stepping its sessions.</summary>
    public const string Running = "running";

    /// <summary>A cancel was asked; the run stops after the session in flight.</summary>
    public const string Cancelling = "cancelling";

    /// <summary>Every session was replayed.</summary>
    public const string Completed = "completed";

    /// <summary>A session failed; the reason is stored and nothing after it was replayed.</summary>
    public const string Failed = "failed";

    /// <summary>Stopped by the owner after a session; the finished sessions are kept.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Whether the run still holds the one running-replay slot.</summary>
    public static bool IsActive(string state) => state is Queued or Running or Cancelling;
}
