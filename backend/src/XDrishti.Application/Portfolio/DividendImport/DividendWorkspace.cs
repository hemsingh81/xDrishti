using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>
/// The steps the dividend import use cases share (ADR 0028): the account must exist and have the Portfolio role on; a file is planned against the account's holdings, the instrument master's names, the remembered aliases and the dividends stored. Every read
/// is one query for the whole file — none per row. Nothing here saves; the use case does, in its unit of work.
/// </summary>
internal sealed class DividendWorkspace(IAccountStore accounts, IHoldingQueries holdings, IInstrumentNames names, IDividendAliases aliases, IDividendStore dividends)
{
    /// <summary>The account with the Portfolio role on (read only).</summary>
    public ValueTask<Result<BrokerAccount>> AccountAsync(Guid accountId, CancellationToken cancellationToken) => PortfolioAccess.AccountWithRoleAsync(accounts, accountId, cancellationToken);

    /// <summary>Plans the file's rows against the account (see <see cref="DividendPlanner"/>).</summary>
    public async ValueTask<DividendPlan> PlanAsync(BrokerAccount account, DividendFile file, CancellationToken cancellationToken)
    {
        var identified = DividendIdentity.Identify(account.Id, file.Items);
        var held = await holdings.ListAsync(new HoldingFilter(account.Id, null, null, null), cancellationToken);
        var shares = await names.ListSharesAsync(cancellationToken);
        var remembered = await aliases.ListAsync(account.Id, cancellationToken);
        var stored = identified.Count == 0 ? new HashSet<string>() : await dividends.StoredIdsAsync(account.Id, [.. identified.Select(i => i.ExternalId)], cancellationToken);
        var manual = identified.Count == 0
            ? []
            : await dividends.ListManualActiveAsync(account.Id, identified.Min(i => i.Row.PayDate), identified.Max(i => i.Row.PayDate), cancellationToken);
        var firstLot = held.SelectMany(h => h.Lots).Min(l => (DateOnly?)l.AcquiredOn);
        return DividendPlanner.Plan(file, identified, new DividendNameIndex(held, shares, remembered), stored, manual, firstLot);
    }
}
