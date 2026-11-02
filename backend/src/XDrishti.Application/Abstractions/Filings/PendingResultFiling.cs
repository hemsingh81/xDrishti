namespace XDrishti.Application.Abstractions.Filings;

/// <summary>A stored results envelope that has no parse row for the current parser version.</summary>
public sealed record PendingResultFiling(Guid Id, DateOnly? PeriodEnd, string ContentSha256, string FileName);
