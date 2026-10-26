namespace XDrishti.Domain.Paper;

/// <summary>One section of the weekly paper review (spec P10-03): lines of text, figures with their states, an optional table and the sentence of its limits that ends it.</summary>
/// <param name="Id">The section's name.</param>
/// <param name="Title">The words.</param>
/// <param name="Lines">The text lines.</param>
/// <param name="Metrics">The figures.</param>
/// <param name="Limits">The limits sentence.</param>
/// <param name="Table">A table, or null.</param>
public sealed record ReviewSection(string Id, string Title, IReadOnlyList<string> Lines, IReadOnlyList<ReviewMetric> Metrics, string Limits, ReviewTable? Table = null);
