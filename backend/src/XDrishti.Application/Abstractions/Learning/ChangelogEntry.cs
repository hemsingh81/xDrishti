namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A stored changelog row (spec P6-08). Fields as in <see cref="NewChangelogEntry"/>.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Entry">What was written.</param>
public sealed record ChangelogEntry(long Id, NewChangelogEntry Entry);
