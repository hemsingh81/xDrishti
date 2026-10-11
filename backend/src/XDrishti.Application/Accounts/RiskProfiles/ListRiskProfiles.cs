using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Accounts.RiskProfiles;

/// <summary>All shared risk profiles with the number of accounts that use each.</summary>
public sealed record ListRiskProfilesQuery : IQuery<IReadOnlyList<RiskProfileView>>;

internal sealed class ListRiskProfilesHandler(IRiskProfileStore profiles) : IQueryHandler<ListRiskProfilesQuery, IReadOnlyList<RiskProfileView>>
{
    public async ValueTask<IReadOnlyList<RiskProfileView>> HandleAsync(ListRiskProfilesQuery query, CancellationToken cancellationToken)
    {
        var list = await profiles.ListAsync(cancellationToken);
        var usage = await profiles.CountUsageAsync(cancellationToken);
        return [.. list.Select(p => RiskProfileMapping.ToView(p, usage.GetValueOrDefault(p.Id)))];
    }
}
