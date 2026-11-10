namespace XDrishti.Domain.Reporting;

/// <summary>One day of the equal-weight basket index (BK-06 m1b).</summary>
/// <param name="Date">The session.</param>
/// <param name="PreviousSession">The session the return is measured from (the date itself for the base point).</param>
/// <param name="Level">The index level, 100 at the base point.</param>
/// <param name="Return">The day's index return (fraction); 0 and meaningless at the base point.</param>
/// <param name="Members">Members the return is the mean of; 0 at the base point.</param>
public sealed record IndexPoint(DateOnly Date, DateOnly PreviousSession, double Level, double Return, int Members);
