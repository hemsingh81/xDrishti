using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Replay;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SizeScaling;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Api.IntegrationTests.Replay;

/// <summary>
/// What a start reads, answered by hand (SR-01 part C): the as-of facts and the inputs of a replay without the years of bars, cell statistics and strategies the real readers need. Everything a start stores, hashes and records is the real code over the real database; only these reads are fixed.
/// The one cell, the strategy version and the members are the test's choice; <see cref="Cells"/> empty is the "no cell statistics" refusal and <see cref="Members"/> large is the memory cap.
/// </summary>
internal sealed class StubReplayFacts : IReplayFacts, IReplayInputs
{
    public static readonly DateOnly Last = new(2026, 6, 30);

    public int Members { get; set; } = 2;

    public bool Cells { get; set; } = true;

    public ValueTask<DateOnly> LastReadableSessionAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Last);

    public ValueTask<IReadOnlyList<(DateOnly Session, double Close)>> DayClosesAsync(Guid instrumentId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<(DateOnly Session, double Close)>>([]);

    public ValueTask<TradingCalendar> CalendarAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new TradingCalendar());

    public ValueTask<ReplaySettingsSnapshot> SettingsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ReplaySettingsSnapshot(new SelectionRules(), new SmartRulesOptions(), new FillConstraints(), new BasketSettingsState(BasketSettings.Defaults, 1), new SizingState(SizingSettings.Defaults, 0), new Dictionary<Guid, double>(), 0.2));

    public ValueTask<Result<CostSheet?>> CostSheetAsync(ReplayParams parameters, CancellationToken cancellationToken) => ValueTask.FromResult(Result.Success<CostSheet?>(null));

    public ValueTask<ReplayUniverse> UniverseAsync(ReplayParams parameters, DateOnly cut, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ReplayUniverse([.. Enumerable.Range(0, Members).Select(i => new ReplayInstrument(new Guid(i + 1, 0, 0, new byte[8]), "S" + i))], false));

    public ValueTask<ReplayCells> CellsAsync(ReplayParams parameters, DateOnly cut, CancellationToken cancellationToken)
    {
        var cell = new CellId("S1", "1D", "BUY", "swing", "FIXED_RR{rr=2}", Expression: Expression.Cash);
        var stored = new StoredCellStats(new string('1', 64), DateTimeOffset.UnixEpoch, new CellStatsOptions(), "run:swing", new CellStatsRow(cell, CellWindow.All, cut, true, 40, 20, 40, 0.5, 0.5, 0.55, 0.50, 0.6, 2.0, -1.0, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true));
        return ValueTask.FromResult(new ReplayCells(Cells ? [new ReplayCell(stored, null, null)] : [], []));
    }

    public ValueTask<IReadOnlyList<Bar>> MinuteBarsAsync(Guid instrumentId, DateOnly cut, CancellationToken cancellationToken) => throw new NotSupportedException("a start reads no bars");

    public ValueTask<ReplayMarket> MarketAsync(DateOnly cut, DateOnly forDate, IReadOnlyList<ContextInstrument> instruments, IReadOnlyList<ContextInstrument> ranked, CancellationToken cancellationToken) => throw new NotSupportedException("a start steps no session");

    public ValueTask<SizingInputs> SizingAsync(DateOnly cut, DateOnly forDate, IReadOnlyList<ContextInstrument> instruments, CancellationToken cancellationToken) => throw new NotSupportedException("a start steps no session");

    public ValueTask<string> BarHashAsync(IReadOnlyList<Guid> instrumentIds, DateOnly from, DateOnly to, CancellationToken cancellationToken) => ValueTask.FromResult(new string('2', 64));

    public ValueTask<string?> LearningSourcesAsync(ReplayParams parameters, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(parameters.Mode == ReplayMode.Learning ? "run:swing" : null);

    public ValueTask<IReadOnlyDictionary<string, IReadOnlyList<StoredVersion>>> VersionsAsync(ReplayParams parameters, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, IReadOnlyList<StoredVersion>>>(new Dictionary<string, IReadOnlyList<StoredVersion>>
        {
            ["S1"] = [new StoredVersion("S1", 1, 1, new string('3', 64), "text", "{}", null, null, DateTimeOffset.UnixEpoch)],
        });
}

/// <summary>The backfill gate of a start: complete.</summary>
internal sealed class CompleteBackfill : ISimAllBackfillGate
{
    public ValueTask<string?> IncompleteReasonAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);

    public ValueTask<bool> DataJobRunningAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);
}

/// <summary>The doubles a test puts in for the reads of a start; null (the default) is the real readers.</summary>
public sealed class ReplayStartDoubles
{
    internal StubReplayFacts? Facts { get; set; }

    internal ISimAllBackfillGate? Gate { get; set; }
}
