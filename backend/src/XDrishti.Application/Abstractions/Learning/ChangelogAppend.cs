namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The result of appending a changelog row (spec P6-08).</summary>
/// <param name="Id">The new row id, or 0 when nothing was written.</param>
/// <param name="Stale">Nothing was written: the subject's latest row changed since the caller read it.</param>
public sealed record ChangelogAppend(long Id, bool Stale);
