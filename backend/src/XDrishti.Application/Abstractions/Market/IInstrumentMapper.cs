using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Market;

/// <summary>The instrument of the master list a broker row was mapped to.</summary>
public sealed record InstrumentRef(Guid Id, string CanonicalSymbol, string Name);

/// <summary>Maps broker holdings to the instrument master: by ISIN first, then by the broker's security id and exchange segment.</summary>
public interface IInstrumentMapper
{
    /// <summary>The instrument of every row that could be mapped, by <see cref="BrokerHolding.Key"/>. Rows without a match are absent.</summary>
    ValueTask<IReadOnlyDictionary<string, InstrumentRef>> MapAsync(IReadOnlyCollection<BrokerHolding> rows, CancellationToken cancellationToken);
}
