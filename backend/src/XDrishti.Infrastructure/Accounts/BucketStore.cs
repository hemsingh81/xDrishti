using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Accounts;

internal sealed class BucketStore(XDrishtiDbContext db) : IBucketStore
{
    public async ValueTask<IReadOnlyList<PortfolioBucket>> ListAsync(CancellationToken cancellationToken) =>
        await db.PortfolioBuckets.AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.Name).ToListAsync(cancellationToken);

    public async ValueTask<string?> FindNameAsync(string name, CancellationToken cancellationToken)
    {
        return await db.PortfolioBuckets.AsNoTracking().Where(x => EF.Functions.ILike(x.Name, LikePattern.Escape(name))).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken);
    }

    public void Add(PortfolioBucket bucket) => db.PortfolioBuckets.Add(bucket);
}
