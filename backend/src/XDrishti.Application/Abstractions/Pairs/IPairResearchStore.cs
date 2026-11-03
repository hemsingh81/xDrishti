namespace XDrishti.Application.Abstractions.Pairs;

/// <summary><c>research.pair_formations</c> and <c>research.pair_trades</c> behind a port (P14-13): insert-only, content-addressed, nothing is updated or deleted.</summary>
public interface IPairResearchStore
{
    /// <summary>Writes the formation; false when the id is stored already.</summary>
    ValueTask<bool> AppendFormationAsync(PairFormationRow row, CancellationToken cancellationToken);

    /// <summary>Writes the trades; the number written (rows already stored are skipped).</summary>
    ValueTask<int> AppendTradesAsync(IReadOnlyList<PairTradeRow> rows, CancellationToken cancellationToken);

    /// <summary>The stored trades that carry the label <paramref name="cfgLabel"/> (the settings they were run under), oldest first; filtered in the database.</summary>
    ValueTask<IReadOnlyList<PairStoredTrade>> ReadTradesAsync(string cfgLabel, CancellationToken cancellationToken);

    /// <summary>How many stored trades dated before <paramref name="before"/> do not carry the label <paramref name="cfgLabel"/>.</summary>
    ValueTask<int> CountOtherTradesAsync(string cfgLabel, DateOnly before, CancellationToken cancellationToken);

    /// <summary>The stored formations of the family, oldest first.</summary>
    ValueTask<IReadOnlyList<StoredFormation>> ReadFormationsAsync(string family, CancellationToken cancellationToken);
}
