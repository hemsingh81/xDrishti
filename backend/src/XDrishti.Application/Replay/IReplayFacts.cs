using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Replay;

/// <summary>
/// The as-of facts of the system replay (SR-01): every read takes the close of a session as its cut and returns only what was known then. Bars are read only through a reader clamped to the hold-out start less its embargo; a read past it fails loudly with
/// <see cref="ReplayHoldOutException"/>. Read only: nothing behind this port writes anything or places an order. An instance serves one run (it keeps what it read once for the whole run).
/// </summary>
public interface IReplayFacts
{
    /// <summary>The last session the replay may read: the hold-out start less one day and less the embargo. Throws <see cref="ReplayHoldOutException"/> when no block is fixed.</summary>
    ValueTask<DateOnly> LastReadableSessionAsync(CancellationToken cancellationToken);

    /// <summary>The trading calendar.</summary>
    ValueTask<TradingCalendar> CalendarAsync(CancellationToken cancellationToken);

    /// <summary>The settings snapshot to use for the whole run (taken on the first call, then the same).</summary>
    ValueTask<ReplaySettingsSnapshot> SettingsAsync(CancellationToken cancellationToken);

    /// <summary>The charge sheet of the run, <c>null</c> inside the success when the run is gross; a failure when a named sheet is not stored.</summary>
    ValueTask<Result<CostSheet?>> CostSheetAsync(ReplayParams parameters, CancellationToken cancellationToken);

    /// <summary>The members of the scope as of the close of <paramref name="cut"/>.</summary>
    ValueTask<ReplayUniverse> UniverseAsync(ReplayParams parameters, DateOnly cut, CancellationToken cancellationToken);

    /// <summary>The cash cells with their statistics (frozen at the start date, or recomputed in memory as of the cut on the weekly cadence), their state and their strategy version as of the cut.</summary>
    ValueTask<ReplayCells> CellsAsync(ReplayParams parameters, DateOnly cut, CancellationToken cancellationToken);

    /// <summary>The 1-minute bars of the instrument up to and including the close of <paramref name="cut"/>, about 400 calendar days.</summary>
    ValueTask<IReadOnlyList<Bar>> MinuteBarsAsync(Guid instrumentId, DateOnly cut, CancellationToken cancellationToken);

    /// <summary>The market gate, the correlations of the ranked instruments and the instrument facts of <paramref name="instruments"/>, as of the close of <paramref name="cut"/> for the plan of <paramref name="forDate"/>.</summary>
    ValueTask<ReplayMarket> MarketAsync(DateOnly cut, DateOnly forDate, IReadOnlyList<ContextInstrument> instruments, IReadOnlyList<ContextInstrument> ranked, CancellationToken cancellationToken);

    /// <summary>The inputs of the down-only size scaling as of the close of <paramref name="cut"/>.</summary>
    ValueTask<SizingInputs> SizingAsync(DateOnly cut, DateOnly forDate, IReadOnlyList<ContextInstrument> instruments, CancellationToken cancellationToken);
}
