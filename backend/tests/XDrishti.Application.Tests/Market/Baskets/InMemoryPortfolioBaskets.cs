using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Tests.Market.Baskets;

/// <summary>Portfolio buckets as system baskets, in memory: a bucket holds the instruments it was given, in that order.</summary>
internal sealed class InMemoryPortfolioBaskets : IPortfolioBasketSource
{
    private readonly List<(Guid Id, string Name, string? AccountName, Guid[] Instruments)> _buckets = [];

    /// <summary>Adds a bucket (as the default one of an account when <paramref name="accountName"/> is given) holding these instruments.</summary>
    public Guid Add(string name, string? accountName, params Guid[] instruments)
    {
        var id = Guid.NewGuid();
        _buckets.Add((id, name, accountName, instruments));
        return id;
    }

    public ValueTask<IReadOnlyList<BasketView>> ListAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<BasketView>>(
            [.. _buckets.Select((b, position) => new BasketView(
                b.Id, b.Name, Basket.DefaultColour, "long_term", null, position, b.Instruments.Length, DateTimeOffset.UnixEpoch, null, BasketKinds.Portfolio, true,
                b.AccountName is null ? null : Guid.Empty, b.AccountName))]);

    public ValueTask<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(_buckets.Any(b => b.Id == id));

    public ValueTask<BasketMembers?> MembersAsync(Guid id, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_buckets.Where(b => b.Id == id).Select(b => new BasketMembers(b.Id, b.Name, null, null, b.Instruments.Length, [])).Cast<BasketMembers?>().FirstOrDefault());

    public ValueTask<PortfolioBasketInstruments?> InstrumentsAsync(Guid id, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_buckets.Where(b => b.Id == id).Select(b => new PortfolioBasketInstruments(b.Id, b.Name, b.Instruments)).Cast<PortfolioBasketInstruments?>().FirstOrDefault());
}
