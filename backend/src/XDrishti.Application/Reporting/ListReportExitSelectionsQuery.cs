using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The stored out-of-sample exit choices (spec P8-10 part b).</summary>
public sealed record ListReportExitSelectionsQuery : IQuery<Result<ReportExitSelectionsView>>;
