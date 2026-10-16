using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>What the owner decided scrip names of dividend files mean for an account (<c>portfolio.dividend_aliases</c>, ADR 0028). Saved by <see cref="IPortfolioUnitOfWork"/>; never deleted.</summary>
public interface IDividendAliases
{
    /// <summary>Every alias of the account, for reading only (not tracked).</summary>
    ValueTask<IReadOnlyList<DividendAlias>> ListAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>One alias, tracked so that pointing it elsewhere can be saved; null when it does not exist.</summary>
    ValueTask<DividendAlias?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The aliases of the account for these name keys in one query, tracked so that pointing them elsewhere can be saved.</summary>
    ValueTask<IReadOnlyList<DividendAlias>> FindByKeysAsync(Guid accountId, IReadOnlyCollection<string> nameKeys, CancellationToken cancellationToken);

    /// <summary>Collects the new alias; <see cref="IPortfolioUnitOfWork"/> saves it. A second alias of the same account and name makes the save fail.</summary>
    void Add(DividendAlias alias);
}
