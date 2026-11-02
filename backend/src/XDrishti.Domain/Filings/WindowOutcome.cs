namespace XDrishti.Domain.Filings;

/// <summary>Where a results answer falls against a window of sessions (P15-06). <see cref="Unavailable"/> is its own outcome and is never read as <see cref="Outside"/>: every consumer takes its non-pass path for it.</summary>
public enum WindowOutcome
{
    /// <summary>The results event session is 0 to N sessions away.</summary>
    Inside,

    /// <summary>The next results event session is more than N sessions away.</summary>
    Outside,

    /// <summary>No visible calendar entry (or an answer that is not a non-negative number of sessions): nothing is known, and nothing counts as a pass.</summary>
    Unavailable,
}
