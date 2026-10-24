using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The groups of a report for the lens of the query (P8-06).</summary>
/// <param name="Query">The checked query.</param>
public sealed record GetReportGroupsQuery(ReportQuery Query) : IQuery<Result<GroupsView>>;
