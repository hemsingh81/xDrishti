using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>The names that files taught the application belong to ISINs (<c>portfolio.security_aliases</c>, ADR 0029). Saved by <see cref="IPortfolioUnitOfWork"/>; never deleted.</summary>
public interface ISecurityAliases
{
    /// <summary>Every alias of the account, for reading only (not tracked).</summary>
    ValueTask<IReadOnlyList<SecurityAlias>> ListAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Collects the new alias; <see cref="IPortfolioUnitOfWork"/> saves it. A second alias of the same account, name and ISIN makes the save fail.</summary>
    void Add(SecurityAlias alias);
}
