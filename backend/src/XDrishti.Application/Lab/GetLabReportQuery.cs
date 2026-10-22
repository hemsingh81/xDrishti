using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>The report of one finished full or robust Lab run (spec P5-06); <c>lab.not_found</c> or <c>lab.no_report</c> when there is none.</summary>
public sealed record GetLabReportQuery(Guid Id) : IQuery<Result<LabReportView>>;
