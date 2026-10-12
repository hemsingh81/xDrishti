namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>Finds the master-list instruments the CSV files belong to (ADR 0016 §5): shares and ETFs that are not retired.</summary>
public interface IListedInstruments
{
    /// <summary>For each canonical symbol that matches, the ids of the matching instruments (more than one means the listing is ambiguous). Symbols without a match are absent.</summary>
    ValueTask<IReadOnlyDictionary<string, IReadOnlyList<Guid>>> FindAsync(IReadOnlyCollection<string> canonicalSymbols, CancellationToken cancellationToken);
}
