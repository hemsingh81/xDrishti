namespace XDrishti.Domain.Checklist;

/// <summary>One check with its standing.</summary>
/// <param name="Check">The check.</param>
/// <param name="Standing">Where it stands for approval.</param>
public sealed record ReviewedCheck(ChecklistCheck Check, CheckStanding Standing);
