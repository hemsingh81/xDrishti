using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>The pivot of two dimensions by one figure (P8-06): every cell is a value, <c>insufficient</c> (masked, with its count) or <c>empty</c>; a grid over the cap is refused before its cells are laid out.</summary>
internal sealed class GetReportPivotHandler(ReportLoader loader) : IQueryHandler<GetReportPivotQuery, Result<PivotView>>
{
    public async ValueTask<Result<PivotView>> HandleAsync(GetReportPivotQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Query.Lens.Count != 2)
        {
            return Result.Failure<PivotView>(ReportErrors.Invalid("A pivot needs a lens of exactly two dimensions, rows first."));
        }

        var loaded = await loader.LoadAsync(query.Query, "pivot", cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<PivotView>(loaded.Error);
        }

        using var report = loaded.Value;
        var pivot = report.Population.Pivot(query.Query.Lens[0], query.Query.Lens[1], query.Query.Metric, ResponseCaps.PivotCells);
        return pivot.IsFailure ? Result.Failure<PivotView>(pivot.Error) : Result.Success(ReportViews.Pivot(pivot.Value, report));
    }
}
