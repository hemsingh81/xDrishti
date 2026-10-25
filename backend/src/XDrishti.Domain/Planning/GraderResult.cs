namespace XDrishti.Domain.Planning;

/// <summary>Either a grade, or "not yet": the stored bars do not yet reach the planned session, or the position is still open at the end of the data. A pending ticket is not written.</summary>
/// <param name="Grade">The grade; null when pending.</param>
/// <param name="PendingReason">Why it is pending; null when graded.</param>
/// <param name="Trade">The replay's trade: the closed one when graded, the still open one when pending for that reason (spec P10-01); otherwise null.</param>
public sealed record GraderResult(TicketGrade? Grade, string? PendingReason, XDrishti.Domain.Research.SimTrade? Trade = null)
{
    /// <summary>Whether nothing can be said yet.</summary>
    public bool IsPending => Grade is null;
}
