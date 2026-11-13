using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Lab;
using XDrishti.Application.Learning.CellsRefresh;
using XDrishti.Application.Learning.SimTraining;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.History;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning;

/// <summary>One strategy version of the model's training set with the request that replays it: the join keys of its cells and the request (its window is set when the block is read).</summary>
internal sealed record PlannedRun(string Hash, string StrategyId, string Style, string Exit, SimulationRequest Request);

/// <summary>What the model's pass needs before the block is touched: the loaded scorer, the cell statistics known before the block, the base rate read from them and the planned replays. Nothing in it was read from the block.</summary>
internal sealed record PreparedModelHoldOut(StoredModel Model, IExplainableScorer Scorer, IReadOnlyDictionary<string, CellStatsRow> Cells, double BaseRate, IReadOnlyList<PlannedRun> Runs);

/// <summary>What the model's pass over the block produced: the scored rows, the base rate read before the block, a hash of the rows and how many candidates could not be scored (no feature or no cell statistics).</summary>
internal sealed record ModelHoldOutRows(IReadOnlyList<ModelHoldOutRow> Rows, double BaseRate, string InputHash, int Dropped);

/// <summary>The pass of a model's strategy versions over the hold-out block, in two steps (an interface so the scoring can be tested without an engine): <see cref="PrepareAsync"/> reads no bar and no session of the block and runs before the look is claimed, so an input that is missing refuses the act before anything is burned.</summary>
internal interface IModelHoldOutPass
{
    ValueTask<Result<PreparedModelHoldOut>> PrepareAsync(StoredModel model, DateOnly blockStart, CancellationToken cancellationToken);

    ValueTask<Result<ModelHoldOutRows>> RunAsync(PreparedModelHoldOut prepared, HoldOutBlock block, CancellationToken cancellationToken);
}

