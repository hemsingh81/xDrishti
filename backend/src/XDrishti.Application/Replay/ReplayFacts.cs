using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Learning;
using XDrishti.Application.Sizing;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;
using ContextInstrument = XDrishti.Application.Abstractions.Planning.ContextInstrument;

namespace XDrishti.Application.Replay;

/// <summary>
/// <see cref="IReplayFacts"/> over the existing stores and queries (SR-01): reads only, behind ports. One instance serves one run: it remembers the strategy versions, change histories, statistics and the settings snapshot it read, so a later session of the run reads
/// them from memory. <b>Learning mode recomputes the cell statistics in memory</b> from the closed trades of the source runs of the statistics frozen at the start (the P6-01 calculator reads only trades that exited on or before its as-of date) on a weekly cadence,
/// and stores nothing. The lifecycle is not recomputed (its legal moves need owner acts and out-of-sample facts, so it is not a self-running rule): the changelog as of the session stands, labelled <c>lifecycle_as_recorded</c>.
/// </summary>
internal sealed class ReplayFacts(
    IHoldOutStore holdOut,
    HoldOutOptions holdOutOptions,
    IBasketQueries baskets,
    IInstrumentQueries instruments,
    IStrategyStore strategies,
    ICellStatsStore stats,
    ILearningChangelog changelog,
    IRunStore runs,
    IBarStore minuteBars,
    IBackfillSeams seams,
    IBarQueries dayBars,
    ITradingCalendar calendars,
    IContextFigures contextFigures,
    IHoldingClassifications classifications,
    ICostSheetStore costSheets,
    IBasketSettingsStore basketSettings,
    ISizingStore sizing,
    SystemReplayOptions options,
    SmartRulesOptions? smart = null,
    SelectionRules? rules = null,
    FillConstraints? fill = null,
    CostStressOptions? costStress = null,
    IBasketContextStore? basketContext = null,
    ConfidenceOptions? confidence = null,
    ShrinkageOptions? shrinkage = null,
    ILogger<ReplayFacts>? logger = null) : IReplayFacts, IReplayInputs
{
    /// <summary>Calendar days of daily bars read before the close of the session for the correlations (about 60 sessions plus slack), as the live context source reads.</summary>
    public const int ReturnLookbackDays = 120;

    private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

    private readonly ClampedBars _bars = new(holdOut, holdOutOptions, minuteBars, seams, dayBars, options.ReadAheadSessions);
    private readonly Dictionary<string, StoredVersion?> _startVersions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<StoredVersion>> _allVersions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<ChangelogEntry>> _histories = new(StringComparer.Ordinal);
    private readonly Dictionary<DateOnly, IReadOnlyList<StoredCellStats>> _learned = [];
    private readonly Dictionary<Guid, string?> _symbols = [];
    private bool _versionsFixed;
    private bool _learningFixed;
    private string? _learningSources;
    private IReadOnlyList<StoredCellStats>? _frozen;
    private IReadOnlyList<CellInput>? _learningInputs;
    private ReplaySettingsSnapshot? _settings;
    private TradingCalendar? _calendar;

    public ValueTask<DateOnly> LastReadableSessionAsync(CancellationToken cancellationToken) => _bars.LastReadableAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<(DateOnly Session, double Close)>> DayClosesAsync(Guid instrumentId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        [.. (await _bars.DaysAsync(instrumentId, from, to, cancellationToken)).Select(b => (XDrishti.Domain.Market.IndiaTime.Today(b.Ts), b.Close))];

    public void BeginRun(DateOnly last, ReplaySettingsSnapshot? stored, IReadOnlyDictionary<string, IReadOnlyList<StoredVersion>>? versions = null, string? learningSources = null, bool learningFixed = false)
    {
        _bars.PlanRange(last);
        if (learningFixed)
        {
            _learningFixed = true;
            _learningSources = learningSources;
        }

        _settings = stored ?? _settings;
        if (versions is not null)
        {
            _versionsFixed = true;
            foreach (var (strategy, list) in versions)
            {
                _allVersions[strategy] = list;
                _startVersions[strategy] = list.MaxBy(v => v.Version);
            }
        }
    }

    /// <summary>
    /// The strategy versions the run reads, each strategy of its cells: under the <c>Start</c> rule the active version, under <c>AsOf</c> every version written before the close of the last session. Stored with the run and hashed into its input hash; a resumed run uses the stored map.
    /// </summary>
    public async ValueTask<IReadOnlyDictionary<string, IReadOnlyList<StoredVersion>>> VersionsAsync(ReplayParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var map = new SortedDictionary<string, IReadOnlyList<StoredVersion>>(StringComparer.Ordinal);
        var close = BasketMember.CutoffAfter(parameters.To);
        foreach (var cell in (await CellsAsync(parameters, parameters.From, cancellationToken)).Cells)
        {
            var strategy = cell.Stored.Row.Cell.Strategy;
            if (map.ContainsKey(strategy))
            {
                continue;
            }

            if (parameters.VersionRule == ReplayVersionRule.Start)
            {
                map[strategy] = await strategies.GetVersionAsync(strategy, null, cancellationToken) is { } active ? [active] : [];
            }
            else
            {
                map[strategy] = [.. (await strategies.ListVersionsAsync(strategy, cancellationToken)).Where(v => v.CreatedAt < close).OrderBy(v => v.Version)];
            }
        }

        return map;
    }

    /// <summary>A hash of the day bars (their times, prices, volumes and the count of 1-minute bars in each) of the instruments over the window the run reads, up to its last session: a change in the stored minutes changes it. Read through the clamp.</summary>
    public async ValueTask<string> BarHashAsync(IReadOnlyList<Guid> instrumentIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var text = new System.Text.StringBuilder();
        foreach (var id in instrumentIds.Distinct().Order())
        {
            text.Append(id.ToString("N")).Append(" seam=").Append((await seams.SeamOfAsync(id, cancellationToken))?.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none").Append('\n');
            foreach (var bar in await _bars.DaysAsync(id, from.AddDays(-ClampedBars.MinuteLookbackDays), to, cancellationToken))
            {
                text.Append(bar.Ts.ToUnixTimeSeconds()).Append(' ').Append(bar.Open.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(bar.High.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
                    .Append(bar.Low.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(bar.Close.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(bar.Volume).Append(' ').Append(bar.Bars).Append(bar.Complete ? 'c' : 'i').Append('\n');
            }
        }

        return ReplayJson.Sha256(text.ToString());
    }

    public async ValueTask<TradingCalendar> CalendarAsync(CancellationToken cancellationToken) => _calendar ??= await calendars.GetAsync(cancellationToken);

    public async ValueTask<ReplaySettingsSnapshot> SettingsAsync(CancellationToken cancellationToken)
    {
        if (_settings is not null)
        {
            return _settings;
        }

        var basket = await basketSettings.CurrentAsync(cancellationToken);
        var sizingState = await sizing.CurrentAsync(cancellationToken);
        var budgets = await sizing.BudgetsAsync(cancellationToken);
        _settings = new ReplaySettingsSnapshot(
            rules ?? new SelectionRules(),
            smart ?? new SmartRulesOptions(),
            fill ?? new FillConstraints(),
            basket,
            SizingInputSource.ClampCapacity(sizingState, costStress ?? new CostStressOptions()),
            budgets,
            options.IncompleteDataShare);
        return _settings;
    }

    public async ValueTask<Result<CostSheet?>> CostSheetAsync(ReplayParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.CostSheetId is not { } id)
        {
            return Result.Success<CostSheet?>(null);
        }

        var found = await costSheets.FindAsync(id, cancellationToken);
        return found.IsFailure ? Result.Failure<CostSheet?>(found.Error) : Result.Success<CostSheet?>(found.Value);
    }

    public async ValueTask<ReplayUniverse> UniverseAsync(ReplayParams parameters, DateOnly cut, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var members = new Dictionary<Guid, string>();
        var survivorship = false;
        foreach (var basketId in parameters.Scope.BasketIds)
        {
            if (await baskets.MembersAsync(basketId, BasketMember.CutoffAfter(cut), null, cancellationToken) is not { } found)
            {
                continue;
            }

            if (found.ArchivedAt is { } archived && archived <= BasketMember.CutoffAfter(cut))
            {
                continue; // a basket archived on or before the cut contributes no member (as the live scope does for an archived basket)
            }

            survivorship |= found.MembersKnownSince is { } since && cut < since;
            foreach (var m in found.Items)
            {
                members.TryAdd(m.Instrument.Id, m.Instrument.Symbol);
            }
        }

        foreach (var id in parameters.Scope.InstrumentIds)
        {
            if (members.ContainsKey(id))
            {
                continue;
            }

            if (!_symbols.TryGetValue(id, out var symbol))
            {
                _symbols[id] = symbol = (await instruments.GetAsync(id, cancellationToken))?.Symbol; // once for the run, not once per session
            }

            if (symbol is not null)
            {
                members[id] = symbol;
            }
        }

        return new ReplayUniverse([.. members.OrderBy(p => p.Value, StringComparer.Ordinal).Select(p => new ReplayInstrument(p.Key, p.Value))], survivorship);
    }

    public async ValueTask<ReplayCells> CellsAsync(ReplayParams parameters, DateOnly cut, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var calendar = await CalendarAsync(cancellationToken);
        var freezeDate = NextSession(calendar, parameters.From);
        var frozen = _frozen ??= CashCells.Only(await stats.ListLatestBeforeAsync(freezeDate, null, CellWindow.All.Code(), cancellationToken), "replay", logger);
        var notes = new List<string>();
        IReadOnlyList<StoredCellStats> rows = frozen;
        DateTimeOffset stateCut;
        if (parameters.Mode == ReplayMode.Learning)
        {
            rows = await LearnedAsync(frozen, parameters.From, cut, cancellationToken);
            stateCut = BasketMember.CutoffAfter(cut); // the changelog as recorded through the close of the session
            notes.Add(HindsightLabels.LifecycleAsRecorded);
        }
        else
        {
            stateCut = new DateTimeOffset(freezeDate.ToDateTime(TimeOnly.MinValue), IndiaOffset);
        }

        var cells = new List<ReplayCell>(rows.Count);
        foreach (var stored in rows.OrderBy(r => r.Row.Cell.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = stored.Row.Cell.Key;
            if (!_histories.TryGetValue(key, out var history))
            {
                _histories[key] = history = await changelog.HistoryAsync(key, cancellationToken);
            }

            var state = CellStateAsOf.Of(history, stateCut);
            if (parameters.IncludeResearchCells && state is null or CellState.Research)
            {
                state = CellState.Candidate; // hindsight, labelled research_cells: the changelog is empty before October 2026
            }

            cells.Add(new ReplayCell(stored, state, await VersionAsync(parameters.VersionRule, stored.Row.Cell.Strategy, cut, cancellationToken)));
        }

        return new ReplayCells(cells, notes);
    }

    public async ValueTask<IReadOnlyList<Domain.Market.Bars.Bar>> MinuteBarsAsync(Guid instrumentId, DateOnly cut, CancellationToken cancellationToken) =>
        await _bars.MinutesAsync(instrumentId, cut, cancellationToken);

    public async ValueTask<ReplayMarket> MarketAsync(DateOnly cut, DateOnly forDate, IReadOnlyList<ContextInstrument> instrumentList, IReadOnlyList<ContextInstrument> ranked, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentList);
        ArgumentNullException.ThrowIfNull(ranked);
        await _bars.EnsureAsync(cut, cancellationToken);
        var ids = instrumentList.Where(i => i.Id is not null).Select(i => i.Id!.Value).Distinct().ToList();
        // shortcut: sectors and basket names are today's, not those of the session (part of settings_now); upgrade when dated classifications exist.
        var sectors = ids.Count == 0 ? new Dictionary<Guid, string>() : (await classifications.SectorsAsync(ids, cancellationToken)).ToDictionary(p => p.Key, p => p.Value);
        var memberships = ids.Count == 0 ? new Dictionary<Guid, IReadOnlyList<BasketRef>>() : (await classifications.BasketsAsync(ids, cancellationToken)).ToDictionary(p => p.Key, p => p.Value);
        var facts = new Dictionary<string, InstrumentFacts>(StringComparer.Ordinal);
        foreach (var i in instrumentList)
        {
            // The replay account holds nothing long-term: no portfolio exists to hold it (HeldLong is false, not unknown).
            facts[i.Symbol.ToUpperInvariant()] = new InstrumentFacts(
                i.Id is { } sid && sectors.TryGetValue(sid, out var sector) ? sector : null,
                i.Id is { } bid ? memberships.TryGetValue(bid, out var list) ? [.. list.Select(b => b.Name).Order(StringComparer.Ordinal)] : [] : null,
                false);
        }

        var (gate, note) = await GateAsync(forDate, cancellationToken);
        return new ReplayMarket(gate, note, await CorrelationsAsync(ranked, cut, forDate, cancellationToken), facts);
    }

    public async ValueTask<SizingInputs> SizingAsync(DateOnly cut, DateOnly forDate, IReadOnlyList<ContextInstrument> instrumentList, CancellationToken cancellationToken)
    {
        await _bars.EnsureAsync(cut, cancellationToken);
        var settings = await SettingsAsync(cancellationToken);
        return await SizingInputSource.ComputeAsync(
            settings.Sizing, settings.Budgets, cut, forDate, instrumentList, (id, from, toExclusive, ct) => _bars.DaysAsync(id, from, toExclusive.AddDays(-1), ct), await CalendarAsync(cancellationToken), classifications, cancellationToken);
    }

    /// <summary>The first trading day after <paramref name="session"/> (the plan date); the next calendar day when the calendar knows none within ten days.</summary>
    internal static DateOnly NextSession(TradingCalendar calendar, DateOnly session)
    {
        for (var i = 1; i <= 10; i++)
        {
            if (calendar.IsTradingDay(session.AddDays(i)))
            {
                return session.AddDays(i);
            }
        }

        return session.AddDays(1);
    }

    private async ValueTask<StoredVersion?> VersionAsync(ReplayVersionRule rule, string strategy, DateOnly cut, CancellationToken cancellationToken)
    {
        if (_versionsFixed)
        {
            // the run carries its version map: a strategy outside it has no version, and the store is never asked
            var known = _allVersions.GetValueOrDefault(strategy) ?? [];
            return rule == ReplayVersionRule.Start ? known.MaxBy(v => v.Version) : known.Where(v => v.CreatedAt < BasketMember.CutoffAfter(cut)).MaxBy(v => v.Version);
        }

        if (rule == ReplayVersionRule.Start)
        {
            if (!_startVersions.TryGetValue(strategy, out var active))
            {
                _startVersions[strategy] = active = await strategies.GetVersionAsync(strategy, null, cancellationToken);
            }

            return active;
        }

        if (!_allVersions.TryGetValue(strategy, out var all))
        {
            _allVersions[strategy] = all = await strategies.ListVersionsAsync(strategy, cancellationToken);
        }

        var close = BasketMember.CutoffAfter(cut);
        return all.Where(v => v.CreatedAt < close).MaxBy(v => v.Version);
    }

    /// <summary>
    /// The statistics of the week of <paramref name="cut"/>: the frozen rows in the week of the start, then each Monday the P6-01 calculator over the closed trades of the frozen snapshot's source runs that exited up to the Sunday before, with the options of the frozen
    /// snapshot (the cadence of the weekly learning service of doc 04). Nothing is stored.
    /// </summary>
    private async ValueTask<IReadOnlyList<StoredCellStats>> LearnedAsync(IReadOnlyList<StoredCellStats> frozen, DateOnly from, DateOnly cut, CancellationToken cancellationToken)
    {
        var asOf = WeekRefresh(cut);
        if (frozen.Count == 0 || asOf < WeekRefresh(from) || asOf == WeekRefresh(from))
        {
            return frozen;
        }

        if (_learned.TryGetValue(asOf, out var cached))
        {
            return cached;
        }

        var first = frozen[0];

        // The sources of the NEWEST snapshot, not of the frozen one: a run made after the start holds the trades the replay "learns" from, and the calculator's own rule (only trades that exited on or before the as-of date) makes the cut.
        // The strategies were designed on this history, which the data_seen_before label says.
        var sourceText = _learningFixed ? _learningSources ?? first.Sources : await NewestSourcesAsync(first.Sources, cancellationToken);
        if (CellSource.TryParseList(sourceText) is not { } sources)
        {
            return frozen; // a snapshot whose sources cannot be read keeps its rows: nothing is guessed
        }

        if (_learningInputs is null)
        {
            var read = await CellSourceReader.ReadAsync(runs, sources, cancellationToken);
            if (read.IsFailure)
            {
                return frozen;
            }

            _learningInputs = read.Value.Cells;
        }

        var key = "replay-learning:" + asOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var created = new DateTimeOffset(asOf.ToDateTime(TimeOnly.MinValue), IndiaOffset);
        var wanted = frozen.Select(r => r.Row.Cell.Key).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<StoredCellStats> rows = [.. CellStatsCalculator.Compute(_learningInputs, asOf, first.Options, null, confidence, shrinkage)
            .Where(r => r.Window == CellWindow.All && wanted.Contains(r.Cell.Key))
            .Select(r => new StoredCellStats(key, created, first.Options, first.Sources, r))];
        _learned[asOf] = rows;
        return rows;
    }

    /// <summary>The sources of the newest cash statistics snapshot (today's), or <paramref name="fallback"/> when there is none.</summary>
    private async ValueTask<string> NewestSourcesAsync(string fallback, CancellationToken cancellationToken)
    {
        var newest = await stats.ListLatestCashAsync(null, CellWindow.All.Code(), cancellationToken);
        return newest.Count > 0 ? newest[0].Sources : fallback;
    }

    /// <summary>
    /// The sources learning mode recomputes from, taken once at the start and stored with the run: the sources of the newest snapshot (the closed trades the weekly refresh has by then), or null in frozen mode. A later refresh must not change a run that has started.
    /// </summary>
    public async ValueTask<string?> LearningSourcesAsync(ReplayParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Mode != ReplayMode.Learning)
        {
            return null;
        }

        await CellsAsync(parameters, parameters.From, cancellationToken); // takes the frozen rows
        return _frozen is { Count: > 0 } rows ? await NewestSourcesAsync(rows[0].Sources, cancellationToken) : null;
    }

    /// <summary>The Sunday before the week of <paramref name="day"/> (the day the weekly refresh knows trades up to).</summary>
    internal static DateOnly WeekRefresh(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7) - 1);

    private async ValueTask<(MarketGateInput? Gate, string? Note)> GateAsync(DateOnly forDate, CancellationToken cancellationToken)
    {
        var read = await contextFigures.AsOfAsync(forDate, cancellationToken);
        var state = (await SettingsAsync(cancellationToken)).Basket.Settings;
        if (state.GateBasketId is { } basketId && basketContext is not null)
        {
            var row = await basketContext.LatestBeforeAsync(basketId, forDate, cancellationToken);
            var name = (await baskets.GetAsync(basketId, cancellationToken))?.Name;
            var breadth = GateBreadthSource.FromBasket(name ?? basketId.ToString("D"), row?.AsOf, row?.B20, row?.State, forDate, (smart ?? new SmartRulesOptions()).GateMaxAgeDays);
            var asOf = read.Figures is null ? row?.AsOf : row is null ? read.Figures.AsOf : (read.Figures.AsOf < row.AsOf ? read.Figures.AsOf : row.AsOf);
            return asOf is { } day ? (new MarketGateInput(day, read.Figures?.Vix, breadth.Breadth, null), breadth.Note) : (null, breadth.Note);
        }

        if (read.Figures is not { } figures || (figures.Vix is null && figures.Breadth is null))
        {
            return (null, null);
        }

        return (new MarketGateInput(figures.AsOf, figures.Vix, figures.Breadth is { } percent ? percent / 100.0 : null, null), figures.Breadth is null ? null : PlanContextNotes.MarketBreadth(figures.BreadthUniverse));
    }

    private async ValueTask<CorrelationTable?> CorrelationsAsync(IReadOnlyList<ContextInstrument> ranked, DateOnly session, DateOnly forDate, CancellationToken cancellationToken)
    {
        var withIds = ranked.Where(i => i.Id is not null).ToList();
        if (withIds.Count < 2)
        {
            return null;
        }

        var returns = new Dictionary<string, IReadOnlyList<DailyReturn>>(StringComparer.Ordinal);
        foreach (var i in withIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var daily = await _bars.DaysAsync(i.Id!.Value, session.AddDays(-ReturnLookbackDays), session, cancellationToken);
            returns[i.Symbol] = ReturnSeries.FromCloses(daily.Where(b => b.Complete).Select(b => (DateOnly.FromDateTime(b.Ts.ToOffset(IndiaOffset).DateTime), b.Close)), session);
        }

        return CorrelationTable.FromReturns(returns, forDate, [.. withIds.Select(i => i.Symbol)]);
    }
}
