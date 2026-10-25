using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The MAE and MFE counts of the lens (spec P8-10); more groups than the cap are refused at the first one past it.</summary>
internal sealed class GetReportMaeMfeHandler(ReportLoader loader) : IQueryHandler<GetReportMaeMfeQuery, Result<ReportMaeMfeView>>
{
    public async ValueTask<Result<ReportMaeMfeView>> HandleAsync(GetReportMaeMfeQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var loaded = await loader.LoadAsync(query.Query, "mae_mfe", cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<ReportMaeMfeView>(loaded.Error);
        }

        using var report = loaded.Value;
        var answer = report.Population.MaeMfe(query.Query.Lens, ResponseCaps.DistributionGroups);
        return answer.IsFailure ? Result.Failure<ReportMaeMfeView>(answer.Error) : Result.Success(ReportViews.MaeMfe(answer.Value, report));
    }
}
