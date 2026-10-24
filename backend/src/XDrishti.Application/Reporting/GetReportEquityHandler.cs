using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The curves of the lens (P8-06). A lens with more groups than <see cref="EquityView.MaxSeries"/> is refused, not cut: no curve is silently left out.</summary>
internal sealed class GetReportEquityHandler(ReportLoader loader) : IQueryHandler<GetReportEquityQuery, Result<EquityView>>
{
    public async ValueTask<Result<EquityView>> HandleAsync(GetReportEquityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var loaded = await loader.LoadAsync(query.Query, "equity", cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<EquityView>(loaded.Error);
        }

        var equity = loaded.Value.Population.Equity(query.Query.Lens);
        if (equity.IsFailure)
        {
            return Result.Failure<EquityView>(equity.Error);
        }

        return ResponseCaps.Exceeded(equity.Value.Series.Count, EquityView.MaxSeries, "groups") is { } tooMany
            ? Result.Failure<EquityView>(tooMany)
            : Result.Success(ReportViews.Equity(equity.Value, loaded.Value));
    }
}
