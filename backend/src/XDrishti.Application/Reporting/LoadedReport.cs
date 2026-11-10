using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// What a report handler gets from the loader: the population (the population rule applied), the runs it was built over and how they were chosen. It owns the place in the <see cref="ReportGate"/>: the handler disposes it when the answer is built, and only then
/// may the next report load its population.
/// </summary>
internal sealed class LoadedReport(ReportPopulation population, IReadOnlyList<ReportRunInfo> runs, bool defaultRunSet, IDisposable lease, TrainingCompletenessView? completeness = null) : IDisposable
{
    /// <summary>The trades after the population rule.</summary>
    public ReportPopulation Population { get; } = population;

    /// <summary>The runs used, with their facts.</summary>
    public IReadOnlyList<ReportRunInfo> Runs { get; } = runs;

    /// <summary>The caller did not name the runs.</summary>
    public bool DefaultRunSet { get; } = defaultRunSet;

    /// <summary>Set for a report over a simulate-everything job (AU-14); null for the evidence reports.</summary>
    public TrainingCompletenessView? Completeness { get; } = completeness;

    public void Dispose() => lease.Dispose();
}
