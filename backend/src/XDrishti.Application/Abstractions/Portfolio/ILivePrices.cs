using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>An instrument the open holdings hold and that Dhan lists in the NSE equity segment: Dhan's security id and the value held (quantity times last price, summed over accounts) - what the active set is ordered by when it must be cut.</summary>
public sealed record HeldSecurity(Guid InstrumentId, string SecurityId, decimal Value);

/// <summary>The portfolio module's side of the live feed (ADR 0024 §3): which instruments the open holdings hold, and writing live prices into them. The Portfolio module owns every write of a holding's price.</summary>
public interface ILivePrices
{
    /// <summary>The instruments of the open holdings of every active Portfolio account (the accounts the quotes poll reads).</summary>
    ValueTask<IReadOnlySet<Guid>> ListHeldInstrumentsAsync(CancellationToken cancellationToken);

    /// <summary>The instruments of the open holdings of every active Portfolio account that Dhan lists in the NSE equity segment (not retired), largest value first. Holdings that are not on the NSE, retired or not mapped are not in the list.</summary>
    ValueTask<IReadOnlyList<HeldSecurity>> ListHeldSecuritiesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the ticks into every <b>open</b> holding of each instrument, across accounts, in one statement: last price, the tick time as the price time, source <c>live</c> (the rules of <c>Holding.ApplyLive</c> and
    /// <c>LivePricePolicy</c>): a holding whose price is as new as the tick or newer is untouched, an indicative tick (before 09:15 India time) does not replace a quote of the same India day, closed holdings are untouched and
    /// the previous close is never written. A row that a holdings sync holds at that moment is left out of the statement and tried once more; if it is still locked it is dropped for this flush and logged. Returns the number of holdings updated.
    /// Executes at once (it is not part of a unit of work).
    /// </summary>
    ValueTask<int> ApplyAsync(IReadOnlyList<LivePrice> prices, CancellationToken cancellationToken);
}
