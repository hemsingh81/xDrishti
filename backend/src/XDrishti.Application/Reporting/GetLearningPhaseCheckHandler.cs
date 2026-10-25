using System.Diagnostics;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Reporting;

/// <summary>Wraps the existing P6 phase check as a read (P8-04, ADR 0103): replay seconds are always null, nothing is written.</summary>
internal sealed class GetLearningPhaseCheckHandler(IQueryHandler<EvaluatePhaseCheckQuery, Result<PhaseCheckView>> check)
    : IQueryHandler<GetLearningPhaseCheckQuery, Result<LearningPhaseCheckView>>
{
    public async ValueTask<Result<LearningPhaseCheckView>> HandleAsync(GetLearningPhaseCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var started = Stopwatch.GetTimestamp();
        var result = await check.HandleAsync(new EvaluatePhaseCheckQuery(null), cancellationToken);
        if (result.IsFailure)
        {
            return Result.Failure<LearningPhaseCheckView>(result.Error);
        }

        var report = result.Value.Report;
        return Result.Success(new LearningPhaseCheckView(
            [.. report.Criteria.Select(c => new LearningPhaseCriterionView(c.Number, c.Name, PhaseCheckReport.Label(c.Verdict), c.Detail, c.Caveat))],
            report.Limits,
            result.Value.Text,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));
    }
}
