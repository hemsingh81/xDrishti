namespace XDrishti.Domain.Learning;

/// <summary>One section of the weekly digest (P7-15). <see cref="Lines"/> is never empty: a section with nothing to list says so.</summary>
/// <param name="Key">The stable key (<c>cells</c>, <c>drift</c> ...).</param>
/// <param name="Title">The heading.</param>
/// <param name="Lines">The lines, in order.</param>
public sealed record DigestSection(string Key, string Title, IReadOnlyList<string> Lines);
