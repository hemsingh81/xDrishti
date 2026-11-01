using XDrishti.Domain.Options;

namespace XDrishti.Application.Abstractions.OptionQuality;

/// <summary>The reference data the checks need (P14-01 tables and the master list): symbols, strike step, lot, epochs. Reads no archive row.</summary>
public interface IOptionReference
{
    /// <summary>The canonical symbols of the instruments, by id (an unknown id is absent).</summary>
    ValueTask<IReadOnlyDictionary<Guid, string>> SymbolsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The instrument of a canonical symbol; null when unknown.</summary>
    ValueTask<Guid?> FindUnderlyingAsync(string canonicalSymbol, CancellationToken cancellationToken);

    /// <summary>The smallest gap in paise between two strikes of the underlying's contracts of its nearest expiry on or after the date; null when unknown.</summary>
    ValueTask<int?> StrikeStepAsync(Guid underlyingId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>The lot-size rows of the underlying.</summary>
    ValueTask<IReadOnlyList<LotSizeEntry>> LotSizesAsync(Guid underlyingId, CancellationToken cancellationToken);

    /// <summary>All rule epochs.</summary>
    ValueTask<IReadOnlyList<MarketEpoch>> EpochsAsync(CancellationToken cancellationToken);
}
