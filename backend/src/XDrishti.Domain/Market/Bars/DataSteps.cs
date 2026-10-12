namespace XDrishti.Domain.Market.Bars;

/// <summary>The three phases of a data job, in the order they run (ADR 0016 §2). The spelling is part of the API.</summary>
public static class DataSteps
{
    public const string Csv = "csv";
    public const string Dhan = "dhan";
    public const string Aggregate = "aggregate";

    public static readonly IReadOnlyList<string> All = [Csv, Dhan, Aggregate];

    public static bool IsKnown(string? step) => step is Csv or Dhan or Aggregate;
}

/// <summary>Where a phase of a data job stands. The spelling is part of the API.</summary>
public static class PhaseStatus
{
    /// <summary>The job was not asked to run this phase.</summary>
    public const string NotRequested = "notRequested";
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Succeeded = "succeeded";

    /// <summary>It could not run for a reason that is not an error (no Data account, nothing to do); see the phase's message and code.</summary>
    public const string Skipped = "skipped";
    public const string Failed = "failed";
}
