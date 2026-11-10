namespace XDrishti.Domain.Research.SimAll;

/// <summary>The state codes of the simulate-everything job and its chunks (spec AU-12, ADR 0248). Text codes, as stored.</summary>
public static class SimAllStates
{
    /// <summary>The job is being worked on.</summary>
    public const string Running = "running";

    /// <summary>The job waits: by the owner, or because a yield rule holds (see <see cref="SimAllPause"/>).</summary>
    public const string Paused = "paused";

    /// <summary>Every chunk is done.</summary>
    public const string Completed = "completed";

    /// <summary>Every chunk is finished and at least one failed.</summary>
    public const string CompletedWithFailures = "completed_with_failures";

    /// <summary>The owner stopped the job; chunks not yet done stay pending.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>A chunk waiting for a worker.</summary>
    public const string Pending = "pending";

    /// <summary>A chunk a worker has claimed; one found so after a restart is redone.</summary>
    public const string ChunkRunning = "running";

    /// <summary>A chunk whose run is stored.</summary>
    public const string Done = "done";

    /// <summary>A chunk that failed, with its reason; the job goes on.</summary>
    public const string Failed = "failed";

    /// <summary>True for a state a job does not leave.</summary>
    public static bool IsFinal(string state) => state is Completed or CompletedWithFailures or Cancelled;

    /// <summary>The state a job ends in once no chunk is pending or running.</summary>
    public static string EndState(int failed) => failed > 0 ? CompletedWithFailures : Completed;
}
