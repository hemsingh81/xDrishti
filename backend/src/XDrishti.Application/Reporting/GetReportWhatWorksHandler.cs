using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The ranking by the credible lower bound with the honest answer "nothing has enough evidence yet" when no cell qualifies (P8-06). That answer is a 200, not an error; more cells than the cap are refused at the first one past it.</summary>
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

        using var report = loaded.Value;
        var answer = report.Population.FindWhatWorks(query.Query.Lens.Count == 0 ? null : query.Query.Lens, ResponseCaps.Cells);
        return answer.IsFailure ? Result.Failure<WhatWorksView>(answer.Error) : Result.Success(ReportViews.WhatWorks(answer.Value, report));
    }
}
