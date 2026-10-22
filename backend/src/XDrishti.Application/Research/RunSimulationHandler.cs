using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
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
    TimeProvider time,
    ILogger<RunSimulationHandler> logger) : IQueryHandler<RunSimulationQuery, Result<SimulationRun>>, IQueryHandler<RunComparisonQuery, Result<ComparisonRun>>
{
    public async ValueTask<Result<SimulationRun>> HandleAsync(RunSimulationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var executed = await ExecuteAsync(query.Request, cancellationToken);
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
        var executed = await ExecuteAsync(query.Request, cancellationToken);
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

    /// <summary>What a run produced: one run record per candidate (one for a plain run), the gated counts and the entries key.</summary>
    private sealed record Executed(IReadOnlyList<SimulationRun> Runs, IReadOnlyList<CandidateRecord> Candidates, IReadOnlyList<long> Gated, string EntriesKey, bool HoldsOvernight);

    private async ValueTask<Result<Executed>> ExecuteAsync(SimulationRequest request, CancellationToken cancellationToken)
    {

        // (1) the request, (2) the fill rules, (3) the strategy: no port is read until all three pass.
        var errors = SimulationRequestChecks.Validate(request, out var timeframe);
        if (errors.HasErrors)
        {
            return Result.Failure<Executed>(SimulationErrors.Invalid(errors.Items));
        }

        var fills = BuildFills(request.Fills);
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
        SimulationLog.RunStarted(logger, request.StrategyId, targets.Label, targets.Ids.Count, request.From, request.To, prepared.Value.Warnings.Count, request.Costs is CostChoice.SheetChoice named ? named.SheetId : "gross");
        var calendar = await calendars.GetAsync(cancellationToken);
        var csvThrough = targets.Ids.Count == 0 ? new Dictionary<Guid, DateOnly>() : await actions.ListCsvAdjustedThroughAsync(targets.Ids, cancellationToken);

        var context = new RunContext(request, calendar, csvThrough, targets, request.Options ?? ReplayOptions.Default, costContext);
        var readTime = time.GetElapsedTime(started);
        var replayTime = TimeSpan.Zero;
        var candidates = prepared.Value.Candidates;
        var replays = candidates.Select(_ => new List<InstrumentReplay>(targets.Ids.Count)).ToList();
        var gated = new long[candidates.Count];
        foreach (var id in targets.Ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readStart = time.GetTimestamp();
            var input = await ReadInstrumentAsync(context, id, cancellationToken);
            var replayStart = time.GetTimestamp();
            readTime += time.GetElapsedTime(readStart, replayStart);

            var many = input is null
                ? new ComparisonReplay(candidates.Select(_ => new InstrumentReplay(InstrumentRun.NotFound(id), [], [])).ToList(), candidates.Select(_ => 0L).ToList())
                : candidates.Count == 1 ? new ComparisonReplay([engine.Replay(prepared.Value, input)], [0L]) : engine.ReplayComparison(prepared.Value, input);
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
                (fills.Value ?? FillRules.Default).ToCanonicalJson(),
                universeRecord,
                calendar,
                costContext is null ? null : CostRecord.Of(costContext));
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
    private static Result<FillRules?> BuildFills(FillRulesInput? input)
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

        var rules = FillRules.Create(input.EntryLimitPercent, slippage.Value);
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
    /// Reads one instrument (spec D3, steps 1 to 5) and builds what the engine takes; null when the instrument has no row. The list of bars is local to the call, so only one instrument's bars are held at a time.
    /// </summary>
    private async ValueTask<InstrumentReplayInput?> ReadInstrumentAsync(RunContext context, Guid id, CancellationToken cancellationToken)
    {
        var instrument = context.Targets.Known is { } known
            ? known.GetValueOrDefault(id)
            : await barQueries.FindInstrumentAsync(id, cancellationToken);
        if (instrument is null)
        {
            return null;
        }

        var request = context.Request;
        var raw = await bars.ReadAsync(id, SimulationBounds.From, SimulationBounds.Until(request.To), cancellationToken);
        var factors = await adjustments.GetAsync(id, cancellationToken);

        var tick = TickSpec.FromMasterPaise(instrument.TickSize);
        var twice = !request.AcknowledgeCsvAdjusted && AdjustsCsvTwice(raw, factors, context.CsvThrough, id);
        SkipReason? preset = twice ? SkipReason.CsvAdjustedTwice : tick.IsFailure ? SkipReason.TickSizeInvalid : null;
        var detail = twice
            ? "An applied corporate action falls into the CSV prices, which are already adjusted at source; acknowledge to run it."
                + (tick.IsFailure ? " " + tick.Error.Message : string.Empty)
            : tick.IsFailure ? tick.Error.Message : null;

        // Both skips can apply: the precedence keeps csv_adjusted_twice; nothing is scanned, so the record has no tick and the detail says the master's tick is refused too.
        return new InstrumentReplayInput(
            id,
            instrument.Symbol,
            raw,
            factors,
            context.CsvThrough.TryGetValue(id, out var through) ? through : null,
            context.Calendar,
            context.Options,
            request.From,
            request.To,
            tick.IsSuccess ? tick.Value : null,
            preset,
            detail,
            context.Costs);
    }

    /// <summary>The guard of ADR 0017 §5 in the run: an applied action that changes the bars read and falls on or before the date the loaded CSV files reach would adjust their prices a second time.</summary>
    private static bool AdjustsCsvTwice(IReadOnlyList<Bar> raw, AdjustmentFactors factors, IReadOnlyDictionary<Guid, DateOnly> through, Guid id) =>
        raw.Count > 0
        && through.ContainsKey(id)
        && factors.ActionsAfter(SessionClock.DateOf(raw[0].EpochSeconds)).Any(a => CsvAdjustment.AdjustsTwice(a.ExDate, through, id));

    private sealed record Universe(UniverseKind Kind, Guid? BasketId, string? BasketName, string Label, IReadOnlyList<Guid> Ids, IReadOnlyDictionary<Guid, BarsInstrument>? Known);

    private sealed record RunContext(
        SimulationRequest Request,
        TradingCalendar Calendar,
        IReadOnlyDictionary<Guid, DateOnly> CsvThrough,
        Universe Targets,
        ReplayOptions Options,
        CostContext? Costs);
}
