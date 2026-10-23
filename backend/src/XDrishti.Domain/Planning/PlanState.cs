namespace XDrishti.Domain.Planning;

/// <summary>Where a plan stands (spec P7-03, ADR 0083). The first state, Draft, has no row; Published is written in the same transaction as the plan.</summary>
public enum PlanState
{
    /// <summary>Being built by the pipeline; not visible as a plan.</summary>
    Draft,

    /// <summary>Proposed to the owner; nothing decided yet.</summary>
    Published,

    /// <summary>The owner has decided on some tickets.</summary>
    InReview,

    /// <summary>The review is complete and at least one ticket is approved.</summary>
    Approved,

    /// <summary>The owner rejected every ticket. Final.</summary>
    Rejected,

    /// <summary>The cutoff passed with nothing approved. Final.</summary>
    Lapsed,

    /// <summary>The pre-open check armed at least one ticket (watched, no order).</summary>
    Armed,

    /// <summary>The session of the plan is over. Final.</summary>
    Expired,
}

/// <summary>The spelling of a <see cref="PlanState"/> in the tables and the API.</summary>
public static class PlanStateText
{
    /// <summary>The lower-case code.</summary>
    public static string Code(this PlanState state) => state.ToString().ToLowerInvariant();

    /// <summary>Reads a code back; null when unknown.</summary>
    public static PlanState? TryParse(string? code) =>
        code is not null && Enum.GetValues<PlanState>().Cast<PlanState?>().FirstOrDefault(s => string.Equals(s!.Value.Code(), code, StringComparison.OrdinalIgnoreCase)) is { } found ? found : null;
}
