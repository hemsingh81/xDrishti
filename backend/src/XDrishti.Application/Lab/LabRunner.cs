using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>
/// Carries out a claimed Lab run (spec P5-02, D1; P5-00 D1): the stages validate, compile and quick test in order, each recorded with its start, end and problems, progress reported through one port. The quick test is the host-agnostic
/// <c>RunSimulationHandler</c> with the run kept in the result store afterwards, so the Lab adds no second engine. A run is stored whole or not at all: the replay is in memory until it is complete, a cancel is seen between instruments and once more
/// before the store is written, and after that the store is allowed to finish. Expected failures end the run <c>failed</c> with located problems; an unexpected exception ends it <c>failed</c> with the code <c>lab.run_crashed</c> and is logged
/// (never the text of the strategy); a stop of the host leaves the run <c>running</c> for the next start to mark <c>interrupted</c> (D4).
/// </summary>
internal sealed class LabRunner(
    ILabRunStore runs,
    IStrategyStore strategies,
    IStrategyChecker checker,
    IQueryHandler<RunSimulationQuery, Result<SimulationRun>> simulation,
    ICommandHandler<StoreRunCommand, StoreOutcome> storeRun,
    IRunRecordWriter recordWriter,
    ICostSheetStore sheets,
    ISimulationEngine engine,
    LabFullTest fullTest,
    LabRobustness robustness,
    ITestLedger ledger,
    ILabResultStore results,
    ILabProgress progress,
    LabSettings settings,
    TimeProvider time,
    ILogger<LabRunner> logger,
    LabDeflation deflation,
    IVariantStatsStore variantStats,
    LabCostStress? costStress = null,
    HoldOutOptions? holdOutOptions = null,
    LabHoldOutGuard? holdOutGuard = null,
    HoldOutUnlocker? unlocker = null,
    LabNulls? nulls = null,
    LabPlateau? plateau = null,
    LabTiers? tiers = null,
    LabBenchmark? benchmark = null,
    PboOptions? pboOptions = null,
    CostStressOptions? stressOptions = null) : ILabRunExecutor
{
    /// <summary>Who is recorded as the author of a stored run.</summary>
    public const string Actor = "lab:quick";

    /// <summary>The stage names.</summary>
    public const string Validate = "validate";

    /// <summary>The compile stage.</summary>
    public const string Compile = "compile";

    /// <summary>The quick-test stage.</summary>
    public const string QuickTest = "quick_test";

    /// <summary>The full-test stage (P5-04).</summary>
    public const string FullTest = "full_test";

    /// <summary>The robustness stage of a <c>robust</c> run (P5-05).</summary>
    public const string Robustness = "robustness";

    /// <summary>The random-entry null stage of a robust run (CF-07), after the robustness stage.</summary>
    public const string Nulls = "nulls";

    /// <summary>How often the watcher looks at the run's progress.</summary>
    private static readonly TimeSpan WatchTick = TimeSpan.FromMilliseconds(100);

    /// <summary>How many ticks pass between two looks at the cancel flag in the database.</summary>
    private const int CancelEvery = 5;

    public async Task ExecuteAsync(LabRun run, CancellationToken hostStop)
    {
        ArgumentNullException.ThrowIfNull(run);
        LabRunLog.Started(logger, run.Id, run.Key[..12]);
        var log = new StageLog(time);
        var counter = new InstrumentCounter();
        var watch = new WatchState();
        using var owner = CancellationTokenSource.CreateLinkedTokenSource(hostStop);
        using var watcherStop = new CancellationTokenSource();
        var throttle = new ProgressThrottle(time, ProgressThrottle.QuarterSecond);
        var watcher = WatchAsync(run, counter, watch, owner, throttle, watcherStop.Token);
        LabFinish finish;
        try
        {
            finish = await StagesAsync(run, log, counter, watch, throttle, owner.Token, hostStop);
        }
        catch (OperationCanceledException) when (hostStop.IsCancellationRequested)
        {
            await StopWatcherAsync(watcherStop, watcher);
            return; // the host is stopping: the run stays running and the next start marks it interrupted
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
            LabRunLog.Cancelled(logger, run.Id, log.Current ?? "queued");
            finish = log.End(run.Id, LabRunState.Cancelled, null, [], time.GetUtcNow());
        }
#pragma warning disable CA1031 // an unexpected failure of a run is recorded on the run; the host goes on
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LabRunLog.Crashed(logger, run.Id, log.Current ?? "claimed", ex);
            finish = log.End(run.Id, LabRunState.Failed, "lab.run_crashed", [], time.GetUtcNow());
        }

        await StopWatcherAsync(watcherStop, watcher);
        await runs.FinishAsync(finish, CancellationToken.None);
        if (finish.State == LabRunState.Succeeded && LabSettings.RunsFullTest(run.Method))
        {
            // the unlock reloads the run and needs it stored as succeeded, so it follows the finish
            await TryAutoUnlockAsync(run, hostStop);
        }

        await progress.FinishedAsync(new LabRunFinished(run.Id, LabRunStates.Code(finish.State)), CancellationToken.None);
    }

    private static async Task StopWatcherAsync(CancellationTokenSource stop, Task watcher)
    {
        await stop.CancelAsync();
        await watcher;
    }

    private async Task<LabFinish> StagesAsync(LabRun run, StageLog log, InstrumentCounter counter, WatchState watch, ProgressThrottle throttle, CancellationToken ownerToken, CancellationToken hostStop)
    {
        var started = time.GetTimestamp();

        // validate: the stored version and the universe are still what the post resolved
        await BeginAsync(run, log, Validate, 0, 0, throttle, ownerToken);
        var version = await strategies.GetByHashAsync(run.StrategyHash, ownerToken);
        var problems = new List<LabProblem>();
        if (version is null)
        {
            problems.Add(new LabProblem(Validate, "strategy", "lab.strategy_missing", "The stored version of the strategy is not found.", "error", null, null));
        }

        if (run.Universe.Instruments.Count == 0)
        {
            problems.Add(new LabProblem(Validate, "universe", "lab.empty_universe", "The universe has no instrument.", "error", null, null));
        }

        if (LabRunOptions.Parse(run.RunOptions).Unknown is { } unknownOption)
        {
            problems.Add(new LabProblem(Validate, "options", "lab.run_options_unknown", $"The stored run options contain `{unknownOption}`, which this version does not know; the run is not guessed at.", "error", null, null));
        }

        if (LabRunOptions.Parse(run.RunOptions).Stress == false && string.Equals(run.Method, LabSettings.RobustMethod, StringComparison.Ordinal) && costStress is { IsOn: true, Enforce: true })
        {
            // queued before the owner turned Enforce on: running it would skip the cap
            problems.Add(new LabProblem(Validate, "options.stress", "lab.stress_off_enforced", "This robust run was posted with `stress: false`, which is refused while the cost stress is enforced; post it again.", "error", null, null));
        }

        log.Finish(Validate, problems.Count, time.GetUtcNow());
        if (problems.Count > 0 || version is null)
        {
            return Failed(run, log, Validate, "lab.validate_failed", problems);
        }

        // compile: the same read and compile as the validate endpoint, under today's rules
        ownerToken.ThrowIfCancellationRequested();
        await BeginAsync(run, log, Compile, 0, 0, throttle, ownerToken);
        var check = checker.Check(version.SourceText);
        problems.AddRange(check.Issues.Select(i => new LabProblem(Compile, i.Path, i.Code, i.Message, i.Severity, i.Line, i.Column)));
        log.Finish(Compile, check.Issues.Count(i => i.Severity == "error"), time.GetUtcNow());
        if (check.Description is not { } description)
        {
            return Failed(run, log, Compile, "lab.compile_failed", problems);
        }

        ownerToken.ThrowIfCancellationRequested();
        if (LabSettings.RunsFullTest(run.Method))
        {
            return await FullAsync(run, description, log, counter, watch, throttle, problems, started, ownerToken, hostStop);
        }

        // quick test
        var total = run.Universe.Instruments.Count;
        watch.Set(QuickTest, total);
        await BeginAsync(run, log, QuickTest, 0, total, throttle, ownerToken);
        var (request, _) = await RequestAsync(run, description, ownerToken);
        var result = await simulation.HandleAsync(new RunSimulationQuery(request, counter), ownerToken);
        if (result.IsFailure)
        {
            return FailedStage(run, log, QuickTest, result.Error, problems);
        }

        var simulated = result.Value;
        var record = recordWriter.Write(simulated);

        // the last safe point: a cancel seen here stores nothing; after the write has begun it is allowed to finish
        ownerToken.ThrowIfCancellationRequested();
        if (await runs.IsCancelRequestedAsync(run.Id, hostStop))
        {
            LabRunLog.Cancelled(logger, run.Id, QuickTest);
            return log.End(run.Id, LabRunState.Cancelled, null, problems, time.GetUtcNow());
        }

        var stored = await storeRun.HandleAsync(new StoreRunCommand(simulated, record, Actor), hostStop);
        if (stored.IsFailure)
        {
            problems.Add(new LabProblem(QuickTest, string.Empty, stored.Error.Code, stored.Error.Message, "error", null, null));
            log.Finish(QuickTest, 1, time.GetUtcNow());
            return Failed(run, log, QuickTest, stored.Error.Code, problems);
        }

        log.Finish(QuickTest, problems.Count(p => p.Stage == QuickTest), time.GetUtcNow());
        var counts = new LabCounts(simulated.Totals.Instruments, simulated.Totals.InstrumentsRun, simulated.Totals.Instruments - simulated.Totals.InstrumentsRun, simulated.Trades.Count, simulated.OpenAtEnd.Count);
        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        LabRunLog.Finished(logger, run.Id, simulated.RunKey[..12], simulated.Trades.Count, simulated.OpenAtEnd.Count, elapsedMs);
        return new LabFinish(run.Id, LabRunState.Succeeded, QuickTest, [simulated.RunKey], counts, problems, log.Snapshot(), null, time.GetUtcNow());
    }

    private LabFinish FailedStage(LabRun run, StageLog log, string stage, Error error, List<LabProblem> problems)
    {
        problems.AddRange((error.Fields ?? []).Select(f => new LabProblem(stage, f.Field, f.Code ?? error.Code, f.Message, "error", null, null)));
        if (error.Fields is null or { Count: 0 })
        {
            problems.Add(new LabProblem(stage, string.Empty, error.Code, error.Message, "error", null, null));
        }

        log.Finish(stage, problems.Count(p => p.Stage == stage), time.GetUtcNow());
        return Failed(run, log, stage, error.Code, problems);
    }

    /// <summary>The full test (spec P5-04): the walk-forward over timeframe, side and exit, the runs stored whole, the verdict stored, then the run ends.</summary>
    private async Task<LabFinish> FullAsync(LabRun run, StrategyDescription description, StageLog log, InstrumentCounter counter, WatchState watch, ProgressThrottle throttle, List<LabProblem> problems, long started, CancellationToken ownerToken, CancellationToken hostStop)
    {
        var total = run.Universe.Instruments.Count * Math.Max(1, description.Timeframes.Count);
        watch.Set(FullTest, total);
        await BeginAsync(run, log, FullTest, 0, total, throttle, ownerToken);
        var (template, sheetUsed) = await RequestAsync(run, description, ownerToken);
        if (template.Costs is not CostChoice.SheetChoice)
        {
            return FailedStage(run, log, FullTest, Error.Validation("lab.costs_required", "A full test is costed: the charge sheet is not stored or not in force on the first day of the window."), problems);
        }

        var outcome = await fullTest.RunAsync(run, description, template, counter, ownerToken);
        if (outcome.IsFailure)
        {
            return FailedStage(run, log, FullTest, outcome.Error, problems);
        }

        var full = outcome.Value;
        var verdict = full.Verdict;
        var sweepRows = new List<LedgerRow>();
        var oosR = full.OosTrades.Select(t => t.R).ToList();
        var statsRows = new List<VariantStatsRow> { LabVariantStats.Of(run.StrategyId, run.Id, run.StrategyHash, TradeMoments.Of(oosR), run.Method, time.GetUtcNow()) };
        var lastStage = FullTest;
        DelaySignals? delay = null;
        NullsOutcome? nullsOutcome = null;
        PlateauView? plateauView = null;
        if (string.Equals(run.Method, LabSettings.RobustMethod, StringComparison.Ordinal))
        {
            ownerToken.ThrowIfCancellationRequested();
            log.Finish(FullTest, 0, time.GetUtcNow());
            var plan = robustness.Plan(description);
            var steps = run.Universe.Instruments.Count * (1 + plan.Points.Count + robustness.ExtraPasses);
            watch.Set(Robustness, steps);
            await BeginAsync(run, log, Robustness, 0, steps, throttle, ownerToken);
            var robust = await robustness.RunAsync(run, time.GetUtcNow(), plan, template, full, counter, ownerToken);
            if (robust.IsFailure)
            {
                return FailedStage(run, log, Robustness, robust.Error, problems);
            }

            verdict = verdict with { Robustness = robust.Value.View };
            sweepRows.AddRange(robust.Value.LedgerRows);
            statsRows.AddRange(robust.Value.VariantStats);
            delay = robust.Value.Delay;
            lastStage = Robustness;

            // CF-07: the random-entry null of the out-of-sample trades, one more stage that reads the stored bars of the development window and writes nothing until the end
            if (nulls is { IsOn: true })
            {
                ownerToken.ThrowIfCancellationRequested();
                log.Finish(Robustness, 0, time.GetUtcNow());
                var (developmentEnd, holdOutKept) = await DevelopmentEndAsync(run, ownerToken);
                var nullSteps = nulls.Steps(full, developmentEnd);
                watch.Set(Nulls, nullSteps);
                await BeginAsync(run, log, Nulls, 0, nullSteps, throttle, ownerToken);
                nullsOutcome = await nulls.RunAsync(run, time.GetUtcNow(), template, full, developmentEnd, holdOutKept, counter, ownerToken);

                // CF-07 part C: the plateau gate reads the sweep just stored in the robustness block (no simulation, no ledger row), and travels in the same block
                if (plateau is { IsOn: true })
                {
                    plateauView = plateau.Read(description.Parameters, plan.Parameters, robust.Value.View.Sensitivity, developmentEnd, template.To);
                }

                lastStage = Nulls;
            }
        }

        // CF-05: the cost stress and the delay test beside the outcome; reads one stored sheet, writes nothing, and the outcome moves only if the owner has turned Enforce on
        // Robust runs only: a plain full run keeps the document it always had (CF-05 criterion 7); the sheet is the one the run was costed with, read once at the start
        var runOptions = LabRunOptions.Parse(run.RunOptions);
        if (runOptions.Stress == true && !(costStress is { IsOn: true } && sheetUsed is not null))
        {
            // asked for, not done: said in the run's problems, never silently dropped
            problems.Add(new LabProblem(FullTest, "options.stress", "lab.stress_not_run", costStress is not { IsOn: true } ? "`stress: true` was asked for but the cost stress is Off (Confidence:CostStress:Mode), so no stress block was computed." : "`stress: true` was asked for but the run was not costed with a stored charge sheet, so no stress block was computed.", "warning", null, null));
        }

        if (costStress is { IsOn: true } && runOptions.StressOn(string.Equals(run.Method, LabSettings.RobustMethod, StringComparison.Ordinal)) && sheetUsed is not null)
        {
            // ADR 0223: the liquidity tier and the capacity check of the same trades, from day bars; carried on the block, read by no gate
            var tierCounts = tiers is null ? null : await tiers.ComputeAsync(full.OosSources, ownerToken);
            verdict = costStress.Apply(verdict, full.OosTrades, sheetUsed.Id, sheetUsed.Checked is not null, delay, sheetUsed.RatesSha256, tierCounts);
        }

        // CF-07: the random-entry null beside the outcome; the outcome moves only if the owner has turned Enforce on, and the family-level nulls (the random-entry null and the placebo set) are one more look each in the ledger (written with the sweep rows below)
        if (nullsOutcome is not null && nulls is not null)
        {
            verdict = nulls.Apply(verdict, plateauView is null ? nullsOutcome.View : nullsOutcome.View with { Plateau = plateauView });
            verdict = plateau?.Apply(verdict) ?? verdict;
            sweepRows.AddRange(nullsOutcome.Rows);
        }

        // CF-11 row 13: the daily Sharpe of the out-of-sample net R against the buy-and-hold of the universe; report-only, read by the scorecard alone
        if (benchmark is not null)
        {
            verdict = verdict with { Benchmark = await benchmark.ComputeAsync(run.Universe, full.OosTrades, ownerToken) };
        }

        // CF-03 part B: the PBO gate. Only with the Gate mode (config, tighten-only: it can only cap a PASS at MARGINAL) does a PASS above the limit become MARGINAL (with the reason); ReportOnly and Off return the verdict as it came, and an unavailable PBO never moves it
        verdict = LabPbo.Apply(pboOptions ?? new PboOptions(), verdict);

        // CF-03: beside the factor; reads the stored trial statistics and writes nothing, and the outcome is not touched
        verdict = verdict with { DeflatedSharpe = await deflation.ComputeAsync(run.StrategyId, verdict, oosR, statsRows, ownerToken) };

        // CF-06: with hold-out enforcement on, a PASS with no hold-out kept (history too short) is capped at MARGINAL and says so; with it off nothing changes
        if (holdOutOptions is { Enforce: true } && holdOutGuard is not null && verdict.Outcome == "PASS" && !await HoldOutKeptAsync(holdOutGuard, ownerToken))
        {
            verdict = verdict with { Outcome = "MARGINAL", Limits = [.. verdict.Limits, "no hold-out is kept (the stored history is too short): capped at MARGINAL"] };
        }

        ownerToken.ThrowIfCancellationRequested();
        if (await runs.IsCancelRequestedAsync(run.Id, hostStop))
        {
            LabRunLog.Cancelled(logger, run.Id, lastStage);
            return log.End(run.Id, LabRunState.Cancelled, null, problems, time.GetUtcNow());
        }

        var keys = new List<string>();
        foreach (var simulated in full.Runs)
        {
            var stored = await storeRun.HandleAsync(new StoreRunCommand(simulated, recordWriter.Write(simulated), "lab:full"), hostStop);
            if (stored.IsFailure)
            {
                return FailedStage(run, log, FullTest, stored.Error, problems);
            }

            keys.Add(simulated.RunKey);
        }

        // shortcut: a failure after the candidate runs are stored leaves them stored (idempotent by key, a retry re-stores nothing); cancel is not rechecked after the store loop. A retry after a crash between the sweep rows and the result can see up to 16 more variants (the rows of the first attempt count).
        foreach (var row in sweepRows)
        {
            await ledger.AppendAsync(row, hostStop);
        }

        await variantStats.AppendAsync(statsRows, hostStop);

        if (!await results.AppendAsync(run.Id, run.StrategyId, run.StrategyHash, verdict, time.GetUtcNow(), hostStop)
            && await results.GetAsync(run.Id, hostStop) is null)
        {
            return FailedStage(run, log, FullTest, Error.Conflict("lab.result_not_stored", "The result of the full test could not be stored."), problems);
        }

        LabRunLog.ResultStored(logger, run.Id, verdict.Outcome, full.Verdict.VariantCount);
        log.Finish(lastStage, 0, time.GetUtcNow());
        var counts = new LabCounts(full.Instruments, full.InstrumentsRun, full.Instruments - full.InstrumentsRun, full.Trades, 0);
        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        LabRunLog.Finished(logger, run.Id, keys[0][..12], full.Trades, 0, elapsedMs);
        return new LabFinish(run.Id, LabRunState.Succeeded, lastStage, keys, counts, problems, log.Snapshot(), null, time.GetUtcNow());
    }

    /// <summary>The last session the random-entry null and the placebo set may read (CF-07, ADR 0208), and whether a hold-out block is kept: the run's own end, or the day before the hold-out block when the run's window reaches it. <see cref="LabHoldOutGuard.Cut"/> shortens only a default window, so an explicit window can end inside the block; the nulls never follow it there.</summary>
    private async ValueTask<(DateOnly End, bool Kept)> DevelopmentEndAsync(LabRun run, CancellationToken cancellationToken)
    {
        if (holdOutGuard is null || await holdOutGuard.HistoryAsync(cancellationToken) is not { } history || await holdOutGuard.EnsureAsync(history.First, history.Last, cancellationToken) is not { } block)
        {
            return (run.To, false);
        }

        var before = block.Start.AddDays(-1);
        return (before < run.To ? before : run.To, true);
    }

    private static async Task<bool> HoldOutKeptAsync(LabHoldOutGuard guard, CancellationToken cancellationToken) =>
        await guard.HistoryAsync(cancellationToken) is { } history && await guard.EnsureAsync(history.First, history.Last, cancellationToken) is not null;

    /// <summary>The auto option (CF-06, D6): off unless the owner turned it on; the run and its result are already stored, so a failure here is logged and never fails the run.</summary>
    private async Task TryAutoUnlockAsync(LabRun run, CancellationToken cancellationToken)
    {
        if (unlocker is null)
        {
            return;
        }

#pragma warning disable CA1031 // the result is stored; an auto unlock that fails is tried again by nobody and must not undo the run
        try
        {
            if (await results.GetAsync(run.Id, cancellationToken) is { } verdict && await unlocker.TryAutoAsync(run.Id, verdict, cancellationToken) is { } done)
            {
                LabRunLog.HoldOutUnlocked(logger, run.Id, done.Mode, done.State);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LabRunLog.Crashed(logger, run.Id, "auto_unlock", ex);
        }
#pragma warning restore CA1031
    }

    private LabFinish Failed(LabRun run, StageLog log, string stage, string code, List<LabProblem> problems)
    {
        LabRunLog.Failed(logger, run.Id, stage, code, problems.Count);
        return new LabFinish(run.Id, LabRunState.Failed, stage, [], null, problems, log.Snapshot(), code, time.GetUtcNow());
    }

    private async ValueTask BeginAsync(LabRun run, StageLog log, string stage, int done, int total, ProgressThrottle throttle, CancellationToken cancellationToken)
    {
        log.Begin(stage, time.GetUtcNow());
        LabRunLog.Stage(logger, run.Id, stage, 0);
        await runs.ReportAsync(run.Id, stage, done, total, log.Snapshot(), time.GetUtcNow(), cancellationToken);
        if (throttle.TryPass())
        {
            await progress.ProgressAsync(new LabRunProgress(run.Id, LabRunStates.Code(LabRunState.Running), stage, done, total, null), cancellationToken);
        }
    }

    /// <summary>The request of the quick test: the stored version by hash, its default timeframe (as the verb), the modelled fills (as the verb), the stored sheet if it is in force on the first day, otherwise gross.</summary>
    private async ValueTask<(SimulationRequest Request, CostSheet? Sheet)> RequestAsync(LabRun run, StrategyDescription description, CancellationToken cancellationToken)
    {
        var sheet = await sheets.FindAsync(settings.CostSheetId, cancellationToken);
        var costs = sheet.IsSuccess && sheet.Value.Schedules[0].EffectiveFrom <= run.From ? CostChoice.Sheet(sheet.Value.Id) : CostChoice.Gross;
        var fills = LabFills.For(run, stressOptions);
        var timeframe = DefaultTimeframe(run.StrategyId, description);
        var request = new SimulationRequest(run.StrategyId, new Dictionary<string, double>(), timeframe.ToCode(), run.From, run.To, [.. run.Universe.Instruments.Select(i => i.Id)], null, costs, false, null, fills, null, null, run.StrategyHash);
        return (request, costs is CostChoice.SheetChoice && sheet.IsSuccess ? sheet.Value : null);
    }

    /// <summary>The timeframe the verb would use without <c>--timeframe</c>: the built-in's default, else 15m when the strategy declares it, else the first it declares.</summary>
    private Timeframe DefaultTimeframe(string strategyId, StrategyDescription description) =>
        engine.BuiltIns.FirstOrDefault(b => string.Equals(b.Id, strategyId, StringComparison.Ordinal))?.DefaultTimeframe
        ?? (description.Timeframes.Contains(Timeframe.M15) || description.Timeframes.Count == 0 ? Timeframe.M15 : description.Timeframes[0]);

    /// <summary>Looks at the run every tick: asks the database for a cancel now and then, and sends the instruments done when they changed and the throttle lets it.</summary>
    private async Task WatchAsync(LabRun run, InstrumentCounter counter, WatchState watch, CancellationTokenSource owner, ProgressThrottle throttle, CancellationToken stop)
    {
        var lastSent = 0;
        var tick = 0;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(WatchTick, time, stop);
                if (++tick % CancelEvery == 0 && await runs.IsCancelRequestedAsync(run.Id, stop))
                {
                    await owner.CancelAsync();
                    return;
                }

                var done = counter.Done;
                if (done != lastSent && throttle.TryPass())
                {
                    lastSent = done;
                    await runs.ReportAsync(run.Id, watch.Stage, done, watch.Total, [], time.GetUtcNow(), stop);
                    await progress.ProgressAsync(new LabRunProgress(run.Id, LabRunStates.Code(LabRunState.Running), watch.Stage, done, watch.Total, null), stop);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // the run is over
        }
#pragma warning disable CA1031 // progress is a convenience: a failed write of it must not end the run
        catch (Exception)
#pragma warning restore CA1031
        {
            // the run's own end writes the final state
        }
    }

    /// <summary>The stage the watcher reports and the total it counts towards; set by the stage that begins.</summary>
    private sealed class WatchState
    {
        private volatile string _stage = QuickTest;
        private int _total;

        public string Stage => _stage;

        public int Total => Volatile.Read(ref _total);

        public void Set(string stage, int total)
        {
            Volatile.Write(ref _total, total);
            _stage = stage;
        }
    }

    /// <summary>The number of instruments the replay has finished; written by the handler, read by the watcher.</summary>
    private sealed class InstrumentCounter : IProgress<int>
    {
        private int _done;

        public int Done => Volatile.Read(ref _done);

        public void Report(int value) => Volatile.Write(ref _done, value);
    }

    /// <summary>The stage log of one run: stages in order, each with its start, end and problems.</summary>
    private sealed class StageLog(TimeProvider time)
    {
        private readonly List<LabStageLog> _stages = [];

        public string? Current { get; private set; }

        public void Begin(string stage, DateTimeOffset now)
        {
            Current = stage;
            _stages.Add(new LabStageLog(stage, now, null, 0));
        }

        public void Finish(string stage, int problems, DateTimeOffset now)
        {
            var index = _stages.FindLastIndex(s => s.Name == stage);
            _stages[index] = _stages[index] with { FinishedAt = now, Problems = problems };
        }

        public IReadOnlyList<LabStageLog> Snapshot() => [.. _stages];

        public LabFinish End(Guid id, LabRunState state, string? error, IReadOnlyList<LabProblem> problems, DateTimeOffset now)
        {
            if (Current is { } current && _stages.FindLast(s => s.Name == current) is { FinishedAt: null })
            {
                Finish(current, 0, time.GetUtcNow());
            }

            return new LabFinish(id, state, Current, [], null, problems, Snapshot(), error, now);
        }
    }
}
