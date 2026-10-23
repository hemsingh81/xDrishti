namespace XDrishti.Domain.Planning;

/// <summary>Either a grade, or "not yet": the stored bars do not yet reach the planned session, or the position is still open at the end of the data. A pending ticket is not written.</summary>
/// <param name="Grade">The grade; null when pending.</param>
/// <param name="PendingReason">Why it is pending; null when graded.</param>
public sealed record GraderResult(TicketGrade? Grade, string? PendingReason)
{
    /// <summary>Whether nothing can be said yet.</summary>
    public bool IsPending => Grade is null;
}
