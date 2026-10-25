using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>A page of the closed trades the report counts, in the order of the exits (P8-06); the page size and cursor are those of the query.</summary>
/// <param name="Query">The checked query.</param>
public sealed record PageReportTradesQuery(ReportQuery Query) : IQuery<Result<TradesView>>;
