using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.Groups;

/// <summary>The master's sectors and the live baskets of the instruments of some holdings (only what the grouping asked for is read, by <see cref="ClassificationLoader"/>).</summary>
internal sealed record Classification(IReadOnlyDictionary<Guid, string> Sectors, IReadOnlyDictionary<Guid, IReadOnlyList<BasketRef>> Baskets)
{
    public static Classification None { get; } = new(new Dictionary<Guid, string>(), new Dictionary<Guid, IReadOnlyList<BasketRef>>());
}
