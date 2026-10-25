using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The excursions of the groups of the lens (spec P8-10), as counts.</summary>
/// <param name="Query">The checked query.</param>
public sealed record GetReportMaeMfeQuery(ReportQuery Query) : IQuery<Result<ReportMaeMfeView>>;
