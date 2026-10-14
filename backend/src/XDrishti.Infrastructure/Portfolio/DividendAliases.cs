using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>The remembered scrip names of dividend files (<c>portfolio.dividend_aliases</c>, ADR 0028). Reads are not tracked; new aliases and retargets are saved by the portfolio unit of work. Nothing is deleted.</summary>
internal sealed class DividendAliases(XDrishtiDbContext db) : IDividendAliases
{
    public async ValueTask<IReadOnlyList<DividendAlias>> ListAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.DividendAliases.AsNoTracking().Where(x => x.AccountId == accountId).OrderBy(x => x.NameKey).ToListAsync(cancellationToken);

    public async ValueTask<DividendAlias?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.DividendAliases.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async ValueTask<IReadOnlyList<DividendAlias>> FindByKeysAsync(Guid accountId, IReadOnlyCollection<string> nameKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nameKeys);
        if (nameKeys.Count == 0)
        {
            return [];
        }

        var keys = nameKeys as string[] ?? [.. nameKeys];
        return await db.DividendAliases.Where(x => x.AccountId == accountId && keys.Contains(x.NameKey)).ToListAsync(cancellationToken);
    }

    public void Add(DividendAlias alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        db.DividendAliases.Add(alias);
    }
}
