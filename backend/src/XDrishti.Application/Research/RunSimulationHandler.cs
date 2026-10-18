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
    TimeProvider time,
    ILogger<RunSimulationHandler> logger) : IQueryHandler<RunSimulationQuery, Result<SimulationRun>>
{
    private static readonly DateOnly FirstDay = new(SimulationRequestChecks.FirstYear, 1, 1);

    /// <summary>The last minute of the India day (bars start on whole minutes, so 23:59 is the last minute that can hold one).</summary>
    private static readonly TimeSpan LastMinuteOfDay = new(23, 59, 0);

    public async ValueTask<Result<SimulationRun>> HandleAsync(RunSimulationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var request = query.Request;

        // (1) the request, (2) the fill rules, (3) the strategy: no port is read until all three pass.
        var errors = SimulationRequestChecks.Validate(request, out var timeframe);
        if (errors.HasErrors)
        {
            return Result.Failure<SimulationRun>(SimulationErrors.Invalid(errors.Items));
        }

        var fills = BuildFills(request.Fills);
        if (fills.IsFailure)
        {
            return Result.Failure<SimulationRun>(fills.Error);
        }

        var prepared = engine.Prepare(request.StrategyId, request.Parameters ?? new Dictionary<string, double>(), timeframe, fills.Value);
        if (prepared.IsFailure)
        {
            // The port contract already caps the list at 20; this is a guard for an engine that does not.
            return Result.Failure<SimulationRun>(prepared.Error.Fields is { Count: > SimulationErrors.MaxStrategyProblems } fields ? SimulationErrors.StrategyInvalid(fields) : prepared.Error);
        }

        // (4) only then the ports.
        var started = time.GetTimestamp();
        var universe = await ResolveUniverseAsync(request, cancellationToken);
        if (universe.IsFailure)
        {
            return Result.Failure<SimulationRun>(universe.Error);
        }

        var targets = universe.Value;
        SimulationLog.RunStarted(logger, request.StrategyId, targets.Label, targets.Ids.Count, request.From, request.To, prepared.Value.Warnings.Count);
        var calendar = await calendars.GetAsync(cancellationToken);
        var csvThrough = targets.Ids.Count == 0 ? new Dictionary<Guid, DateOnly>() : await actions.ListCsvAdjustedThroughAsync(targets.Ids, cancellationToken);

        var context = new RunContext(request, calendar, csvThrough, targets, request.Options ?? ReplayOptions.Default);
        var readTime = time.GetElapsedTime(started);
        var replayTime = TimeSpan.Zero;
        var replays = new List<InstrumentReplay>(targets.Ids.Count);
        foreach (var id in targets.Ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readStart = time.GetTimestamp();
            var input = await ReadInstrumentAsync(context, id, cancellationToken);
            var replayStart = time.GetTimestamp();
            readTime += time.GetElapsedTime(readStart, replayStart);

            var replay = input is null ? new InstrumentReplay(InstrumentRun.NotFound(id), [], []) : engine.Replay(prepared.Value, input);
            replayTime += time.GetElapsedTime(replayStart);
            if (replay.Record.Skipped is { } reason)
            {
                var code = reason.ToCode();
                SimulationLog.InstrumentSkipped(logger, replay.Record.Symbol, replay.Record.InstrumentId, code);
            }

            replays.Add(replay);
        }

        var assemblyStart = time.GetTimestamp();
        var members = replays.Select(r => new UniverseMember(r.Record.InstrumentId, r.Record.Symbol)).ToList();
        var header = new RunHeader(
            prepared.Value.Record,
            engine.EngineVersion,
            request.From,
            request.To,
            context.Options,
            request.AcknowledgeCsvAdjusted,
            (fills.Value ?? FillRules.Default).ToCanonicalJson(),
            new UniverseRecord(targets.Kind, targets.BasketId, targets.BasketName, targets.Label, members),
            calendar);
        var run = RunAssembler.Assemble(header, replays);
        var skipped = replays.Count(r => r.Record.Skipped is not null);
        var readMs = (long)readTime.TotalMilliseconds;
        var replayMs = (long)replayTime.TotalMilliseconds;
        var assemblyMs = (long)time.GetElapsedTime(assemblyStart).TotalMilliseconds;
        SimulationLog.RunFinished(logger, run.RunKey, replays.Count - skipped, skipped, run.Trades.Count, readMs, replayMs, assemblyMs);
        return Result.Success(run);
    }

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
        var raw = await bars.ReadAsync(id, IndiaInstant(FirstDay, TimeSpan.Zero), IndiaInstant(request.To, LastMinuteOfDay), cancellationToken);
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
            detail);
    }

    /// <summary>The guard of ADR 0017 §5 in the run: an applied action that changes the bars read and falls on or before the date the loaded CSV files reach would adjust their prices a second time.</summary>
    private static bool AdjustsCsvTwice(IReadOnlyList<Bar> raw, AdjustmentFactors factors, IReadOnlyDictionary<Guid, DateOnly> through, Guid id) =>
        raw.Count > 0
        && through.ContainsKey(id)
        && factors.ActionsAfter(SessionClock.DateOf(raw[0].EpochSeconds)).Any(a => CsvAdjustment.AdjustsTwice(a.ExDate, through, id));

    /// <summary>An India wall-clock time as an instant with offset 0: Npgsql writes a <c>DateTimeOffset</c> to <c>timestamptz</c> only with offset 0 and throws for +05:30.</summary>
    private static DateTimeOffset IndiaInstant(DateOnly date, TimeSpan timeOfDay) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, timeOfDay.Hours, timeOfDay.Minutes, 0, IndiaTime.Offset).ToUniversalTime();

    private sealed record Universe(UniverseKind Kind, Guid? BasketId, string? BasketName, string Label, IReadOnlyList<Guid> Ids, IReadOnlyDictionary<Guid, BarsInstrument>? Known);

    private sealed record RunContext(
        SimulationRequest Request,
        TradingCalendar Calendar,
        IReadOnlyDictionary<Guid, DateOnly> CsvThrough,
        Universe Targets,
        ReplayOptions Options);
}
