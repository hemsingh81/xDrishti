using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The ranking by the credible lower bound with the honest answer "nothing has enough evidence yet" when no cell qualifies (P8-06). That answer is a 200, not an error.</summary>
internal sealed class GetReportWhatWorksHandler(ReportLoader loader) : IQueryHandler<GetReportWhatWorksQuery, Result<WhatWorksView>>
{
    public async ValueTask<Result<WhatWorksView>> HandleAsync(GetReportWhatWorksQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var loaded = await loader.LoadAsync(query.Query, "what-works", cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<WhatWorksView>(loaded.Error);
        }

        var answer = loaded.Value.Population.FindWhatWorks(query.Query.Lens.Count == 0 ? null : query.Query.Lens);
        if (answer.IsFailure)
        {
            return Result.Failure<WhatWorksView>(answer.Error);
        }

        return ResponseCaps.Exceeded(answer.Value.CellsLookedAt, ResponseCaps.Cells, "cells") is { } tooMany
            ? Result.Failure<WhatWorksView>(tooMany)
            : Result.Success(ReportViews.WhatWorks(answer.Value, loaded.Value));
    }
}
