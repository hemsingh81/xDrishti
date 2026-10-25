namespace XDrishti.Application.Reporting;

/// <summary>The evidence of a benchmark read: it has no runs or trades, so it states its basis, the instruments behind it, its range, its limits and what is unavailable.</summary>
/// <param name="Basis">Always <c>price return, dividends not included</c>.</param>
/// <param name="N">Distinct instruments included in a basket figure.</param>
/// <param name="Range">The window.</param>
/// <param name="Limits">The honest limits, as text.</param>
/// <param name="UnavailableInputs">Inputs that do not exist, each with the reason.</param>
public sealed record BenchmarkEvidenceView(string Basis, int N, BenchmarkWindowView Range, IReadOnlyList<string> Limits, IReadOnlyList<string> UnavailableInputs);
