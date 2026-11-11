using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>A share of the master as the list baskets see it: the flag fields, the symbol (without the exchange prefix) and the lot size of its nearest unexpired stock future (null when it has none).</summary>
public sealed record MasterShare(FlagRow Flags, string Symbol, int? MasterLot);

/// <summary>Reads the stored instrument master for the list baskets (BK-05): the shares, retired ones included. No network.</summary>
public interface IListMasterReader
{
    ValueTask<IReadOnlyList<MasterShare>> ReadSharesAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>The id of each given canonical symbol (<c>NSE:NIFTY</c>) that names exactly one live index instrument of the master; a symbol with none, or more than one, is absent (BK-05b).</summary>
    ValueTask<IReadOnlyDictionary<string, Guid>> ReadIndexIdsAsync(IReadOnlyCollection<string> canonicalSymbols, CancellationToken cancellationToken);
}
