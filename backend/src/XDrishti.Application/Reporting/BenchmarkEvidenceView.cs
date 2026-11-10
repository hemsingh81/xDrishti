namespace XDrishti.Application.Reporting;

/// <summary>The evidence of a benchmark read: it has no runs or trades, so it states its basis, the instruments behind it, its range, its limits and what is unavailable.</summary>
/// <param name="Basis">Always <c>price return, dividends not included</c>.</param>
/// <param name="N">Distinct instruments included in a basket figure.</param>
/// <param name="Range">The window.</param>
/// <param name="Limits">The honest limits, as text.</param>
/// <param name="UnavailableInputs">Inputs that do not exist, each with the reason.</param>
/// <param name="Completeness">Only on a training benchmark (AU-14): that it is a training simulation, not evidence, and the range cut to the training window; absent from the evidence benchmarks.</param>
public sealed record BenchmarkEvidenceView(string Basis, int N, BenchmarkWindowView Range, IReadOnlyList<string> Limits, IReadOnlyList<string> UnavailableInputs,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] TrainingCompletenessView? Completeness = null);
