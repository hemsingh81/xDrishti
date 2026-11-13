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
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.History;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning;

/// <summary>What the model's pass over the block produced: the scored rows, the base rate read before the block, a hash of the rows and how many candidates could not be scored (no feature or no cell statistics).</summary>
internal sealed record ModelHoldOutRows(IReadOnlyList<ModelHoldOutRow> Rows, double BaseRate, string InputHash, int Dropped);

/// <summary>The one plain pass of a model's strategy versions over the hold-out block (an interface so the scoring can be tested without an engine).</summary>
internal interface IModelHoldOutPass
{
    ValueTask<Result<ModelHoldOutRows>> RunAsync(StoredModel model, HoldOutBlock block, CancellationToken cancellationToken);
}

/// <summary>
/// The second reader of the sessions inside the locked hold-out (CF-10e part 3, ADR 0285; the first is <c>HoldOutPass</c>). It runs the strategy versions of the model's training set once over the block, as the training did (default parameters, default timeframe and exits, the stored charge sheet),
/// over the instruments of the stored baskets, through the same <c>RunSimulationQuery</c>, and extracts the decision-time features of each candidate from the bars before its decision only (the P6-04 extractor), joined with the newest stored cash cell statistics whose as-of date is before the block
/// (the cell's rate and the base rate, both therefore known before the block). A candidate whose features or cell cannot be built is dropped and counted, never imputed. The model scores the rest; a win is a realised R above zero. Used by <see cref="ModelHoldOutScorer"/> after the look is claimed; an
/// architecture test pins that. Reads, never writes; places no order.
/// </summary>
internal sealed class ModelHoldOutPass(
    ITrainingSetSources trainingSets,
    ISimAllStore jobs,
    IRunStore runs,
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

    private sealed record Version(string StrategyId, string Hash, string Style, string Exit);

    private sealed record Candidate(string Key, Guid InstrumentId, Timeframe Timeframe, Side Side, DateOnly Day, long DecisionEpoch, double Stop, double? Target, double R, string CellKey);

    public async ValueTask<Result<ModelHoldOutRows>> RunAsync(StoredModel model, HoldOutBlock block, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(block);
        if (model.Model.FeatureVersion != TradeFeatureCatalog.Version || model.Model.FeatureWidth != TradeFeatureCatalog.All.Count)
        {
            return Unavailable("The model was made in another feature catalog version than the one this system builds now.");
        }

        var versions = await VersionsAsync(model.Model.TrainingSetId, cancellationToken);
        if (versions.Count == 0)
        {
            return Unavailable("The strategy versions of the model's training set are not known.");
        }

        var statsRows = CashCells.Only(await cells.ListLatestBeforeAsync(block.Start, null, CellWindow.All.Code(), cancellationToken), "model_holdout", null).Where(r => r.Row.Window == CellWindow.All).ToList();
        var total = statsRows.Sum(r => r.Row.N);
        if (statsRows.Count == 0 || total <= 0)
        {
            return Unavailable("No cell statistics were stored before the block, so the baselines cannot be read.");
        }

        var baseRate = Math.Clamp(statsRows.Sum(r => r.Row.WinRatePosterior * r.Row.N) / total, 1e-6, 1 - 1e-6);
        var stats = statsRows.ToDictionary(r => r.Row.Cell.Key, r => r.Row, StringComparer.Ordinal);

        var bytes = await files.ReadAsync(model.Model.Sha256, cancellationToken);
        var scorer = bytes.IsSuccess ? loader.Load(bytes.Value, model.Model.Sha256) : null;
        if (scorer is not { IsSuccess: true })
        {
            return Unavailable("The model file cannot be loaded.");
        }

        var instruments = await InstrumentsAsync(cancellationToken);
        if (instruments.Count == 0)
        {
            return Unavailable("No instrument is in the stored baskets.");
        }

        var simulated = await SimulateAsync(versions, instruments, block, cancellationToken);
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
            var until = DateTimeOffset.FromUnixTimeSeconds(group.Max(c => c.DecisionEpoch));

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
                if (!values.TryGetValue(c.Key, out var stored) || stored.Length != TradeFeatureCatalog.StoredCount || !stats.TryGetValue(c.CellKey, out var cell))
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

                var p = scorer.Value.Score(new ValidationRow("score", 0, 0, default, 0, 0, string.Empty, [.. vector]));
                if (!double.IsFinite(p) || p is < 0 or > 1 || !(cell.WinRatePosterior > 0 && cell.WinRatePosterior < 1))
                {
                    dropped++;
                    continue;
                }

                rows.Add(new ModelHoldOutRow(c.Key, c.Day, p, c.R > 0, c.R, cell.WinRatePosterior));
            }
        }

        var text = string.Join('\n', rows.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Key}|{r.Day:yyyy-MM-dd}|{r.PWin:R}|{(r.Won ? 1 : 0)}|{r.R:R}|{r.CellRate:R}")));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"model-holdout/1|{model.Id}|{block.Start:yyyy-MM-dd}|{baseRate:R}\n{text}"))));
        return Result.Success(new ModelHoldOutRows(rows, baseRate, hash, dropped));
    }

    private async ValueTask<IReadOnlyList<Version>> VersionsAsync(string trainingSetId, CancellationToken cancellationToken)
    {
        var sources = await trainingSets.SourcesAsync(trainingSetId, cancellationToken);
        var found = new List<Version>();
        if (string.IsNullOrWhiteSpace(sources))
        {
            return found;
        }

        if (sources.StartsWith(SimAllPrefix, StringComparison.Ordinal))
        {
            if (Guid.TryParse(sources[SimAllPrefix.Length..], out var job))
            {
                found.AddRange((await jobs.ListSweepsAsync(job, cancellationToken)).Select(s => new Version(s.StrategyId, s.StrategyHash, "swing", "simulated")));
            }

            return found;
        }

        foreach (var part in sources.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split(':');
            if (pieces.Length == 3 && await runs.FindAsync(pieces[0], cancellationToken) is { } header)
            {
                found.Add(new Version(header.StrategyId, header.DefinitionSha256, pieces[1], pieces[2]));
            }
        }

        return [.. found.DistinctBy(v => (v.Hash, v.Style, v.Exit))];
    }

    private async ValueTask<Result<List<Candidate>>> SimulateAsync(IReadOnlyList<Version> versions, IReadOnlyList<Guid> instruments, HoldOutBlock block, CancellationToken cancellationToken)
    {
        var sheet = await sheets.FindAsync(labSettings.CostSheetId, cancellationToken);
        var costs = sheet.IsSuccess && sheet.Value.Schedules.Count > 0 && sheet.Value.Schedules[0].EffectiveFrom <= block.Start ? CostChoice.Sheet(sheet.Value.Id) : CostChoice.Gross;
        var result = new List<Candidate>();
        foreach (var v in versions)
        {
            var version = await strategies.GetByHashAsync(v.Hash, cancellationToken);
            if (version is null || checker.Check(version.SourceText).Description is not { } description)
            {
                return Unavailable<List<Candidate>>("A strategy version of the training set is not stored or no longer compiles, so the model cannot be scored on it.");
            }

            var timeframe = engine.BuiltIns.FirstOrDefault(b => string.Equals(b.Id, v.StrategyId, StringComparison.Ordinal))?.DefaultTimeframe
                ?? (description.Timeframes.Contains(Timeframe.M15) || description.Timeframes.Count == 0 ? Timeframe.M15 : description.Timeframes[0]);
            var request = new SimulationRequest(v.StrategyId, new Dictionary<string, double>(), timeframe.ToCode(), block.Start, block.HistoryLast, [.. instruments], null, costs, false, null, null, null, null, v.Hash);
            var run = await simulation.HandleAsync(new RunSimulationQuery(request), cancellationToken);
            if (run.IsFailure)
            {
                return Result.Failure<List<Candidate>>(run.Error);
            }

            foreach (var rt in run.Value.Trades)
            {
                var t = rt.Trade;
                var cell = new CellId(t.StrategyId, t.TriggerTimeframe.ToCode(), t.Side.ToString().ToUpperInvariant(), v.Style, v.Exit, Expression: Expression.Cash);
                result.Add(new Candidate(v.Hash[..12] + "|" + t.Key, t.InstrumentId, t.TriggerTimeframe, t.Side, t.TradeDate, t.DecisionEpochSeconds, t.StopPrice, t.TargetPrice, t.Costing is { } c ? c.RNet : t.RFilled, cell.Key));
            }
        }

        return Result.Success(result);
    }

    private async ValueTask<IReadOnlyList<Guid>> InstrumentsAsync(CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();
        foreach (var basket in await baskets.ListAsync(false, cancellationToken))
        {
            if (await baskets.MembersAsync(basket.Id, null, null, cancellationToken) is { } members)
            {
                foreach (var m in members.Items)
                {
                    ids.Add(m.Instrument.Id);
                }
            }
        }

        return [.. ids.Order()];
    }

    private static Result<ModelHoldOutRows> Unavailable(string reason) => Result.Failure<ModelHoldOutRows>(MlInfluenceErrors.HoldOutUnavailable(reason));

    private static Result<T> Unavailable<T>(string reason) => Result.Failure<T>(MlInfluenceErrors.HoldOutUnavailable(reason));
}
