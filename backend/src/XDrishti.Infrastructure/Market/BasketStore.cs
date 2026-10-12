using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market;

internal sealed class BasketStore(XDrishtiDbContext db) : IBasketStore
{
    public async ValueTask<Basket?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Baskets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async ValueTask<IReadOnlyList<Basket>> ListActiveAsync(CancellationToken cancellationToken) =>
        await db.Baskets.Where(x => x.ArchivedAt == null).OrderBy(x => x.Position).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);

    public void Add(Basket basket) => db.Baskets.Add(basket);

    // Must be lower(a) = lower(b), exactly as the unique index ux_baskets_active_name_lower; no .NET comparison translates to that.
#pragma warning disable CA1862 // The translation to SQL lower() is the point.
    public async ValueTask<bool> NameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        await db.Baskets.AsNoTracking().AnyAsync(x => x.ArchivedAt == null && x.Id != exceptId && x.Name.ToLower() == name.ToLower(), cancellationToken);
#pragma warning restore CA1862

    public async ValueTask<IReadOnlyList<BasketMember>> ListActiveMembersAsync(Guid basketId, CancellationToken cancellationToken) =>
        await db.BasketMembers.Where(x => x.BasketId == basketId && x.ValidTo == null).OrderBy(x => x.Position).ThenBy(x => x.ValidFrom).ThenBy(x => x.Id).ToListAsync(cancellationToken);

    public void Add(BasketMember member) => db.BasketMembers.Add(member);
}
