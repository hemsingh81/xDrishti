using XDrishti.Domain.Baskets.Derived;

namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>One instrument with its daily bars (adjusted for the applied corporate actions) and the flags the engine reads. <c>Bars.Symbol</c> is the canonical symbol.</summary>
public sealed record UniverseInstrument(Guid InstrumentId, InstrumentBars Bars);

/// <summary>
/// Reads our own daily bars for the universe of a date (BK-03): every share that has a daily bar in the window, retired ones included, never narrowed to today's tracked set. One query for the whole window, shared by every basket of a run. No network.
/// </summary>
public interface IDerivedUniverseReader
{
    /// <summary>The bars dated <paramref name="from"/> to <paramref name="to"/>; <c>Bars.Benchmark</c> is the Nifty 50 over the same window (null when it has none).</summary>
    ValueTask<IReadOnlyList<UniverseInstrument>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
