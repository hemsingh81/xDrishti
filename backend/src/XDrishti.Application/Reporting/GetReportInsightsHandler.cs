using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The eight fixed-template insights (P8-06); the engine writes a figure only when every group compared has the minimum sample.</summary>
internal sealed class GetReportInsightsHandler(ReportLoader loader) : IQueryHandler<GetReportInsightsQuery, Result<InsightsView>>
{
    public async ValueTask<Result<InsightsView>> HandleAsync(GetReportInsightsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var loaded = await loader.LoadAsync(query.Query, "insights", cancellationToken);
        return loaded.IsFailure ? Result.Failure<InsightsView>(loaded.Error) : Result.Success(ReportViews.Insights(loaded.Value.Population.Insights(), loaded.Value));
    }
}
