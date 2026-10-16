namespace XDrishti.Application.Abstractions.Live;

/// <summary>
/// The latest live price of the instruments that are not holdings (indices, alert-only instruments): <c>market.live_quotes</c>, one row per instrument, unlogged, emptied at the start of each feed window (ADR 0024 §3).
/// No history is kept.
/// </summary>
public interface ILiveQuotes
{
    /// <summary>Inserts or updates one row per instrument (the newest tick wins: a tick not newer than the stored one changes nothing). Executes at once. Returns the number of rows written.</summary>
    ValueTask<int> UpsertAsync(IReadOnlyList<LivePrice> prices, CancellationToken cancellationToken);

    /// <summary>Empties the table - the start of each window. The runtime role has no DELETE; this calls a database function that runs with its owner's rights.</summary>
    ValueTask ClearAsync(CancellationToken cancellationToken);
}
