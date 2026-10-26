using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The P8 phase check (spec P8-17). It takes no parameter: the owner's stopwatch, the interaction result and the evidence files are never accepted from a request.</summary>
public sealed record EvaluateReportsPhaseCheckQuery : IQuery<Result<ReportsPhaseCheckView>>;
