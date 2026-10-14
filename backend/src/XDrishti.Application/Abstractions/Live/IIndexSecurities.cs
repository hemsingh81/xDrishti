namespace XDrishti.Application.Abstractions.Live;

/// <summary>An index of the master list as the feed needs it: its id, the label it was asked for by, and Dhan's security id in the index segment (<see cref="FeedSegments.Index"/>).</summary>
public sealed record IndexSecurity(string Label, Guid InstrumentId, string SecurityId);

/// <summary>An index asked for by name: <see cref="Label"/> is how it is called in logs, the master list is searched by canonical symbol, then by display name (ignoring case).</summary>
public sealed record IndexName(string Label, string CanonicalSymbol, string DisplayName);

/// <summary>The market module's side of the live feed: indices of the master list with a Dhan security id (ADR 0024 §2). Retired instruments and indices without a Dhan id are not returned.</summary>
public interface IIndexSecurities
{
    /// <summary>The indices that exist; one that is not in the master list is simply absent from the answer.</summary>
    ValueTask<IReadOnlyList<IndexSecurity>> FindAsync(IReadOnlyList<IndexName> names, CancellationToken cancellationToken);

    /// <summary>The index with this instrument id (for example the portfolio's benchmark); null when it is not an index, is retired or has no Dhan id.</summary>
    ValueTask<IndexSecurity?> FindByIdAsync(Guid instrumentId, CancellationToken cancellationToken);
}