/// <summary>
/// The second reader of the sessions inside the locked hold-out (CF-10e part 3, ADR 0285; the first is <c>HoldOutPass</c>). <see cref="PrepareAsync"/> proves every input before the look is claimed: the catalog version, the strategy versions of the model's training set (<c>sim-all:{job}</c> through the job's sweeps, or
/// <c>runKey:style:exit</c> through the stored run and the LC-01 request planner, which rebuilds the exact request that made the run), the cell statistics known before the block (for a simulation-trained model rebuilt the way its training built them, from the job's closed trades that ended before the block, through
/// <see cref="SimTrainingCells"/>; for a run-trained model the newest stored cash snapshot before the block), the model file and the instruments of the baskets as of the block's start. <see cref="RunAsync"/> replays the versions once over the block through the same <c>RunSimulationQuery</c>, extracts the decision-time features
/// from bars before each decision only (the P6-04 extractor; no bar after the block's end is asked for) and joins the cell statistics. A candidate with no features or cell is dropped and counted, never imputed. A win is a realised R above zero. Reads, never writes; places no order.
/// </summary>
internal sealed class ModelHoldOutPass(
    ITrainingSetSources trainingSets,
    ISimAllStore jobs,
    ITrainingRunReader trainingRuns,
    SimTrainingSettings trainingSettings,
    IRunStore runs,
    ISourceRequestPlanner planner,
    IStrategyStore strategies,
    IStrategyChecker checker,
    ISimulationEngine engine,
    ICostSheetStore sheets,
    LabSettings labSettings,
    IQueryHandler<RunSimulationQuery, Result<SimulationRun>> simulation,
    IBasketQueries baskets,
    IBarStore bars,
    IBackfillSeams seams,
    IAdjustmentFactors adjustments,
    ITradingCalendar calendars,
    ITradeFeatureExtractor extractor,
    ICellStatsStore cells,
    IModelFiles files,
    IModelScorerLoader loader) : IModelHoldOutPass
{
    private const string SimAllPrefix = "sim-all:";
    private const int LookbackDays = 730;

    private sealed record Candidate(string Key, Guid InstrumentId, Timeframe Timeframe, Side Side, DateOnly Day, long DecisionEpoch, double Stop, double? Target, double R, string CellKey);

    public async ValueTask<Result<PreparedModelHoldOut>> PrepareAsync(StoredModel model, DateOnly blockStart, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Model.FeatureVersion != TradeFeatureCatalog.Version || model.Model.FeatureWidth != TradeFeatureCatalog.All.Count)
        {
            return Unavailable("The model was made in another feature catalog version than the one this system builds now.");
        }

        var sources = await trainingSets.SourcesAsync(model.Model.TrainingSetId, cancellationToken);
        if (string.IsNullOrWhiteSpace(sources))
        {
            return Unavailable("The training set of the model is not stored.");
        }

        var instruments = await InstrumentsAsync(blockStart, cancellationToken);
        if (instruments.Count == 0)
        {
            return Unavailable("No instrument is in the stored baskets.");
        }

        IReadOnlyList<PlannedRun> planned;
        IReadOnlyList<CellStatsRow> statsRows;
        if (sources.StartsWith(SimAllPrefix, StringComparison.Ordinal))
        {
            var simulated = await PlanSimulationAsync(sources[SimAllPrefix.Length..], instruments, blockStart, cancellationToken);
            if (simulated.IsFailure)
            {
                return Result.Failure<PreparedModelHoldOut>(simulated.Error);
            }

            (planned, statsRows) = simulated.Value;
        }
        else
        {
            var replayed = await PlanRunsAsync(sources, blockStart, cancellationToken);
            if (replayed.IsFailure)
            {
                return Result.Failure<PreparedModelHoldOut>(replayed.Error);
            }

            planned = replayed.Value;
            statsRows = [.. CashCells.Only(await cells.ListLatestBeforeAsync(blockStart, null, CellWindow.All.Code(), cancellationToken), "model_holdout", null).Select(r => r.Row)];
        }

        var all = statsRows.Where(r => r.Window == CellWindow.All && r.Cell.IsCash).ToList();
        var total = all.Sum(r => r.N);
        if (planned.Count == 0 || all.Count == 0 || total <= 0)
        {
            return Unavailable("No cell statistics are known before the block, so the baselines and the cell features cannot be read.");
        }

        var stats = all.GroupBy(r => r.Cell.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (!planned.Any(p => stats.Keys.Any(k => string.Equals(CellId.TryParse(k)?.Strategy, p.StrategyId, StringComparison.Ordinal))))
        {
            return Unavailable("No cell known before the block belongs to a strategy version of the model's training set.");
        }

        var baseRate = Math.Clamp(all.Sum(r => r.WinRatePosterior * r.N) / total, 1e-6, 1 - 1e-6);
        var bytes = await files.ReadAsync(model.Model.Sha256, cancellationToken);
        var scorer = bytes.IsSuccess ? loader.Load(bytes.Value, model.Model.Sha256) : null;
        return scorer is not { IsSuccess: true }
            ? Unavailable("The model file cannot be loaded.")
            : Result.Success(new PreparedModelHoldOut(model, scorer.Value, stats, baseRate, planned));
    }

    public async ValueTask<Result<ModelHoldOutRows>> RunAsync(PreparedModelHoldOut prepared, HoldOutBlock block, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(block);
        var simulated = await SimulateAsync(prepared.Runs, block, cancellationToken);
        if (simulated.IsFailure)
        {
            return Result.Failure<ModelHoldOutRows>(simulated.Error);
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        var rows = new List<ModelHoldOutRow>();
        var dropped = 0;
        foreach (var group in simulated.Value.GroupBy(c => c.InstrumentId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decisions = group.Select(c => new TradeFeatureDecision(c.Key, c.Timeframe, c.DecisionEpoch, c.Side, c.Stop, c.Target)).ToList();
            var first = DateTimeOffset.FromUnixTimeSeconds(group.Min(c => c.DecisionEpoch)).AddDays(-LookbackDays);
            var until = DateTimeOffset.FromUnixTimeSeconds(group.Max(c => c.DecisionEpoch)); // never later than the block's last session: the trades are decided inside the block

            // the same shortcut as the shadow scorer: a seam inside the window gives no score, so serving never differs from training
            if (await seams.SeamOfAsync(group.Key, cancellationToken) is { } seam && seam > first)
            {
                dropped += decisions.Count;
                continue;
            }

            var raw = await SeamAwareBars.ReadAsync(bars, seams, group.Key, first, until, cancellationToken);
            if (raw.Count == 0)
            {
                dropped += decisions.Count;
                continue;
            }

            var values = extractor.Extract(raw, await adjustments.GetAsync(group.Key, cancellationToken), calendar, decisions);
            foreach (var c in group)
            {
                if (!values.TryGetValue(c.Key, out var stored) || stored.Length != TradeFeatureCatalog.StoredCount || !prepared.Cells.TryGetValue(c.CellKey, out var cell))
                {
                    dropped++;
                    continue;
                }

                var vector = new double[TradeFeatureCatalog.All.Count];
                stored.CopyTo(vector, 0);
                new[] { cell.N, cell.WinRatePosterior, cell.WinRateLower, cell.ExpectancyR ?? double.NaN, cell.Enough ? 1 : 0 }.CopyTo(vector, TradeFeatureCatalog.StoredCount);
                if (TradeFeatureCatalog.DefaultRequired.Any(n => !double.IsFinite(vector[TradeFeatureCatalog.IndexOf(n)])))
                {
                    dropped++;
                    continue;
                }

                var p = prepared.Scorer.Score(new ValidationRow("score", 0, 0, default, 0, 0, string.Empty, [.. vector]));
                if (!double.IsFinite(p) || p is < 0 or > 1 || !(cell.WinRatePosterior > 0 && cell.WinRatePosterior < 1))
                {
                    dropped++;
                    continue;
                }

                rows.Add(new ModelHoldOutRow(c.Key, c.Day, p, c.R > 0, c.R, cell.WinRatePosterior));
            }
        }

        var text = string.Join('\n', rows.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Key}|{r.Day:yyyy-MM-dd}|{r.PWin:R}|{(r.Won ? 1 : 0)}|{r.R:R}|{r.CellRate:R}")));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"model-holdout/1|{prepared.Model.Model.Sha256}|{block.Start:yyyy-MM-dd}|{prepared.BaseRate:R}\n{text}"))));
        return Result.Success(new ModelHoldOutRows(rows, prepared.BaseRate, hash, dropped));
    }

    /// <summary>A model trained from a simulation job: the versions are the job's sweeps, replayed as the job's chunks were (default parameters and timeframe, the stored charge sheet), and the cells are rebuilt from the job's closed trades that ended before the block.</summary>
    private async ValueTask<Result<(IReadOnlyList<PlannedRun> Runs, IReadOnlyList<CellStatsRow> Stats)>> PlanSimulationAsync(string jobText, IReadOnlyList<Guid> instruments, DateOnly blockStart, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(jobText, out var jobId) || await jobs.GetAsync(jobId, cancellationToken) is not { } job)
        {
            return Unavailable<(IReadOnlyList<PlannedRun>, IReadOnlyList<CellStatsRow>)>("The simulation job of the model's training set is not stored.");
        }

        var sweeps = await jobs.ListSweepsAsync(jobId, cancellationToken);
        var sheet = await sheets.FindAsync(labSettings.CostSheetId, cancellationToken);
        var costs = sheet.IsSuccess && sheet.Value.Schedules.Count > 0 && sheet.Value.Schedules[0].EffectiveFrom <= blockStart ? CostChoice.Sheet(sheet.Value.Id) : CostChoice.Gross;
        var planned = new List<PlannedRun>();
        foreach (var sweep in sweeps)
        {
            var version = await strategies.GetByHashAsync(sweep.StrategyHash, cancellationToken);
            if (version is null || checker.Check(version.SourceText).Description is not { } description)
            {
                return Unavailable<(IReadOnlyList<PlannedRun>, IReadOnlyList<CellStatsRow>)>("A strategy version of the training set is not stored or no longer compiles, so the model cannot be scored on it.");
            }

            var timeframe = engine.BuiltIns.FirstOrDefault(b => string.Equals(b.Id, sweep.StrategyId, StringComparison.Ordinal))?.DefaultTimeframe
                ?? (description.Timeframes.Contains(Timeframe.M15) || description.Timeframes.Count == 0 ? Timeframe.M15 : description.Timeframes[0]);
            var request = new SimulationRequest(sweep.StrategyId, new Dictionary<string, double>(), timeframe.ToCode(), blockStart, blockStart, [.. instruments], null, costs, false, null, null, null, null, sweep.StrategyHash);
            planned.Add(new PlannedRun(sweep.StrategyHash, sweep.StrategyId, SimTrainingCells.Style, SimTrainingCells.ExitLabel, request));
        }

        // the cells, as the training built them, from the closed trades that ended before the block (the job's window ends before its hold-out; the filter makes it a property of this read too)
        var finished = await jobs.ListFinishedAsync(jobId, cancellationToken);
        var headers = await trainingRuns.ListByKeysAsync(finished.RunKeys, cancellationToken);
        var sampleEvery = (int)Math.Max(1, (headers.Sum(h => (long)h.TradeCount) + trainingSettings.MaxRows - 1) / trainingSettings.MaxRows);
        var trades = new List<SimTrade>();
        foreach (var header in headers)
        {
            await foreach (var runTrade in trainingRuns.StreamTradesAsync(header.RunKey, cancellationToken))
            {
                var t = runTrade.Trade;
                if (SimTrainingRules.Admits(t.TradeDate, t.ExitEpochSeconds, job.WindowTo, job.HoldOutStart) && SessionClock.DateOf(t.ExitEpochSeconds) < blockStart && (sampleEvery == 1 || SimTrainingCells.Bucket(t.Key) % sampleEvery == 0))
                {
                    trades.Add(t);
                }
            }
        }

        var inputs = SimTrainingCells.BuildInputs(trades);
        if (inputs.IsFailure)
        {
            return Result.Failure<(IReadOnlyList<PlannedRun>, IReadOnlyList<CellStatsRow>)>(MlInfluenceErrors.HoldOutUnavailable(inputs.Error.Message));
        }

        IReadOnlyList<CellStatsRow> stats = inputs.Value.Count == 0 ? [] : CellStatsCalculator.Compute(inputs.Value, blockStart.AddDays(-1), new CellStatsOptions());
        return Result.Success(((IReadOnlyList<PlannedRun>)planned, stats));
    }

    /// <summary>A model trained from stored runs: each run is rebuilt by the LC-01 planner (the exact request that made it, proven by compiling to the stored definition hash); a run that cannot be rebuilt refuses the act.</summary>
    private async ValueTask<Result<IReadOnlyList<PlannedRun>>> PlanRunsAsync(string sources, DateOnly blockStart, CancellationToken cancellationToken)
    {
        var planned = new List<PlannedRun>();
        foreach (var part in sources.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split(':');
            if (pieces.Length != 3 || await runs.FindAsync(pieces[0], cancellationToken) is not { } header)
            {
                return Unavailable<IReadOnlyList<PlannedRun>>("A run of the model's training set is not stored.");
            }

            var record = await runs.ReadRecordTextAsync(pieces[0], cancellationToken);
            var request = record is null ? null : await planner.PlanAsync(header, record, cancellationToken);
            if (request is not { IsSuccess: true })
            {
                return Unavailable<IReadOnlyList<PlannedRun>>("A run of the model's training set cannot be rebuilt (" + (request?.Error.Message ?? "its record is not stored") + ").");
            }

            planned.Add(new PlannedRun(header.DefinitionSha256, header.StrategyId, pieces[1], pieces[2], request.Value with { From = blockStart, To = blockStart }));
        }

        return Result.Success<IReadOnlyList<PlannedRun>>([.. planned.DistinctBy(p => (p.Hash, p.Style, p.Exit, p.Request.EvaluationTimeframe))]);
    }

    private async ValueTask<Result<List<Candidate>>> SimulateAsync(IReadOnlyList<PlannedRun> plan, HoldOutBlock block, CancellationToken cancellationToken)
    {
        var result = new List<Candidate>();
        var failed = 0;
        Error? lastFailure = null;
        foreach (var v in plan)
        {
            var run = await simulation.HandleAsync(new RunSimulationQuery(v.Request with { From = block.Start, To = block.HistoryLast, Purpose = RunPurpose.Research }), cancellationToken);
            if (run.IsFailure)
            {
                // a version that cannot run (as a failed chunk of the training job) contributes no candidate; if none runs at all the act is refused below
                failed++;
                lastFailure = run.Error;
                continue;
            }

            foreach (var rt in run.Value.Trades)
            {
                var t = rt.Trade;
                var cell = new CellId(t.StrategyId, t.TriggerTimeframe.ToCode(), t.Side.ToString().ToUpperInvariant(), v.Style, v.Exit, Expression: Expression.Cash);
                result.Add(new Candidate(v.Hash[..12] + "|" + t.Key, t.InstrumentId, t.TriggerTimeframe, t.Side, t.TradeDate, t.DecisionEpochSeconds, t.StopPrice, t.TargetPrice, t.Costing is { } c ? c.RNet : t.RFilled, cell.Key));
            }
        }

        return failed == plan.Count && lastFailure is not null ? Result.Failure<List<Candidate>>(lastFailure) : Result.Success(result);
    }

    private async ValueTask<IReadOnlyList<Guid>> InstrumentsAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();
        foreach (var basket in await baskets.ListAsync(false, cancellationToken))
        {
            if (await baskets.MembersAsync(basket.Id, null, asOf, cancellationToken) is { } members)
            {
                foreach (var m in members.Items)
                {
                    ids.Add(m.Instrument.Id);
                }
            }
        }

        return [.. ids.Order()];
    }

    private static Result<PreparedModelHoldOut> Unavailable(string reason) => Result.Failure<PreparedModelHoldOut>(MlInfluenceErrors.HoldOutUnavailable(reason));

    private static Result<T> Unavailable<T>(string reason) => Result.Failure<T>(MlInfluenceErrors.HoldOutUnavailable(reason));
}
