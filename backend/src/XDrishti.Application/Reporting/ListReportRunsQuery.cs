using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The stored runs a report may use, with the default run set marked and whether net figures are possible (P8-06).</summary>
public sealed record ListReportRunsQuery : IQuery<Result<RunsView>>;
