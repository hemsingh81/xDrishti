using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The distribution of R of the groups of the lens (P8-06), in bins of half an R.</summary>
/// <param name="Query">The checked query.</param>
public sealed record GetReportDistributionQuery(ReportQuery Query) : IQuery<Result<DistributionView>>;
