using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>
/// Runs a strategy over the stored bars of a universe (spec P3-04a). It validates the request, builds the fill rules, asks the engine to prepare the strategy (so every refusal comes before any port is read), resolves the universe and then,
/// one instrument at a time in order, reads the bars, the adjustment factors and the corporate actions, applies the guard against adjusting a CSV-adjusted series twice, hands the values to the engine and keeps its answer. The record is
/// assembled with <see cref="RunAssembler"/>. It names no type of the engine (ADR 0039) and writes nothing; a time is read only to time the phases for the log.
/// <para>
/// A run is not resumable: an unexpected exception ends it with no partial record, and cancellation is not swallowed. Every expected condition is a skip with a reason in the record.
/// </para>
/// </summary>
internal sealed class RunSimulationHandler(
    IBarStore bars,
    IBarQueries barQueries,
    IBasketQueries baskets,
    IAdjustmentFactors adjustments,
    ICorporateActionQueries actions,
    ITradingCalendar calendars,
    ISimulationEngine engine,
    IStrategyStore strategies,
    ICostSheetStore costSheets,
    IContextDayStore contextDays,
    IEventCalendarSnapshots eventSnapshots,
    IResultsFilingTimes filingTimes,
    ISurpriseInputsReader surpriseReader,
    TimeProvider time,
    ILogger<RunSimulationHandler> logger,
    IRandomEntryEngine randomEntries,
    IPlaceboEngine placeboEngine,
    IEntryTierSource? entryTiers = null,
    ICostStressSettings? costStress = null,
    IRelativeStrengthRunSource? relativeStrength = null) : IQueryHandler<RunSimulationQuery, Result<SimulationRun>>, IQueryHandler<RunComparisonQuery, Result<ComparisonRun>>, IQueryHandler<RunNullWalkQuery, Result<NullWalkRun>>, IQueryHandler<RunPlaceboWalkQuery, Result<PlaceboWalkRun>>
{
    private readonly InstrumentInputReader _reader = new(bars, barQueries, adjustments);

    public async ValueTask<Result<SimulationRun>> HandleAsync(RunSimulationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var executed = await ExecuteAsync(query.Request, query.Progress, cancellationToken, query.BasisSink);
        if (executed.IsFailure)
        {
            return Result.Failure<SimulationRun>(executed.Error);
        }

        return executed.Value.Runs.Count == 1
            ? Result.Success(executed.Value.Runs[0])
            : Result.Failure<SimulationRun>(SimulationErrors.Invalid([new FieldError("exits", "The request names " + executed.Value.Runs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " candidate exits; a comparison is a RunComparisonQuery.", null, "simulation.exits_are_a_comparison")]));
    }

    public async ValueTask<Result<ComparisonRun>> HandleAsync(RunComparisonQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var executed = await ExecuteAsync(query.Request, query.Progress, cancellationToken);
        if (executed.IsFailure)
        {
            return Result.Failure<ComparisonRun>(executed.Error);
        }

        var done = executed.Value;
        if (done.Runs.Count < 2)
        {
            return Result.Failure<ComparisonRun>(SimulationErrors.Invalid([new FieldError("exits", "A comparison needs two candidate exits or more; give --exit twice or a list of values.", null, "simulation.comparison_needs_candidates")]));
        }

        var candidates = new List<CandidateRun>(done.Runs.Count);
        for (var i = 0; i < done.Runs.Count; i++)
        {
            candidates.Add(new CandidateRun(done.Candidates[i].Id, done.Candidates[i].Exit!, done.Runs[i], done.Gated[i]));
        }

        // D4: a swing comparison is not gated, an intraday one has nothing to gate
        var report = ComparisonReport.Build(candidates, gateApplied: !done.HoldsOvernight);
        return Result.Success(new ComparisonRun(candidates.AsReadOnly(), report, done.EntriesKey));
    }

    /// <summary>
    /// The random-entry walk of CF-07: the same preparation and reading as a run, but each instrument of the plan is handed to the random-entry engine instead of the replay, over the window of the request only. The run record that would be assembled is not built.
    /// </summary>
    public async ValueTask<Result<NullWalkRun>> HandleAsync(RunNullWalkQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var collected = new Dictionary<int, NullTradeDraws>();
        var plan = query.Plan;
        var executed = await ExecuteAsync(query.Request, query.Progress, cancellationToken, null, new NullWalk(
            id => plan.Trades.Any(t => t.InstrumentId == id),
            (strategy, input, done, ct) =>
            {
                foreach (var draws in randomEntries.WalkRandomEntries(strategy, input, plan, done, ct).Trades)
                {
                    collected[draws.TradeId] = draws;
                }
            }));
        return executed.IsFailure
            ? Result.Failure<NullWalkRun>(executed.Error)
            : Result.Success(new NullWalkRun([.. query.Plan.Trades.Select(t => collected.GetValueOrDefault(t.Id) ?? new NullTradeDraws(t.Id, [], 0, "the instrument could not be read"))]));
    }

    /// <summary>
    /// The placebo walk of CF-07 part B: the same preparation and reading as a run, but each instrument that has a signal in the plan (the roll order is made of them) is handed to the placebo engine instead of the replay, over the window of the request only.
    /// </summary>
    public async ValueTask<Result<PlaceboWalkRun>> HandleAsync(RunPlaceboWalkQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        // shortcut: this is a second ExecuteAsync beside the random-entry walk, so the strategy is prepared again and the bars are read again; when runtime on real data requires it, do both walks in one read per instrument.
        var plan = query.Plan;
        var outcomes = new List<PlaceboOutcome>();
        var executed = await ExecuteAsync(query.Request, query.Progress, cancellationToken, null, new NullWalk(
            id => plan.RollOrder.Contains(id),
            (strategy, input, done, ct) => outcomes.AddRange(placeboEngine.WalkPlacebos(strategy, input, plan, done, ct).Outcomes)));
        return executed.IsFailure ? Result.Failure<PlaceboWalkRun>(executed.Error) : Result.Success(new PlaceboWalkRun(outcomes));
    }

    /// <summary>A random-entry or placebo walk a call of <see cref="ExecuteAsync"/> is made for: which instruments it reads and what it does with each one (the draws are kept by the caller of the walk).</summary>
    private sealed record NullWalk(Func<Guid, bool> Wants, Action<IPreparedStrategy, InstrumentReplayInput, IProgress<int>?, CancellationToken> Walk);

    /// <summary>Turns the one-per-trade reports of the random-entry engine into the running total the progress port takes.</summary>
    private sealed class TradeCounter(IProgress<int>? progress) : IProgress<int>
    {
        private int _done;

        public void Report(int value) => progress?.Report(Interlocked.Add(ref _done, value));
    }

    /// <summary>What a run produced: one run record per candidate (one for a plain run), the gated counts and the entries key.</summary>
    private sealed record Executed(IReadOnlyList<SimulationRun> Runs, IReadOnlyList<CandidateRecord> Candidates, IReadOnlyList<long> Gated, string EntriesKey, bool HoldsOvernight);

    private async ValueTask<Result<Executed>> ExecuteAsync(SimulationRequest request, IProgress<int>? progress, CancellationToken cancellationToken, ExplainBasisSink? basisSink = null, NullWalk? nullWalk = null)
    {

        // (1) the request, (2) the fill rules, (3) the strategy: no port is read until all three pass.
        var errors = SimulationRequestChecks.Validate(request, out var timeframe);
        if (errors.HasErrors)
        {
            return Result.Failure<Executed>(SimulationErrors.Invalid(errors.Items));
        }

        var stress = costStress?.Current ?? new CostStressOptions();
        var fills = BuildFills(request.Fills, stress);
        if (fills.IsFailure)
        {
            return Result.Failure<Executed>(fills.Error);
        }

        var prepared = await PrepareStrategyAsync(request, timeframe, fills.Value, cancellationToken);
        if (prepared.IsFailure)
        {
            // The port contract already caps the list at 20; this is a guard for an engine that does not.
            return Result.Failure<Executed>(prepared.Error.Fields is { Count: > SimulationErrors.MaxStrategyProblems } fields ? SimulationErrors.StrategyInvalid(fields) : prepared.Error);
        }

        // CF-05, ADR 0230: a strategy that names `min_turnover` carries the threshold in its fill rules, so the run key changes with it; a strategy that does not keeps its rules and its key.
        var finalFills = fills.Value;
        if (prepared.Value.ReadsTurnover)
        {
            var basis = finalFills ?? FillRules.Default;
            var withThreshold = FillRules.Create(basis.EntryLimitPercent, basis.Slippage, basis.EntryDelayMinutes, basis.ConservativeSlippage, basis.TierABps, basis.TierBBps, stress.MinTurnoverCrore);
            if (withThreshold.IsFailure)
            {
                return Result.Failure<Executed>(withThreshold.Error);
            }

            finalFills = withThreshold.Value;
            prepared = await PrepareStrategyAsync(request, timeframe, finalFills, cancellationToken);
            if (prepared.IsFailure)
            {
                return Result.Failure<Executed>(prepared.Error);
            }
        }

        // (4) only then the ports: the charge sheet first (a gross choice reads nothing), then the rest.
        var started = time.GetTimestamp();
        var costs = await ResolveCostsAsync(request, cancellationToken);
        if (costs.IsFailure)
        {
            return Result.Failure<Executed>(costs.Error);
        }

        var costContext = costs.Value;
        var universe = await ResolveUniverseAsync(request, cancellationToken);
        if (universe.IsFailure)
        {
            return Result.Failure<Executed>(universe.Error);
        }

        var targets = universe.Value;

        // P15-06 (ADR 0182): a strategy that names `not_event_day` gets the stored results calendar, read once for the whole run, before any bar is read. No rows at all, or a failed read, refuse the run: an empty calendar would make every day look like "no event".
        EventCalendarSnapshot? events = null;
        EventCalendarSnapshot? snapshotRead = null;
        if (prepared.Value.ReadsEvents || prepared.Value.ReadsResultsDays)
        {
            var snapshot = await eventSnapshots.ReadAsync(targets.Ids, request.To, cancellationToken);
            if (snapshot.IsFailure)
            {
                return Result.Failure<Executed>(SimulationErrors.EventCalendarNotAvailable(snapshot.Error.Message));
            }

            snapshotRead = snapshot.Value;
            if (prepared.Value.ReadsEvents)
            {
                if (snapshot.Value.RowCount == 0)
                {
                    return Result.Failure<Executed>(SimulationErrors.EventCalendarNotAvailable("no results calendar rows for the window (P15-04)"));
                }

                events = snapshot.Value;
            }
        }

        // P15-05 (ADR 0185): a strategy that names a results-days feature gets the snapshot and the results filing times, read once before any bar. Neither a calendar row nor a filing in the window refuses the run.
        ResultsDayInputs? resultsDays = null;
        var missed = (Missed: 0, Total: 0);
        if (prepared.Value.ReadsResultsDays)
        {
            var filings = await filingTimes.ReadAsync(targets.Ids, cancellationToken);
            var through = new DateTimeOffset(request.To.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
            var inWindow = filings.Where(f => f.BroadcastTs < through).ToList();
            SurpriseInputs? surprises = null;
            if (prepared.Value.ReadsSurprise)
            {
                // P15-09: the breakpoints are made from the previous complete season of the universe of the run, so the numbers of every filing of it are read once, before any bar.
                surprises = await surpriseReader.ReadAsync(inWindow, snapshotRead!, await calendars.GetAsync(cancellationToken), TimeSpan.FromMinutes(snapshotRead!.LagMinutes), cancellationToken);
            }

            resultsDays = ResultsDayInputs.Create(snapshotRead, inWindow, snapshotRead!.LagMinutes, surprises);
            if (surprises is not null)
            {
                missed = resultsDays.CountMissedReactionDays(await calendars.GetAsync(cancellationToken), request.From, request.To);
            }
            if (resultsDays.IsEmpty)
            {
                return Result.Failure<Executed>(SimulationErrors.EventCalendarNotAvailable("no results calendar or filings for the window (P15)"));
            }
        }

        SimulationLog.RunStarted(logger, request.StrategyId, targets.Label, targets.Ids.Count, request.From, request.To, prepared.Value.Warnings.Count, request.Costs is CostChoice.SheetChoice named ? named.SheetId : "gross");
        var calendar = await calendars.GetAsync(cancellationToken);
        var csvThrough = targets.Ids.Count == 0 ? new Dictionary<Guid, DateOnly>() : await actions.ListCsvAdjustedThroughAsync(targets.Ids, cancellationToken);

        // P3-lift (ADR 0145): a strategy that reads the market context gets the stored days, read once for the whole run; one that does not reads none and its run key is unchanged.
        var readsContext = prepared.Value.ReadsContext;
        var days = readsContext ? await contextDays.ListAsync(cancellationToken) : null;
        if (basisSink is not null)
        {
            basisSink.Basis = new ExplainBasis(prepared.Value, calendar, csvThrough, days, events, resultsDays, entryTiers, request.Fills?.ConservativeSlippage == true);
        }

        // BK-09: a strategy that names mom_rank, rs_vs_index or sector_rs gets the stored rows of the previous closed sessions, read once per instrument; the step being off means there are none and every instrument is skipped with a reason.
        var strength = prepared.Value.ReadsRelativeStrength && relativeStrength is not null ? await relativeStrength.BeginAsync(targets.Ids, request.To, cancellationToken) : null;
        var context = new RunContext(request, calendar, csvThrough, targets, request.Options ?? ReplayOptions.Default, costContext, days, events, resultsDays, prepared.Value.ReadsTurnover, strength);
        var readTime = time.GetElapsedTime(started);
        var replayTime = TimeSpan.Zero;
        var candidates = prepared.Value.Candidates;
        var replays = candidates.Select(_ => new List<InstrumentReplay>(targets.Ids.Count)).ToList();
        var gated = new long[candidates.Count];
        var nullTrades = nullWalk is null ? null : new TradeCounter(progress);   // a random-entry or placebo walk reports the trades (pairs) done, not the instruments
        // CF-07: a random-entry or placebo walk reads only the instruments that have a real trade in the plan
        var walked = nullWalk is null ? targets.Ids : [.. targets.Ids.Where(nullWalk.Wants)];
        foreach (var id in walked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readStart = time.GetTimestamp();
            var input = await ReadInstrumentAsync(context, id, cancellationToken);
            var replayStart = time.GetTimestamp();
            readTime += time.GetElapsedTime(readStart, replayStart);

            if (nullWalk is not null)
            {
                if (input is not null)
                {
                    nullWalk.Walk(prepared.Value, input, nullTrades, cancellationToken);
                }

                replayTime += time.GetElapsedTime(replayStart);
                continue;
            }

            var many = input is null
                ? new ComparisonReplay(candidates.Select(_ => new InstrumentReplay(InstrumentRun.NotFound(id), [], [])).ToList(), candidates.Select(_ => 0L).ToList())
                : candidates.Count == 1 ? new ComparisonReplay([engine.ReplayUntilCancelled(prepared.Value, input, cancellationToken)], [0L]) : engine.ReplayComparisonUntilCancelled(prepared.Value, input, cancellationToken);
            replayTime += time.GetElapsedTime(replayStart);
            if (many.Members[0].Record.Skipped is { } reason)
            {
                var code = reason.ToCode();
                SimulationLog.InstrumentSkipped(logger, many.Members[0].Record.Symbol, many.Members[0].Record.InstrumentId, code);
            }

            for (var k = 0; k < candidates.Count; k++)
            {
                replays[k].Add(many.Members[k]);
                gated[k] += many.GatedBlocked[k];
            }

            progress?.Report(replays[0].Count);
        }

        if (nullWalk is not null)
        {
            // the random-entry walk has no run record: its caller reads the draws from the collector
            return Result.Success(new Executed([], candidates, gated, string.Empty, prepared.Value.HoldsOvernight));
        }

        var assemblyStart = time.GetTimestamp();
        var members = replays[0].Select(r => new UniverseMember(r.Record.InstrumentId, r.Record.Symbol)).ToList();
        var universeRecord = new UniverseRecord(targets.Kind, targets.BasketId, targets.BasketName, targets.Label, members);
        var runs = new List<SimulationRun>(candidates.Count);
        RunHeader? firstHeader = null;
        for (var k = 0; k < candidates.Count; k++)
        {
            var header = new RunHeader(
                candidates[k].Record,
                engine.EngineVersion,
                request.From,
                request.To,
                context.Options,
                request.AcknowledgeCsvAdjusted,
                (finalFills ?? FillRules.Default).ToCanonicalJson(),
                universeRecord,
                calendar,
                costContext is null ? null : CostRecord.Of(costContext),
                days is null ? null : ContextFingerprint.Of(days, request.To),
                events is null ? null : new EventFilterRecord(events.Sha256, events.RowCount, events.LagMinutes, EventCalendarSnapshot.RuleVersion),
                resultsDays is null ? null : new ResultsDaysRecord(resultsDays.Calendar?.Sha256 ?? "-", resultsDays.Calendar?.RowCount ?? 0, resultsDays.FilingsSha256, resultsDays.FilingCount, resultsDays.LagMinutes, ResultsDaySeries.Version, resultsDays.Surprises?.Sha256, resultsDays.Surprises?.Rows ?? 0, resultsDays.Surprises is null ? null : SurpriseInputs.Version, resultsDays.Surprises?.UnverifiedRows ?? 0, resultsDays.Surprises?.Seasons ?? 0, resultsDays.Surprises?.ThinSeasons ?? 0, missed.Missed, missed.Total),
                Purpose: request.Purpose,
                RelativeStrength: context.RelativeStrength?.Record(replays[k].Sum(r => r.Record.Scans.Sum(s => s.FeatureUnknown))));
            firstHeader ??= header;
            runs.Add(RunAssembler.Assemble(header, replays[k]));
        }

        var run = runs[0];
        var skipped = replays[0].Count(r => r.Record.Skipped is not null);
        var readMs = (long)readTime.TotalMilliseconds;
        var replayMs = (long)replayTime.TotalMilliseconds;
        var assemblyMs = (long)time.GetElapsedTime(assemblyStart).TotalMilliseconds;
        SimulationLog.RunFinished(logger, run.RunKey, replays[0].Count - skipped, skipped, run.Trades.Count, readMs, replayMs, assemblyMs);
        var entriesKey = RunAssembler.EntriesKey(firstHeader!, run, candidates[0].EntriesDefinitionSha256);
        return Result.Success(new Executed(runs.AsReadOnly(), candidates, gated, entriesKey, prepared.Value.HoldsOvernight));
    }

    /// <summary>
    /// The strategy of the request (spec P3-02d, D7): a hash names a stored version; an id names the stored family (the latest version, or the one asked for); an id that is not stored but is a built-in runs the built-in of the engine, which
    /// is the same strategy by construction (the seed equals the builder). Anything else is refused with what exists.
    /// </summary>
    private async ValueTask<Result<IPreparedStrategy>> PrepareStrategyAsync(SimulationRequest request, Timeframe timeframe, FillRules? fills, CancellationToken cancellationToken)
    {
        var parameters = request.Parameters ?? new Dictionary<string, double>();
        StoredVersion? stored;
        if (!string.IsNullOrWhiteSpace(request.StrategyHash))
        {
            stored = await strategies.GetByHashAsync(request.StrategyHash, cancellationToken);
            if (stored is null)
            {
                return Refuse("strategy_hash", SimulationErrors.UnknownStrategyHashCode, "No stored strategy version has this hash.");
            }

            if (!string.IsNullOrWhiteSpace(request.StrategyId) && !string.Equals(request.StrategyId, stored.StrategyId, StringComparison.Ordinal))
            {
                return Refuse("strategy_id", SimulationErrors.UnknownStrategyCode, "The hash is a version of `" + stored.StrategyId + "`, not of `" + request.StrategyId + "`.");
            }
        }
        else
        {
            stored = await strategies.GetVersionAsync(request.StrategyId, request.StrategyVersion, cancellationToken);
            if (stored is null)
            {
                if (request.StrategyVersion is { } version)
                {
                    var family = await strategies.GetAsync(request.StrategyId, cancellationToken);
                    return family is null
                        ? await UnknownStrategyAsync(request.StrategyId, cancellationToken)
                        : Refuse("strategy_version", SimulationErrors.UnknownStrategyVersionCode, "The strategy `" + family.Id + "` has no version " + version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; its versions are 1 to " + family.LatestVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                }

                var builtIn = engine.Prepare(request.StrategyId, parameters, timeframe, fills, request.Exits);
                return builtIn.IsFailure && builtIn.Error.Fields is [{ Code: SimulationErrors.UnknownStrategyCode }]
                    ? await UnknownStrategyAsync(request.StrategyId, cancellationToken)
                    : builtIn;
            }
        }

        return engine.PrepareStored(stored.SourceText, stored.Hash, parameters, timeframe, fills, request.Exits);
    }

    private async ValueTask<Result<IPreparedStrategy>> UnknownStrategyAsync(string id, CancellationToken cancellationToken)
    {
        var known = (await strategies.ListAsync(cancellationToken)).Select(s => s.Id).Concat(engine.BuiltIns.Select(b => b.Id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return Refuse("strategy_id", SimulationErrors.UnknownStrategyCode, "The strategy `" + id + "` is not stored; the strategies are " + string.Join(", ", known.Select(k => "`" + k + "`")) + ".");
    }

    private static Result<IPreparedStrategy> Refuse(string path, string code, string message) =>
        Result.Failure<IPreparedStrategy>(SimulationErrors.StrategyInvalid([new FieldError(path, message, null, code)]));

    /// <summary>The fill rules of the request: null when it gave none (the engine then uses <see cref="FillRules.Default"/>), otherwise built with <c>Create</c> so that an invalid number comes back as the P3-03b code.</summary>
    private static Result<FillRules?> BuildFills(FillRulesInput? input, CostStressOptions stress)
    {
        if (input is null)
        {
            return Result.Success<FillRules?>(null);
        }

        var slippage = SlippageConfig.Create(input.Ticks, input.BarRangeFrac, input.StopExitExtraTicks);
        if (slippage.IsFailure)
        {
            return Result.Failure<FillRules?>(slippage.Error);
        }

        var rules = FillRules.Create(input.EntryLimitPercent, slippage.Value, input.EntryDelayMinutes, input.ConservativeSlippage, stress.TierABps, stress.TierBBps);
        return rules.IsFailure ? Result.Failure<FillRules?>(rules.Error) : Result.Success<FillRules?>(rules.Value);
    }

    /// <summary>
    /// The cost context of a sheet choice (spec P3-03f, F2): one read of the store; absent is <c>simulation.cost_sheet_not_found</c>, a stored text that no longer parses passes through, a first schedule after the first day of the window is
    /// <c>simulation.cost_sheet_not_effective</c>. A gross choice reads nothing and gives null.
    /// </summary>
    private async ValueTask<Result<CostContext?>> ResolveCostsAsync(SimulationRequest request, CancellationToken cancellationToken)
    {
        if (request.Costs is not CostChoice.SheetChoice choice)
        {
            return Result.Success<CostContext?>(null);
        }

        if (choice.SheetId.StartsWith(CostSheet.TestPrefix, StringComparison.Ordinal))
        {
            // a synthetic sheet is never stored, so it is never found, and the store is not asked
            return Result.Failure<CostContext?>(SimulationErrors.CostSheetNotFound(choice.SheetId));
        }

        var sheet = await costSheets.FindAsync(choice.SheetId, cancellationToken);
        if (sheet.IsFailure)
        {
            return Result.Failure<CostContext?>(sheet.Error.Kind == ErrorKind.NotFound ? SimulationErrors.CostSheetNotFound(choice.SheetId) : sheet.Error);
        }

        var first = sheet.Value.Schedules[0].EffectiveFrom;
        if (first > request.From)
        {
            return Result.Failure<CostContext?>(SimulationErrors.CostSheetNotEffective(choice.SheetId, first, request.From));
        }

        var profile = string.Equals(choice.Profile, SizingProfile.Conservative.Name, StringComparison.Ordinal) ? SizingProfile.Conservative : SizingProfile.Standard;
        var context = CostContext.Create(sheet.Value, profile, choice.Capital, choice.MaxCostR);
        return context.IsFailure
            ? Result.Failure<CostContext?>(SimulationErrors.Invalid([new FieldError("costs", context.Error.Message, null, context.Error.Code)]))
            : Result.Success<CostContext?>(context.Value);
    }

    private async ValueTask<Result<Universe>> ResolveUniverseAsync(SimulationRequest request, CancellationToken cancellationToken)
    {
        if (request.InstrumentIds is { } ids)
        {
            return Result.Success(new Universe(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, ids, null));
        }

        var basketId = request.BasketId!.Value;
        var members = await baskets.MembersAsync(basketId, null, null, cancellationToken);
        if (members is null || members.ArchivedAt is not null)
        {
            return Result.Failure<Universe>(SimulationErrors.BasketNotFound);
        }

        var ordered = members.Items.OrderBy(m => m.Position).ToList();
        var known = new Dictionary<Guid, BarsInstrument>();
        foreach (var member in ordered)
        {
            known[member.Instrument.Id] = new BarsInstrument(member.Instrument.Id, member.Instrument.Symbol, member.Instrument.Name, member.Instrument.TickSize);
        }

        return Result.Success(new Universe(UniverseKind.Basket, basketId, members.Name, UniverseLabels.MembersAsOfToday, [.. ordered.Select(m => m.Instrument.Id)], known));
    }

    /// <summary>
    /// Reads one instrument (spec D3, steps 1 to 5) and builds what the engine takes; null when the instrument has no row. The list of bars is local to the call, so only one instrument's bars are held at a time. The reading is
    /// <see cref="InstrumentInputReader"/>, which the trade cards use too.
    /// </summary>
    private async ValueTask<InstrumentReplayInput?> ReadInstrumentAsync(RunContext context, Guid id, CancellationToken cancellationToken)
    {
        var instrument = await _reader.FindAsync(id, context.Targets.Known, cancellationToken);
        if (instrument is null)
        {
            return null;
        }

        var request = context.Request;
        var (raw, factors) = await _reader.ReadAsync(id, request.To, cancellationToken);
        var built = InstrumentInputReader.Build(id, instrument, raw, factors, request.AcknowledgeCsvAdjusted, context.Calendar, context.CsvThrough, context.Options, request.From, request.To, context.Costs, context.ContextDays, context.ContextDays is { Count: > 0 } ? null : (context.ContextDays is null ? null : "no_context"), context.Events, context.ResultsDays,
            request.Fills?.ConservativeSlippage == true && entryTiers is not null ? await entryTiers.ForAsync(id, request.From, request.To, cancellationToken) : null,
            context.ReadsTurnover && entryTiers is not null ? await entryTiers.TurnoverForAsync(id, request.From, request.To, cancellationToken) : null);
        return context.RelativeStrength is { } strength ? built with { RelativeStrength = await strength.ReadAsync(id, request.From, cancellationToken) } : built;
    }

    private sealed record Universe(UniverseKind Kind, Guid? BasketId, string? BasketName, string Label, IReadOnlyList<Guid> Ids, IReadOnlyDictionary<Guid, BarsInstrument>? Known);

    private sealed record RunContext(
        SimulationRequest Request,
        TradingCalendar Calendar,
        IReadOnlyDictionary<Guid, DateOnly> CsvThrough,
        Universe Targets,
        ReplayOptions Options,
        CostContext? Costs,
        IReadOnlyList<ContextDayRecord>? ContextDays,
        EventCalendarSnapshot? Events,
        ResultsDayInputs? ResultsDays = null,
        bool ReadsTurnover = false,
        IRelativeStrengthRun? RelativeStrength = null);
}
