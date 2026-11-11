using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Replay;

/// <summary>One instrument of the replay universe.</summary>
/// <param name="Id">The instrument id.</param>
/// <param name="Symbol">The symbol.</param>
public sealed record ReplayInstrument(Guid Id, string Symbol);

/// <summary>The universe at a session: the members of the chosen baskets and instruments as they were at its close, and whether the earliest known membership stood in for a date before it was known.</summary>
/// <param name="Members">The members, by symbol.</param>
/// <param name="Survivorship">True when a basket's membership is not known as far back as the session (ISS-22): the earliest known membership was used.</param>
public sealed record ReplayUniverse(IReadOnlyList<ReplayInstrument> Members, bool Survivorship);

/// <summary>One cell as the replay sees it at a session: its statistics row, its state, and the strategy version the rule gives (null: <c>no_version</c>).</summary>
/// <param name="Stored">The statistics row with its snapshot facts.</param>
/// <param name="State">The state as of the cut; null when not known.</param>
/// <param name="Version">The strategy version under the version rule; null when none.</param>
public sealed record ReplayCell(StoredCellStats Stored, CellState? State, StoredVersion? Version);

/// <summary>The cells of a session.</summary>
/// <param name="Cells">The cash cells.</param>
/// <param name="Notes">Plain notes, for example the label that the lifecycle is as recorded.</param>
public sealed record ReplayCells(IReadOnlyList<ReplayCell> Cells, IReadOnlyList<string> Notes);

/// <summary>The market facts of a session: the gate and its note, the correlations of the ranked instruments, and the instrument facts by upper-case symbol.</summary>
/// <param name="Gate">The market gate input; null when none can be formed.</param>
/// <param name="MarketNote">What the breadth is.</param>
/// <param name="Correlations">Correlations of the daily returns up to the close of the session; null when not computable.</param>
/// <param name="Instruments">Sector and baskets by symbol.</param>
public sealed record ReplayMarket(MarketGateInput? Gate, string? MarketNote, CorrelationTable? Correlations, IReadOnlyDictionary<string, InstrumentFacts> Instruments);

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
