using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The headline figures of the whole report population (P8-06); the query model is the one of all report reads.</summary>
/// <param name="Query">The checked query.</param>
public sealed record GetReportSummaryQuery(ReportQuery Query) : IQuery<Result<SummaryView>>;
