using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Accounts;

internal sealed class BucketStore(XDrishtiDbContext db) : IBucketStore
{
    public async ValueTask<IReadOnlyList<PortfolioBucket>> ListAsync(CancellationToken cancellationToken) =>
        await db.PortfolioBuckets.AsNoTracking().Where(x => x.ArchivedAt == null).OrderBy(x => x.CreatedAt).ThenBy(x => x.Name).ToListAsync(cancellationToken);

    public async ValueTask<PortfolioBucket?> FindByNameAsync(string name, CancellationToken cancellationToken) =>
        await db.PortfolioBuckets.AsNoTracking().FirstOrDefaultAsync(x => EF.Functions.ILike(x.Name, LikePattern.Escape(name), LikePattern.EscapeCharacter), cancellationToken);

    public async ValueTask<bool> NameTakenAsync(string name, Guid? exceptBucketId, CancellationToken cancellationToken) =>
        await db.PortfolioBuckets.AsNoTracking().AnyAsync(
            x => x.Id != exceptBucketId && EF.Functions.ILike(x.Name, LikePattern.Escape(name), LikePattern.EscapeCharacter), cancellationToken);

    public async ValueTask<IReadOnlyList<string>> NamesStartingWithAsync(string prefix, Guid? exceptBucketId, CancellationToken cancellationToken) =>
        await db.PortfolioBuckets.AsNoTracking()
            .Where(x => x.Id != exceptBucketId && EF.Functions.ILike(x.Name, LikePattern.Escape(prefix) + "%", LikePattern.EscapeCharacter))
            .Select(x => x.Name)
            .ToListAsync(cancellationToken);

    public async ValueTask<PortfolioBucket?> FindDefaultForAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.PortfolioBuckets.FirstOrDefaultAsync(x => x.DefaultForAccountId == accountId, cancellationToken);

    public async ValueTask<IReadOnlyDictionary<Guid, string>> DefaultNamesAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken) =>
        await db.PortfolioBuckets.AsNoTracking()
            .Where(x => x.DefaultForAccountId != null && accountIds.Contains(x.DefaultForAccountId.Value))
            .ToDictionaryAsync(x => x.DefaultForAccountId!.Value, x => x.Name, cancellationToken);

    public async ValueTask<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> bucketIds, CancellationToken cancellationToken) =>
        await db.PortfolioBuckets.AsNoTracking().Where(x => bucketIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

    public void Add(PortfolioBucket bucket) => db.PortfolioBuckets.Add(bucket);
}
