using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The R distributions of the lens (P8-06); more groups than the cap are refused at the first one past it.</summary>
internal sealed class GetReportDistributionHandler(ReportLoader loader) : IQueryHandler<GetReportDistributionQuery, Result<DistributionView>>
{
    /// <summary>The width of a bin in R.</summary>
    public const double BinWidthR = 0.5;

    public async ValueTask<Result<DistributionView>> HandleAsync(GetReportDistributionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var loaded = await loader.LoadAsync(query.Query, "distribution", cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<DistributionView>(loaded.Error);
        }

        using var report = loaded.Value;
        var distribution = report.Population.Distribution(query.Query.Lens, BinWidthR, ResponseCaps.DistributionGroups);
        return distribution.IsFailure ? Result.Failure<DistributionView>(distribution.Error) : Result.Success(ReportViews.Distribution(distribution.Value, report));
    }
}
