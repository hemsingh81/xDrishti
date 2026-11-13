#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
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
using XDrishti.Application.Learning;
using XDrishti.Application.Learning.CellsRefresh;
using XDrishti.Application.Learning.SimTraining;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Learning;

/// <summary>
/// CF-10e part 3 (ADR 0285): the model's pass over the hold-out block on a real database, the real simulation query, the real feature extractor and the real stores, with recording decorators on the bar store, the corporate-action factors and the extractor. What is scripted is only what a
/// training job leaves behind (the job, its sweeps and the closed trades of its runs; a stored run and the request that made it): there is no engine run of a whole job here. The block is 2026-03-03 to 2026-03-06 (block 1, sealed under the pinned clock), with absurd canary bars after it. Asserted: no bar later than the
/// block's last minute is ever asked for, the cells come from the snapshot of the day before the block and never from the one of its first day, rows are scored for both kinds of training set, the dropped count adds up, the input hash repeats on a fresh database, one look and one score row are stored, a second call is refused
/// with both rows unchanged, an input that is missing refuses before the claim with the unlock table empty, and a filling block is refused with nothing written.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ModelHoldOutPassDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly S = ReplayDays.D2;
    private static readonly DateOnly E = ReplayDays.D5;
    private static readonly DateOnly Canary = new(2026, 3, 9);
    private static readonly long BlockEndEpoch = Day.Epoch(E, Day.Minute("15:30"));
    private static readonly Guid JobId = Guid.Parse("00000000-0000-7000-8000-00000000cf10");
    private static readonly string SetId = new('a', 64);
    private const string RunKey = "k1";

    private Guid _abb;
    private Guid _tcs;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await CleanAsync();
        await base.DisposeAsync();
    }

    private async Task CleanAsync()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.ml_holdout_scores; DELETE FROM lab.holdout_unlocks; DELETE FROM lab.holdout_releases; DELETE FROM lab.holdout_blocks; DELETE FROM research.cell_stats; DELETE FROM research.ml_switch_settings; DELETE FROM research.ml_influence_checks; DELETE FROM research.rollback_proposals; DELETE FROM research.ml_monitor_checks; DELETE FROM research.model_designations; DELETE FROM research.model_versions; DELETE FROM research.training_sets; DELETE FROM market.basket_members; DELETE FROM market.baskets");
    }

    // ---- the world ----

    private static Bar[] Wiggle(DateOnly date, double basis, int seed)
    {
        var bars = new List<Bar>();
        var price = basis;
        for (var m = Day.Minute("09:15"); m <= Day.Minute("15:29"); m++)
        {
            var next = basis + (1.8 * Math.Sin((m + seed) / 9.0)) + (0.9 * Math.Cos((m * (seed + 3)) / 31.0));
            bars.Add(new Bar(Day.Epoch(date, m), price, Math.Max(price, next) + 0.05, Math.Min(price, next) - 0.05, next, Day.MinuteVolume + (m % 17 * 40)));
            price = next;
        }

        return [.. bars];
    }

    private static IEnumerable<DateOnly> Weekdays(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                yield return d;
            }
        }
    }

    private async Task<Guid> TrackedAsync(string ticker, IEnumerable<Bar> bars)
    {
        var id = await SeedAsync(ticker);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.instruments SET tick_size = 5 WHERE id = @id", ("id", id));
            await ExecuteAsync(connection, "INSERT INTO market.tracked_instruments (instrument_id, active, active_since, tracked_by) VALUES (@id, true, now(), 'probe')", ("id", id));
        }

        using var scope = Factory.Services.CreateScope();
        await using var session = await scope.ServiceProvider.GetRequiredService<IBarStore>().BeginMergeAsync(id, BarSource.Dhan, Ct);
        await session.AddAsync(bars.ToArray(), Ct);
        await session.CompleteAsync(null, Ct);
        return id;
    }

    private async Task SeedWorldAsync()
    {
        (await StrategySeeding.SeedAsync(Factory.Services, Ct)).ShouldBeGreaterThan(0);
        var before = Weekdays(new DateOnly(2026, 2, 16), new DateOnly(2026, 2, 27)).ToList();
        var canary = Weekdays(Canary, Canary.AddDays(4)).ToList();
        Bar[] Pre(int seed) => [.. before.SelectMany((d, i) => Wiggle(d, 100, seed + i))];
        Bar[] Blocks(Bar[] d2, double after) => ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), d2, ReplayDays.Flat(ReplayDays.D3, after), ReplayDays.Flat(ReplayDays.D4, after), ReplayDays.Flat(ReplayDays.D5, after));
        Bar[] Absurd() => [.. canary.SelectMany(d => ReplayDays.Scaled(ReplayDays.Flat(d, 100), 1_000_000))];
        _abb = await TrackedAsync("ABB", ReplayDays.Join(Pre(1), Blocks(ReplayDays.F1(ReplayDays.D2), 105.00), Absurd()));
        _tcs = await TrackedAsync("TCS", ReplayDays.Join(Pre(7), Blocks(ReplayDays.F2(ReplayDays.D2), 98.50), Absurd()));
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO market.baskets (id, name, colour, purpose, description, position, created_at, revision) VALUES ('00000000-0000-7000-8000-0000000000b1', 'cf10e', '#000000', 'trading', 'd', 1, now(), 1)");
        await ExecuteAsync(connection, "INSERT INTO market.basket_members (id, basket_id, instrument_id, position, valid_from, added_by) VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000b1', @a, 1, '2026-01-01', 'test'), (gen_random_uuid(), '00000000-0000-7000-8000-0000000000b1', @t, 2, '2026-01-01', 'test')", ("a", _abb), ("t", _tcs));
        await ExecuteAsync(connection, "INSERT INTO lab.holdout_blocks (start_date, history_first, history_last, length_months, setting_hash, created_at) VALUES (@s, '2026-02-16', @e, 1, repeat('a', 64), now())", ("s", S), ("e", E));
    }

    private async Task<IReadOnlyList<SimulationRequest>> RequestsAsync(IServiceProvider sp)
    {
        var strategies = sp.GetRequiredService<IStrategyStore>();
        var engine = sp.GetRequiredService<ISimulationEngine>();
        var requests = new List<SimulationRequest>();
        foreach (var family in await strategies.ListAsync(Ct))
        {
            if (await strategies.GetVersionAsync(family.Id, null, Ct) is not { } version)
            {
                continue;
            }

            var timeframe = engine.BuiltIns.FirstOrDefault(b => b.Id == family.Id)?.DefaultTimeframe ?? Timeframe.M15;
            requests.Add(new SimulationRequest(family.Id, new Dictionary<string, double>(), timeframe.ToCode(), S, E, [_abb, _tcs], null, CostChoice.Gross, false, null, null, null, null, version.Hash));
        }

        return requests;
    }

    /// <summary>The trades the real engine makes in the block: the probe that fixes which cells exist.</summary>
    private async Task<(List<SimTrade> Trades, List<SimulationRequest> Runnable)> ProbeAsync(IServiceProvider sp)
    {
        var simulation = sp.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
        var trades = new List<SimTrade>();
        var runnable = new List<SimulationRequest>();
        foreach (var request in await RequestsAsync(sp))
        {
            var run = await simulation.HandleAsync(new RunSimulationQuery(request), Ct);
            if (run.IsSuccess)
            {
                runnable.Add(request);
                trades.AddRange(run.Value.Trades.Select(t => t.Trade));
            }
        }

        return (trades, runnable);
    }

    /// <summary>Closed training trades before the block for every cell the probe saw: 40 per cell, alternating win and loss (the flipped variant gives the snapshot of the block's first day other figures).</summary>
    private static List<SimTrade> TrainingTrades(IReadOnlyList<SimTrade> probe, bool flipped)
    {
        var trades = new List<SimTrade>();
        foreach (var cell in probe.GroupBy(t => (t.StrategyId, t.TriggerTimeframe, t.Side)))
        {
            var seed = cell.First();
            for (var i = 0; i < (flipped ? 25 : 40); i++)
            {
                var date = new DateOnly(2026, 1, 5).AddDays(i);
                var exit = Day.Epoch(date, Day.Minute("15:00"));
                trades.Add(seed with
                {
                    TradeDate = date,
                    DecisionEpochSeconds = Day.Epoch(date, Day.Minute("09:45")),
                    EntryEpochSeconds = Day.Epoch(date, Day.Minute("09:46")),
                    ExitEpochSeconds = exit,
                    RFilled = (i % 2 == 0) != flipped ? 1.0 : -1.0,
                    Costing = null,
                    Costs = CostBasis.None,
                });
            }
        }

        return trades;
    }

    private static async Task StoreSnapshotAsync(IServiceProvider sp, IReadOnlyList<SimTrade> trades, DateOnly asOf, char key)
    {
        var inputs = SimTrainingCells.BuildInputs(trades).Value;
        var options = new CellStatsOptions();
        var rows = CellStatsCalculator.Compute(inputs, asOf, options);
        (await sp.GetRequiredService<ICellStatsStore>().StoreAsync(new CellSnapshotToStore(new string(key, 64), asOf, options, "k1:swing:simulated", rows, "test", DateTimeOffset.UtcNow), Ct)).ShouldBeTrue();
    }

    private async Task<long> RegisterChampionAsync(IServiceProvider sp, string sources)
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "DELETE FROM research.training_sets");
            await ExecuteAsync(
                connection,
                """
                INSERT INTO research.training_sets (set_id, label_set_id, rule_version, feature_version, basis, sources, required_features, half_life_days, prior_strength, min_trades, credible_level, from_date, to_date, labelled, row_count,
                    positives, negatives, unresolved, no_target, dropped_no_features, dropped_unknown, created_at, created_by)
                VALUES (@id, @id, 'tb-1', @version, 'Gross', @sources, 'cell_n', 180, 20, 30, 0.9, '2026-01-01', '2026-02-27', 10, 10, 6, 4, 0, 0, 0, 0, now(), 'test')
                """,
                ("id", SetId), ("version", TradeFeatureCatalog.Version), ("sources", sources));
        }

        var random = new Random(11);
        var rows = Enumerable.Range(0, 300).Select(i =>
        {
            var features = Enumerable.Range(0, TradeFeatureCatalog.All.Count).Select(_ => random.NextDouble()).ToImmutableArray();
            var date = new DateOnly(2026, 1, 5).AddDays(i / 5);
            return new ValidationRow("r" + i, features[0] > 0.5 ? 1 : 0, features[0] - 0.5, date, Day.Epoch(date, Day.Minute("09:45")) + i, Day.Epoch(date, Day.Minute("15:00")) + i, "c|15m|BUY|swing|simulated|*", features);
        }).ToList();
        var trained = sp.GetRequiredService<IModelTrainer>().Train(rows);
        trained.IsSuccess.ShouldBeTrue(trained.IsFailure ? trained.Error.Message : string.Empty);
        var model = trained.Value;
        (await sp.GetRequiredService<IModelFiles>().WriteAsync(model.Sha256, model.Bytes, Ct)).IsSuccess.ShouldBeTrue();
        var registry = sp.GetRequiredService<IModelRegistry>();
        var registered = await registry.RegisterAsync(
            new ModelToRegister(new string('f', 64), model.Sha256, model.Bytes.Length, model.Trainer, null, SetId, TradeFeatureCatalog.Version, model.Width, model.FitRows, model.CalibrationRows, 300, new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 13), S, ValidationOutcome.Eligible,
                6, 5, 4, true, 3.5, 0.65, 0.23, string.Empty, "report", "params", model.CalibrationScores, model.CalibrationValues, model.Importance, 1, 0, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), "test"),
            Ct);
        (await registry.DesignateAsync(registered.Id, ModelRole.Champion, "setup", "test", DateTimeOffset.UtcNow, [], null, Ct)).ShouldBeTrue();
        return registered.Id;
    }

    // ---- recording decorators ----

    private sealed class RecordingBars(IBarStore inner) : IBarStore
    {
        public List<DateTimeOffset> Reads { get; } = [];

        public ValueTask<IBarMergeSession> BeginMergeAsync(Guid instrumentId, BarSource source, CancellationToken cancellationToken) => inner.BeginMergeAsync(instrumentId, source, cancellationToken);

        public ValueTask<IBarMergeSession> BeginInsertOnlyMergeAsync(Guid instrumentId, BarSource source, CancellationToken cancellationToken) => inner.BeginInsertOnlyMergeAsync(instrumentId, source, cancellationToken);

        public ValueTask<IReadOnlyList<Bar>> ReadAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            Reads.Add(to);
            return inner.ReadAsync(instrumentId, from, to, cancellationToken);
        }
    }

    private sealed class RecordingExtractor(ITradeFeatureExtractor inner) : ITradeFeatureExtractor
    {
        public List<(long LastBar, long LastDecision)> Calls { get; } = [];

        public IReadOnlyDictionary<string, double[]> Extract(IReadOnlyList<Bar> rawBars, AdjustmentFactors factors, TradingCalendar calendar, IReadOnlyList<TradeFeatureDecision> decisions)
        {
            Calls.Add((rawBars.Count == 0 ? 0 : rawBars.Max(b => b.EpochSeconds), decisions.Max(d => d.DecisionEpochSeconds)));
            return inner.Extract(rawBars, factors, calendar, decisions);
        }
    }

    private sealed record World(ModelHoldOutScorer Scorer, ModelHoldOutPass Pass, RecordingBars Bars, RecordingExtractor Extractor);

    private static World Assemble(IServiceProvider sp, List<SimTrade> training, List<SimulationRequest> requests, bool simAll, string tradingStrategy)
    {
        var bars = new RecordingBars(sp.GetRequiredService<IBarStore>());
        var extractor = new RecordingExtractor(sp.GetRequiredService<ITradeFeatureExtractor>());

        var strategies = sp.GetRequiredService<IStrategyStore>();
        var sweeps = requests.Select(r => new SimAllSweep(r.StrategyId, 1, r.StrategyHash!)).ToList();
        var jobs = Substitute.For<ISimAllStore>();
        jobs.GetAsync(JobId, Arg.Any<CancellationToken>()).Returns(new SimAllJob(JobId, "completed", null, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 27), S, 1, "test", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        jobs.ListSweepsAsync(JobId, Arg.Any<CancellationToken>()).Returns(sweeps);
        jobs.ListFinishedAsync(JobId, Arg.Any<CancellationToken>()).Returns(new SimAllFinished([RunKey], 0));

        var trainingRuns = Substitute.For<ITrainingRunReader>();
        trainingRuns.ListByKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(training.Count == 0 ? [] : [new TrainingRunHeader(RunKey, "ORB", new string('1', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 27), 2, 2, training.Count, 0, DateTimeOffset.UnixEpoch)]);
        trainingRuns.StreamTradesAsync(RunKey, Arg.Any<CancellationToken>()).Returns(_ => Stream(training));

        var runs = Substitute.For<IRunStore>();
        var planner = Substitute.For<ISourceRequestPlanner>();
        if (!simAll)
        {
            var first = requests.First(r => r.StrategyId == tradingStrategy);
            runs.FindAsync(RunKey, Arg.Any<CancellationToken>()).Returns(new StoredRunHeader(1, RunKey, "sim-5", 8, 5, first.StrategyId, first.StrategyHash!, S, E, "symbols", "x", 2, 2, 0, 0, 0, 0, false, "{}", null, null, 2, new string('2', 64), new string('3', 64), DateTimeOffset.UnixEpoch, "test"));
            runs.ReadRecordTextAsync(RunKey, Arg.Any<CancellationToken>()).Returns("{}");
            planner.PlanAsync(Arg.Any<StoredRunHeader>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(first)));
        }

        var pass = new ModelHoldOutPass(
            sp.GetRequiredService<ITrainingSetSources>(), jobs, trainingRuns, new SimTrainingSettings(), runs, planner, strategies, sp.GetRequiredService<IStrategyChecker>(), sp.GetRequiredService<ISimulationEngine>(), sp.GetRequiredService<ICostSheetStore>(),
            sp.GetRequiredService<LabSettings>(), sp.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>(), sp.GetRequiredService<IBasketQueries>(), bars, sp.GetRequiredService<IBackfillSeams>(), sp.GetRequiredService<IAdjustmentFactors>(),
            sp.GetRequiredService<ITradingCalendar>(), extractor, sp.GetRequiredService<ICellStatsStore>(), sp.GetRequiredService<IModelFiles>(), sp.GetRequiredService<IModelScorerLoader>());
        var scorer = new ModelHoldOutScorer(
            sp.GetRequiredService<IModelRegistry>(), sp.GetRequiredService<IHoldOutStore>(), sp.GetRequiredService<HoldOutEpochs>(), sp.GetRequiredService<ModelHoldOutLooks>(), pass, sp.GetRequiredService<IMlHoldOutScores>(), sp.GetRequiredService<IMlSwitchSettings>(),
            sp.GetRequiredService<MlInfluenceOptions>(), sp.GetRequiredService<TimeProvider>(), NullLogger<ModelHoldOutScorer>.Instance);
        return new World(scorer, pass, bars, extractor);
    }

    private static async IAsyncEnumerable<RunTrade> Stream(IReadOnlyList<SimTrade> trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private Task<long> CountAsync(string table) => ScalarAsync<long>($"SELECT count(*) FROM {table}");

    private async Task<(long Model, World World, List<SimTrade> Probe)> ArrangeAsync(IServiceProvider sp, bool simAll)
    {
        await SeedWorldAsync();
        var (probe, requests) = await ProbeAsync(sp);
        probe.Count.ShouldBeGreaterThan(0, "the real engine must trade on the shaped days, or the probe has no cell to train on");
        var training = TrainingTrades(probe, flipped: false);
        var model = await RegisterChampionAsync(sp, simAll ? $"sim-all:{JobId}" : $"{RunKey}:swing:simulated");
        if (!simAll)
        {
            await StoreSnapshotAsync(sp, training, S.AddDays(-1), 'c'); // the day before the block: used
            await StoreSnapshotAsync(sp, TrainingTrades(probe, flipped: true), S, 'd'); // the first day of the block: never used
        }

        return (model, Assemble(sp, simAll ? training : [], requests, simAll, probe[0].StrategyId), probe);
    }

    private static void AssertNoBarAfterTheBlock(World world)
    {
        world.Bars.Reads.ShouldNotBeEmpty();
        world.Bars.Reads.ShouldAllBe(to => to.ToUnixTimeSeconds() <= BlockEndEpoch, "no bar after the block's last minute is asked for (the canary days follow it)");
        world.Extractor.Calls.ShouldNotBeEmpty();
        world.Extractor.Calls.ShouldAllBe(c => c.LastBar <= c.LastDecision, "the extractor is given no bar after the last decision it describes");
    }

    [Fact]
    public async Task A_simulation_trained_model_is_scored_on_the_block_with_cells_rebuilt_before_it_and_no_bar_after_it()
    {
        using var scope = Factory.Services.CreateScope();
        var (model, world, probe) = await ArrangeAsync(scope.ServiceProvider, simAll: true);

        var result = await world.Scorer.ScoreAsync(model, "owner", "owner:test", Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        result.Value.Rows.ShouldBeGreaterThan(0);
        (result.Value.Rows + result.Value.Dropped).ShouldBe(probe.Count, "every candidate of the block is either scored or counted as dropped");
        AssertNoBarAfterTheBlock(world);
        (await CountAsync("lab.holdout_unlocks")).ShouldBe(1);
        (await CountAsync("research.ml_holdout_scores")).ShouldBe(1);

        var again = await world.Scorer.ScoreAsync(model, "owner", "owner:test", Ct);
        again.Error.Code.ShouldBe(MlInfluenceErrors.HoldOutReusedCode);
        (await CountAsync("lab.holdout_unlocks")).ShouldBe(1);
        (await CountAsync("research.ml_holdout_scores")).ShouldBe(1);
    }

    [Fact]
    public async Task A_run_trained_model_reads_the_snapshot_of_the_day_before_the_block_and_not_the_one_of_its_first_day()
    {
        using var scope = Factory.Services.CreateScope();
        var (model, world, probe) = await ArrangeAsync(scope.ServiceProvider, simAll: false);
        var stored = await scope.ServiceProvider.GetRequiredService<IModelRegistry>().FindAsync(model, Ct);

        var prepared = await world.Pass.PrepareAsync(stored!, S, Ct);

        prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Error.Message : string.Empty);
        prepared.Value.Cells.Values.ShouldAllBe(c => c.AsOf == S.AddDays(-1) && c.N == 40, "the snapshot of the day before the block (40 trades per cell), never the flipped one of the first day (25)");

        var result = await world.Scorer.ScoreAsync(model, "owner", "owner:test", Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        result.Value.Rows.ShouldBeGreaterThan(0);
        (result.Value.Rows + result.Value.Dropped).ShouldBe(probe.Count(t => t.StrategyId == probe[0].StrategyId), "the one version the stored run names");
        AssertNoBarAfterTheBlock(world);
        (await CountAsync("lab.holdout_unlocks")).ShouldBe(1);
        (await CountAsync("research.ml_holdout_scores")).ShouldBe(1);
    }

    [Fact]
    public async Task With_no_cell_known_before_the_block_the_act_is_refused_before_the_claim_and_the_unlock_table_stays_empty()
    {
        using var scope = Factory.Services.CreateScope();
        var (model, world, _) = await ArrangeAsync(scope.ServiceProvider, simAll: false);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "DELETE FROM research.cell_stats WHERE as_of < @s", ("s", S));
        }

        var result = await world.Scorer.ScoreAsync(model, "owner", "owner:test", Ct);

        result.Error.Code.ShouldBe(MlInfluenceErrors.HoldOutUnavailableCode);
        (await CountAsync("lab.holdout_unlocks")).ShouldBe(0);
        (await CountAsync("research.ml_holdout_scores")).ShouldBe(0);
    }

    [Fact]
    public async Task The_input_hash_repeats_on_a_fresh_database()
    {
        string first;
        using (var scope = Factory.Services.CreateScope())
        {
            var (model, world, _) = await ArrangeAsync(scope.ServiceProvider, simAll: true);
            first = (await world.Scorer.ScoreAsync(model, "owner", "owner:test", Ct)).Value.InputHash;
        }

        await Factory.ResetAllAsync();
        await CleanAsync();
        string second;
        using (var scope = Factory.Services.CreateScope())
        {
            var (model, world, _) = await ArrangeAsync(scope.ServiceProvider, simAll: true);
            second = (await world.Scorer.ScoreAsync(model, "owner", "owner:test", Ct)).Value.InputHash;
        }

        second.ShouldBe(first);
    }

    [Fact]
    public async Task A_block_that_is_still_filling_is_refused_and_nothing_is_written()
    {
        using var scope = Factory.Services.CreateScope();
        var (model, world, _) = await ArrangeAsync(scope.ServiceProvider, simAll: true);
        Factory.Clock.Pin(new DateTimeOffset(2026, 3, 5, 6, 30, 0, TimeSpan.Zero));

        var result = await world.Scorer.ScoreAsync(model, "owner", "owner:test", Ct);

        result.Error.Code.ShouldBe(LabErrors.HoldOutFillingCode);
        (await CountAsync("lab.holdout_unlocks")).ShouldBe(0);
        (await CountAsync("research.ml_holdout_scores")).ShouldBe(0);
    }
}
