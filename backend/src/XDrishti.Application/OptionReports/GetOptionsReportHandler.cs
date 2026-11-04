using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.OptionReports;

/// <summary>The <c>xd reports options</c> use case (spec P14-16): the archive summary, the newest expression runs, the pair formations of the last 400 days and the verdict list, by the same handlers as the routes, with the limits block. Read only.</summary>
internal sealed class GetOptionsReportHandler(
    IQueryHandler<GetOptionArchiveSummaryQuery, Result<OptionArchiveSummaryView>> archive,
    IQueryHandler<ListExpressionRunsQuery, Result<ExpressionRunList>> runs,
    IQueryHandler<ListPairFormationsQuery, Result<PairFormationList>> formations,
    IQueryHandler<GetP14VerdictListQuery, Result<P14VerdictList>> verdicts) : IQueryHandler<GetOptionsReportQuery, Result<OptionsReportView>>
{
    public async ValueTask<Result<OptionsReportView>> HandleAsync(GetOptionsReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var verdictList = await verdicts.HandleAsync(new GetP14VerdictListQuery(query.Cut), cancellationToken);
        if (verdictList.IsFailure)
        {
            return Result.Failure<OptionsReportView>(verdictList.Error);
        }

        var archiveView = await archive.HandleAsync(new GetOptionArchiveSummaryQuery(), cancellationToken);
        var runList = await runs.HandleAsync(new ListExpressionRunsQuery(null, null), cancellationToken);
        var formationList = await formations.HandleAsync(new ListPairFormationsQuery(null, null), cancellationToken);
        foreach (var part in new Result[] { archiveView, runList, formationList })
        {
            if (part.IsFailure)
            {
                return Result.Failure<OptionsReportView>(part.Error);
            }
        }

        var data = new OptionsReportData(archiveView.Value, runList.Value, formationList.Value, verdictList.Value, OptionsReportText.Limits);
        return Result.Success(new OptionsReportView(data, OptionsReportText.Render(data)));
    }
}
