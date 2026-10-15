using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>The names that files taught the application belong to ISINs (<c>portfolio.security_aliases</c>, ADR 0029). Reads are not tracked; new aliases are saved by the portfolio unit of work. Nothing is updated or deleted.</summary>
internal sealed class SecurityAliases(XDrishtiDbContext db) : ISecurityAliases
{
    public async ValueTask<IReadOnlyList<SecurityAlias>> ListAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.SecurityAliases.AsNoTracking().Where(x => x.AccountId == accountId).OrderBy(x => x.NameKey).ThenBy(x => x.Isin).ToListAsync(cancellationToken);

    public void Add(SecurityAlias alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        db.SecurityAliases.Add(alias);
    }
}
