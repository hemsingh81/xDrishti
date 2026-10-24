using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The R distributions of the lens (P8-06).</summary>
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

        var distribution = loaded.Value.Population.Distribution(query.Query.Lens, BinWidthR);
        if (distribution.IsFailure)
        {
            return Result.Failure<DistributionView>(distribution.Error);
        }

        return ResponseCaps.Exceeded(distribution.Value.Groups.Count, ResponseCaps.DistributionGroups, "groups") is { } tooMany
            ? Result.Failure<DistributionView>(tooMany)
            : Result.Success(ReportViews.Distribution(distribution.Value, loaded.Value));
    }
}
