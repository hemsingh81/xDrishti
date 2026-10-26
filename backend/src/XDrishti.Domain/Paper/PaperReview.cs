namespace XDrishti.Domain.Paper;

/// <summary>The weekly paper review (spec P10-03): sections and the same as one text.</summary>
/// <param name="Sections">The sections in their fixed order.</param>
/// <param name="Text">The printable text; the same input always gives the same text.</param>
/// <param name="Discipline">The discipline report behind the review; null when the paper record could not be read or there is no epoch.</param>
public sealed record PaperReview(IReadOnlyList<ReviewSection> Sections, string Text, DisciplineReport? Discipline);
