namespace XDrishti.Domain.Paper;

/// <summary>The paper book cannot be trusted for the plan: the plan takes nothing from it (the <c>book:unavailable</c> rule of ADR 0092).</summary>
/// <param name="Reason">The sentence the plan shows.</param>
public sealed record PaperBookUnavailable(string Reason) : PaperBookRead;
