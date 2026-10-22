using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>Builds the report of a succeeded full or robust run from the stored run and result (spec P5-06). Reads only; no engine pass.</summary>
internal sealed class GetLabReportHandler(ILabRunStore runs, ILabResultStore results, LabSettings settings) : IQueryHandler<GetLabReportQuery, Result<LabReportView>>
{
    public async ValueTask<Result<LabReportView>> HandleAsync(GetLabReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var run = await runs.GetAsync(query.Id, cancellationToken);
        if (run is null)
        {
            return Result.Failure<LabReportView>(LabErrors.RunNotFound(query.Id));
        }

        var state = LabRunStates.Code(run.State);
        if (run.State != LabRunState.Succeeded || !LabSettings.RunsFullTest(run.Method))
        {
            return Result.Failure<LabReportView>(LabErrors.NoReport(state, run.Method));
        }

        var verdict = await results.GetAsync(run.Id, cancellationToken);
        return verdict is null
            ? Result.Failure<LabReportView>(LabErrors.NoReport(state, run.Method))
            : Result.Success(LabReportBuilder.Build(LabRunView.Of(run, verdict), verdict, settings.MinFoldsForPass));
    }
}
