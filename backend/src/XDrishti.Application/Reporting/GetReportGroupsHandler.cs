using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>Groups the population by the lens (P8-06). A lens of regime or account, or one that would add a signal once per label, is refused by the engine with its code.</summary>
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

        var grouping = loaded.Value.Population.GroupBy(query.Query.Lens);
        if (grouping.IsFailure)
        {
            return Result.Failure<GroupsView>(grouping.Error);
        }

        return ResponseCaps.Exceeded(grouping.Value.Groups.Count, ResponseCaps.Groups, "groups") is { } tooMany
            ? Result.Failure<GroupsView>(tooMany)
            : Result.Success(ReportViews.Groups(grouping.Value, loaded.Value));
    }
}
