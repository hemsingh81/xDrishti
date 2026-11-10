using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The simulate-everything jobs a training report can read, newest first, a page at a time (AU-14).</summary>
/// <param name="Limit">1 to 50 (20 when empty).</param>
/// <param name="Cursor">The cursor the previous page returned.</param>
public sealed record ListSimReportJobsQuery(string? Limit, string? Cursor) : IQuery<Result<SimReportJobsView>>;
