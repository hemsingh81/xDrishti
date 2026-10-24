using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The equity and drawdown curves of the groups of the lens (P8-06).</summary>
/// <param name="Query">The checked query.</param>
public sealed record GetReportEquityQuery(ReportQuery Query) : IQuery<Result<EquityView>>;
