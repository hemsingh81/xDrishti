using XDrishti.Application.Live.Push;

namespace XDrishti.Application.Abstractions.Live;

/// <summary>Turns a screen's <see cref="LiveScope"/> into instruments: the instruments of the open holdings in the scope (as <c>GET /api/holdings/summary</c> reads them) and of the listed holdings, largest value first, each once. Holdings with no instrument are left out.</summary>
public interface ILiveScopeResolver
{
    ValueTask<IReadOnlyList<Guid>> ResolveAsync(Guid? accountId, Guid? bucketId, IReadOnlyCollection<Guid> holdingIds, CancellationToken cancellationToken);
}
