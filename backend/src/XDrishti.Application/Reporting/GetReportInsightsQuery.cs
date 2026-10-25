using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The auto-insights of the report population (P8-06): the lens of the query is not used, the insights compare fixed groupings.</summary>
/// <param name="Query">The checked query.</param>
public sealed record GetReportInsightsQuery(ReportQuery Query) : IQuery<Result<InsightsView>>;
