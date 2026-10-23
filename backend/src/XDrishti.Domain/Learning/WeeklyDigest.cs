namespace XDrishti.Domain.Learning;

/// <summary>The weekly learning digest (P7-15, ADR 0094) built from stored records: the sections and the same as one text. Nothing in it was applied; every item is a proposal for the owner.</summary>
/// <param name="Week">The ISO week.</param>
/// <param name="Sections">The sections in their fixed order.</param>
/// <param name="Text">The printable text; the same input always gives the same text.</param>
public sealed record WeeklyDigest(DigestWeek Week, IReadOnlyList<DigestSection> Sections, string Text);
