using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>The rule the file imports (tradebook, dividend list) share about the account they import into.</summary>
internal static class PortfolioAccess
{
    /// <summary>The account (read only) when it exists (<c>portfolio.not_found</c> otherwise) and has the Portfolio role on (<c>portfolio.role_off</c> otherwise).</summary>
    public static async ValueTask<Result<BrokerAccount>> AccountWithRoleAsync(IAccountStore accounts, Guid accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var account = await accounts.FindReadOnlyAsync(accountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<BrokerAccount>(PortfolioErrors.AccountNotFound);
        }

        return account.Portfolio.Enabled ? Result.Success(account) : Result.Failure<BrokerAccount>(PortfolioErrors.RoleOff);
    }
}
