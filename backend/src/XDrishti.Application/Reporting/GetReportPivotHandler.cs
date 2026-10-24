using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>The pivot of two dimensions by one figure (P8-06): every cell is a value, <c>insufficient</c> (masked, with its count) or <c>empty</c>.</summary>
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

        var pivot = loaded.Value.Population.Pivot(query.Query.Lens[0], query.Query.Lens[1], query.Query.Metric);
        if (pivot.IsFailure)
        {
            return Result.Failure<PivotView>(pivot.Error);
        }

        return ResponseCaps.Exceeded(pivot.Value.Cells.Count, ResponseCaps.PivotCells, "cells") is { } tooMany
            ? Result.Failure<PivotView>(tooMany)
            : Result.Success(ReportViews.Pivot(pivot.Value, loaded.Value));
    }
}
