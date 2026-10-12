using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>Totals over the stored 1-minute bars (from the coverage table: cheap enough to ask while a job is polled).</summary>
public sealed record BarTotals(int Instruments, long Bars, DateTimeOffset? FirstBar, DateTimeOffset? LastBar, int InstrumentsBehind, int InstrumentsEmpty);

/// <summary>What the aggregates hold.</summary>
public sealed record AggregateTotals(IReadOnlyList<TimeframeStat> Timeframes, DateTimeOffset? LastBuiltAt, int InstrumentsPending);

/// <summary>An instrument as the bars endpoint needs it.</summary>
public sealed record BarsInstrument(Guid Id, string Symbol, string Name);

/// <summary>Reads of the stored bars and their coverage (all reads, no tracking).</summary>
public interface IBarQueries
{
    /// <summary><paramref name="expectedStart"/> is the start (India midnight) of the last session that has closed: an instrument whose last bar is earlier is behind.</summary>
    ValueTask<BarTotals> GetBarTotalsAsync(DateTimeOffset expectedStart, CancellationToken cancellationToken);

    ValueTask<AggregateTotals> GetAggregateTotalsAsync(CancellationToken cancellationToken);

    /// <summary>The tracked instruments and the ones that have bars, with their coverage.</summary>
    ValueTask<DataInstrumentPage> ListInstrumentsAsync(DataInstrumentSearch search, DateTimeOffset expectedStart, DateOnly expectedSession, CancellationToken cancellationToken);

    ValueTask<BarsInstrument?> FindInstrumentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The newest <paramref name="take"/> bars of the timeframe, newest first.</summary>
    ValueTask<IReadOnlyList<BarRow>> ReadBarsAsync(Guid instrumentId, Timeframe timeframe, int take, CancellationToken cancellationToken);
}
