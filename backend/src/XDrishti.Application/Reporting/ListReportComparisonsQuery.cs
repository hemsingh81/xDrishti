using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The stored exit comparisons (spec P8-10 part b).</summary>
public sealed record ListReportComparisonsQuery : IQuery<Result<ReportComparisonsView>>;
