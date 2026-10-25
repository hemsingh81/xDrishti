using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>Groups the population by the lens (P8-06). A lens of regime or account, or one that would add a signal once per label, is refused by the engine with its code; one with more groups than the cap is refused at the first group past it.</summary>
internal sealed class GetReportGroupsHandler(ReportLoader loader) : IQueryHandler<GetReportGroupsQuery, Result<GroupsView>>
{
    public async ValueTask<Result<GroupsView>> HandleAsync(GetReportGroupsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var loaded = await loader.LoadAsync(query.Query, "groups", cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<GroupsView>(loaded.Error);
        }

        using var report = loaded.Value;
        var grouping = report.Population.GroupBy(query.Query.Lens, ResponseCaps.Groups);
        return grouping.IsFailure ? Result.Failure<GroupsView>(grouping.Error) : Result.Success(ReportViews.Groups(grouping.Value, report));
    }
}
