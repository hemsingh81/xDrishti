using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>Totals over the stored 1-minute bars (from the coverage table: cheap enough to ask while a job is polled).</summary>
public sealed record BarTotals(int Instruments, long Bars, DateTimeOffset? FirstBar, DateTimeOffset? LastBar, int InstrumentsBehind, int InstrumentsEmpty);

/// <summary>What the aggregates hold.</summary>
public sealed record AggregateTotals(IReadOnlyList<TimeframeStat> Timeframes, DateTimeOffset? LastBuiltAt, int InstrumentsPending);

/// <summary>An instrument as the bars endpoint and the simulation need it. <paramref name="TickSize"/> is the master's tick in paise (null when the master has none); a run turns it into rupees with <c>TickSpec.FromMasterPaise</c>.</summary>
public sealed record BarsInstrument(Guid Id, string Symbol, string Name, decimal? TickSize = null);

/// <summary>Reads of the stored bars and their coverage (all reads, no tracking).</summary>
public interface IBarQueries
{
    /// <summary><paramref name="expectedStart"/> is the start (India midnight) of the last session that has closed: an instrument whose last bar is earlier is behind.</summary>
    ValueTask<BarTotals> GetBarTotalsAsync(DateTimeOffset expectedStart, CancellationToken cancellationToken);

    ValueTask<AggregateTotals> GetAggregateTotalsAsync(CancellationToken cancellationToken);

    /// <summary>The tracked instruments and the ones that have bars, with their coverage; how far behind each is counts the trading days of <paramref name="calendar"/> up to <paramref name="expectedSession"/>.</summary>
    ValueTask<DataInstrumentPage> ListInstrumentsAsync(DataInstrumentSearch search, DateTimeOffset expectedStart, DateOnly expectedSession, TradingCalendar calendar, CancellationToken cancellationToken);

    ValueTask<BarsInstrument?> FindInstrumentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The newest <paramref name="take"/> bars of the timeframe, newest first.</summary>
    ValueTask<IReadOnlyList<BarRow>> ReadBarsAsync(Guid instrumentId, Timeframe timeframe, int take, CancellationToken cancellationToken);

    /// <summary>The day bars (<c>1D</c>) of the India dates from <paramref name="from"/> up to, not including, <paramref name="toExclusive"/>, oldest first.</summary>
    ValueTask<IReadOnlyList<BarRow>> ReadDailyBarsAsync(Guid instrumentId, DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken);
}
