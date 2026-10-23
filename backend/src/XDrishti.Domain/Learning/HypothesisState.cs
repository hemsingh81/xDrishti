namespace XDrishti.Domain.Learning;

/// <summary>Where a hypothesis stands (spec P6-10, ADR 0076). Accepted, Rejected and Expired are final; the others are open and count against <c>max_open_hypotheses</c>.</summary>
public enum HypothesisState
{
    /// <summary>Written down (by the owner or the grid), not yet approved.</summary>
    Proposed,

    /// <summary>Approved to be tried; no candidate run is attached yet.</summary>
    Approved,

    /// <summary>A candidate run that differs from the baseline in the one knob is attached; not yet judged.</summary>
    Running,

    /// <summary>Judged by an experiment (P6-11); the verdict is not yet accepted or rejected.</summary>
    Judged,

    /// <summary>The owner accepted a Better verdict. Only a record: applying the change is a separate act of the owner.</summary>
    Accepted,

    /// <summary>Declined or not Better; final.</summary>
    Rejected,

    /// <summary>Withdrawn without a verdict; final.</summary>
    Expired,
}

/// <summary>The spelling of a <see cref="HypothesisState"/> in the table and the API.</summary>
public static class HypothesisStateText
{
    /// <summary>The lower-case code (<c>proposed</c>, <c>approved</c>, ...).</summary>
    public static string Code(this HypothesisState state) => state.ToString().ToLowerInvariant();

    /// <summary>Reads a code back; null when unknown.</summary>
    public static HypothesisState? TryParse(string? code) =>
        code is not null && Enum.GetValues<HypothesisState>().Cast<HypothesisState?>().FirstOrDefault(s => string.Equals(s!.Value.Code(), code, StringComparison.OrdinalIgnoreCase)) is { } found ? found : null;
}
