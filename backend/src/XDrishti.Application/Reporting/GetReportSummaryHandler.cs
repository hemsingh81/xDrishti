using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The KPIs of the whole population: the engine's single group, or every figure <c>empty</c> when the report holds no trade (P8-06).</summary>
internal sealed class GetReportSummaryHandler(ReportLoader loader) : IQueryHandler<GetReportSummaryQuery, Result<SummaryView>>
{
    public async ValueTask<Result<SummaryView>> HandleAsync(GetReportSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var loaded = await loader.LoadAsync(query.Query, "summary", cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<SummaryView>(loaded.Error);
        }

        var report = loaded.Value;
        var grouping = report.Population.GroupBy([]);
        if (grouping.IsFailure)
        {
            return Result.Failure<SummaryView>(grouping.Error);
        }

        var all = grouping.Value.Groups.Count > 0 ? ReportViews.Group(grouping.Value.Groups[0]) : ReportViews.EmptyGroup(report.Population.Evidence.Population);
        return Result.Success(new SummaryView(all, ReportViews.Evidence(grouping.Value.Evidence, report)));
    }
}
