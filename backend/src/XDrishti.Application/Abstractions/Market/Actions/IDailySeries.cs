using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Abstractions.Market.Actions;

/// <summary>An instrument that has day bars.</summary>
public sealed record SeriesInstrument(Guid Id, string Symbol);

/// <summary>The day bars (the <c>1D</c> aggregate, raw prices) the detection of corporate actions reads.</summary>
public interface IDailySeries
{
    /// <summary>The shares and ETFs (not retired) that have day bars, by symbol.</summary>
    ValueTask<IReadOnlyList<SeriesInstrument>> ListInstrumentsAsync(CancellationToken cancellationToken);

    /// <summary>The day bars of the instrument from <paramref name="from"/> (an India date) on, oldest first.</summary>
    ValueTask<IReadOnlyList<DailyBar>> ReadAsync(Guid instrumentId, DateOnly from, CancellationToken cancellationToken);
}
