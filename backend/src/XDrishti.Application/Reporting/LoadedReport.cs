using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>What a report handler gets from the loader: the population (the population rule applied), the runs it was built over and how they were chosen.</summary>
/// <param name="Population">The trades after the population rule.</param>
/// <param name="Runs">The runs used, with their facts.</param>
/// <param name="DefaultRunSet">The caller did not name the runs.</param>
internal sealed record LoadedReport(ReportPopulation Population, IReadOnlyList<ReportRunInfo> Runs, bool DefaultRunSet);
