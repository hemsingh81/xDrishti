using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>What the matching of names needs, read once for a file: the account's holdings, the master's shares, the remembered aliases (ADR 0028) and the names that files taught (ADR 0029).</summary>
internal sealed record ReportData(IReadOnlyList<HoldingData> Holdings, IReadOnlyList<ShareName> Shares, IReadOnlyList<DividendAlias> Aliases, IReadOnlyList<SecurityAlias> Learned)
{
    public SecurityIndex Index() => new(Holdings, Shares, Aliases, Learned);
}

/// <summary>
/// The steps the report use cases share (ADR 0029): the account must exist and have the Portfolio role on; a file's bytes become a sheet; the data names are matched against is read once per file — none per row. Nothing here saves; the use case does, in its unit of work.
/// </summary>
internal sealed class ReportWorkspace(IAccountStore accounts, IHoldingQueries holdings, IInstrumentNames names, IDividendAliases aliases, ISecurityAliases learned, ISpreadsheetReader spreadsheets)
{
    /// <summary>The account with the Portfolio role on (read only).</summary>
    public ValueTask<Result<BrokerAccount>> AccountAsync(Guid accountId, CancellationToken cancellationToken) => PortfolioAccess.AccountWithRoleAsync(accounts, accountId, cancellationToken);

    /// <summary>The file as rows of cells, whatever its format.</summary>
    public ValueTask<Result<Sheet>> LoadAsync(TradeFileContent content, CancellationToken cancellationToken) => SheetLoader.LoadAsync(content, spreadsheets, cancellationToken);

    public async ValueTask<ReportData> ReadAsync(BrokerAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var held = await holdings.ListAsync(new HoldingFilter(account.Id, null, null, null), cancellationToken);
        var shares = await names.ListSharesAsync(cancellationToken);
        var remembered = await aliases.ListAsync(account.Id, cancellationToken);
        var taught = await learned.ListAsync(account.Id, cancellationToken);
        return new ReportData(held, shares, remembered, taught);
    }
}
