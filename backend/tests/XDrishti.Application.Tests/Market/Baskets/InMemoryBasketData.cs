using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Tests.Market.Baskets;

/// <summary>
/// Baskets and members in memory, saved together with the tracking rows and audit events of <see cref="InMemoryTrackingData"/> as one unit of work:
/// new rows only show up in the store after a save, and a refused save drops them all (as the database does). Entities that were changed in place
/// stay changed, so collision tests use operations that add rows.
/// </summary>
internal sealed class InMemoryBasketData : IBasketStore, IBasketQueries, IMarketUnitOfWork
{
    private readonly List<Basket> _pendingBaskets = [];
    private readonly List<BasketMember> _pendingMembers = [];

    public InMemoryBasketData(InMemoryTrackingData tracking) => Tracking = tracking;

    public InMemoryTrackingData Tracking { get; }

    public List<Basket> Baskets { get; } = [];

    public List<BasketMember> Members { get; } = [];

    /// <summary>The next saves that are refused, each one first running what a concurrent request would have done.</summary>
    public Queue<Action> Collisions { get; } = new();

    public int Saves { get; private set; }

    public (Guid BasketId, DateTimeOffset? Cutoff, DateOnly? AsOf)? LastMembersCall { get; private set; }

    public (Guid BasketId, int Take)? LastHistoryCall { get; private set; }

    public ValueTask<Basket?> FindAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Baskets.FirstOrDefault(b => b.Id == id));

    public ValueTask<IReadOnlyList<Basket>> ListActiveAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Basket>>([.. Baskets.Where(b => !b.IsArchived).OrderBy(b => b.Position).ThenBy(b => b.CreatedAt).ThenBy(b => b.Id)]);

    public void Add(Basket basket) => _pendingBaskets.Add(basket);

    /// <summary>Names the database counts as taken although the handlers' own comparison does not (it folds case differently).</summary>
    public HashSet<string> NamesTakenInDatabase { get; } = [];

    public ValueTask<bool> NameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(NamesTakenInDatabase.Contains(name)
            || Baskets.Any(b => !b.IsArchived && b.Id != exceptId && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)));

    public ValueTask<IReadOnlyList<BasketMember>> ListActiveMembersAsync(Guid basketId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<BasketMember>>([.. Members.Where(m => m.BasketId == basketId && m.IsActive).OrderBy(m => m.Position).ThenBy(m => m.ValidFrom).ThenBy(m => m.Id)]);

    public void Add(BasketMember member) => _pendingMembers.Add(member);

    public ValueTask SaveAsync(CancellationToken cancellationToken)
    {
        Saves++;
        Commit();
        return Tracking.SaveAsync(cancellationToken);
    }

    public async ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        if (Collisions.TryDequeue(out var concurrentRequest))
        {
            Saves++;
            _pendingBaskets.Clear();
            _pendingMembers.Clear();
            Tracking.DiscardPending();
            concurrentRequest();
            return false;
        }

        Saves++;
        Commit();
        return await Tracking.TrySaveAsync(cancellationToken);
    }

    public ValueTask<IReadOnlyList<BasketView>> ListAsync(bool includeArchived, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<BasketView>>([.. Baskets.Where(b => includeArchived || !b.IsArchived).OrderBy(b => b.Position).Select(View)]);

    public ValueTask<BasketView?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Baskets.Where(b => b.Id == id).Select(View).Cast<BasketView?>().FirstOrDefault());

    public ValueTask<BasketMembers?> MembersAsync(Guid basketId, DateTimeOffset? cutoff, DateOnly? asOf, CancellationToken cancellationToken)
    {
        LastMembersCall = (basketId, cutoff, asOf);
        var basket = Baskets.FirstOrDefault(b => b.Id == basketId);
        return ValueTask.FromResult<BasketMembers?>(basket is null ? null : new BasketMembers(basketId, basket.Name, basket.ArchivedAt, asOf, 0, []));
    }

    public ValueTask<BasketHistory?> HistoryAsync(Guid basketId, int take, CancellationToken cancellationToken)
    {
        LastHistoryCall = (basketId, take);
        var basket = Baskets.FirstOrDefault(b => b.Id == basketId);
        return ValueTask.FromResult<BasketHistory?>(basket is null ? null : new BasketHistory(basketId, basket.Name, []));
    }

    public ValueTask<IReadOnlyList<InstrumentBasketView>?> ForInstrumentAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<InstrumentBasketView>?>(
            Tracking.Instruments.Any(i => i.Id == instrumentId)
                ? [.. Members.Where(m => m.InstrumentId == instrumentId && m.IsActive).Select(m => (Member: m, Basket: Baskets.First(b => b.Id == m.BasketId)))
                    .Where(x => !x.Basket.IsArchived).Select(x => new InstrumentBasketView(x.Basket.Id, x.Basket.Name, x.Basket.Colour, x.Basket.Purpose.ToCode(), x.Basket.Position, x.Member.ValidFrom))]
                : null);

    /// <summary>A committed basket.</summary>
    public Basket AddBasket(string name, DateTimeOffset now, BasketPurpose purpose = BasketPurpose.Trading)
    {
        var basket = Basket.Create(Basket.Check(name, null, purpose.ToCode(), null).Value, Baskets.Count(b => !b.IsArchived), now);
        Baskets.Add(basket);
        return basket;
    }

    /// <summary>A committed membership.</summary>
    public BasketMember AddMember(Basket basket, Guid instrumentId, DateTimeOffset now, string? note = null)
    {
        var member = BasketMember.Add(basket.Id, instrumentId, Members.Count(m => m.BasketId == basket.Id && m.IsActive), note, "seed", now);
        Members.Add(member);
        return member;
    }

    public IReadOnlyList<Guid> ActiveInstruments(Basket basket) =>
        [.. Members.Where(m => m.BasketId == basket.Id && m.IsActive).OrderBy(m => m.Position).Select(m => m.InstrumentId)];

    private BasketView View(Basket b) =>
        new(b.Id, b.Name, b.Colour, b.Purpose.ToCode(), b.Description, b.Position, Members.Count(m => m.BasketId == b.Id && m.IsActive), b.CreatedAt, b.ArchivedAt, BasketKinds.Manual, false, null, null);

    private void Commit()
    {
        Baskets.AddRange(_pendingBaskets);
        Members.AddRange(_pendingMembers);
        _pendingBaskets.Clear();
        _pendingMembers.Clear();
    }
}
