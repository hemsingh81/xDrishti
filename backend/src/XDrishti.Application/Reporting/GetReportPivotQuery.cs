using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>A pivot of the two dimensions of the lens by the figure of the query (P8-06); a lens that is not exactly two dimensions is <c>reports.invalid</c>.</summary>
/// <param name="Query">The checked query.</param>
public sealed record GetReportPivotQuery(ReportQuery Query) : IQuery<Result<PivotView>>;
