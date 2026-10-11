using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Accounts;

internal sealed class RiskProfileStore(XDrishtiDbContext db) : IRiskProfileStore
{
    public async ValueTask<IReadOnlyList<RiskProfile>> ListAsync(CancellationToken cancellationToken) =>
        await db.RiskProfiles.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken);

    public async ValueTask<RiskProfile?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.RiskProfiles.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async ValueTask<string?> FindNameAsync(Guid id, CancellationToken cancellationToken) =>
        await db.RiskProfiles.AsNoTracking().Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken);

    public async ValueTask<RiskProfile?> FindForDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        // FOR UPDATE conflicts with the key-share lock a referencing write takes: a concurrent trading update either committed before
        // (and is seen by the usage count that follows) or waits and then fails its foreign-key check.
        var rows = await db.RiskProfiles
            .FromSql($"SELECT *, xmin FROM accounts.risk_profiles WHERE id = {id} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<bool> NameExistsAsync(string name, Guid? exceptProfileId, CancellationToken cancellationToken)
    {
        return await db.RiskProfiles.AsNoTracking().AnyAsync(
            x => EF.Functions.ILike(x.Name, LikePattern.Escape(name), LikePattern.EscapeCharacter) && (exceptProfileId == null || x.Id != exceptProfileId), cancellationToken);
    }

    public async ValueTask<IReadOnlyDictionary<Guid, int>> CountUsageAsync(CancellationToken cancellationToken) =>
        await db.BrokerAccounts.AsNoTracking()
            .Where(x => x.ArchivedAt == null && x.Trading.RiskProfileId != null)
            .GroupBy(x => x.Trading.RiskProfileId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

    public void Add(RiskProfile profile) => db.RiskProfiles.Add(profile);

    public void Remove(RiskProfile profile) => db.RiskProfiles.Remove(profile);
}
