namespace XDrishti.Domain.Research;

/// <summary>One excluded session (spec P3-04 Scope 4): the date, why, how many bars the window of the session held and how many a complete session has (0 for a closed day and a missing session).</summary>
public sealed record SessionNote(DateOnly Date, SessionExclusion Reason, int BarsInWindow, int ExpectedMinutes);
