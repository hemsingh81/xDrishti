using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// AU-13 (ADR 0249) over a real TimescaleDB: the training reader streams the trades of a training run and of nothing else, the training record and its <c>training_sim</c> cell statistics are written once and kept apart from the research cell statistics, the database refuses a
/// record that claims the hold-out was read or a verdict of Eligible, the migration goes down and up, and a training sweep of an ancestor of a family is a linked look in its honest trial count.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class SimTrainingStoreTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Before = "20261110090000_AddSimAllJobs";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly List<ScratchDatabase> _scratches = [];

    private ScratchDatabase Db => _scratches[0];

    public async ValueTask InitializeAsync()
    {
        var scratch = await ScratchDatabase.CreateAsync(factory, "xd_simtrain_" + Guid.NewGuid().ToString("N")[..8], CancellationToken.None);
        _scratches.Add(scratch);
        await StoreKit.ExecuteAsync(scratch.ConnectionString, "INSERT INTO lab.holdout_blocks (start_date, history_first, history_last, length_months, setting_hash, created_at) VALUES ('2040-01-01', '2026-01-01', '2041-06-30', 12, repeat('a', 64), now())", CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var scratch in _scratches)
        {
            await scratch.DisposeAsync();
        }
    }

    private async Task<SimulationRun> StoredAsync(RunPurpose purpose)
    {
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo()) with { IsTraining = purpose == RunPurpose.Training };
        (await StoreKit.WithStoreAsync(Db.Host.Services, s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        return run;
    }

    private Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters) => StoreKit.CountAsync(Db.ConnectionString, sql, Ct, parameters);

    private async Task<Guid> JobAsync()
    {
        var id = Guid.NewGuid();
        await StoreKit.ExecuteAsync(Db.ConnectionString, "INSERT INTO research.sim_all_jobs (id, state, window_from, window_to, hold_out_start, workers, started_by, created_at, updated_at, finished_at) VALUES (@id, 'completed', '2026-01-01', '2039-10-31', '2040-01-01', 2, 'owner:me@example.com', now(), now(), now())", Ct, ("id", id));
        return id;
    }

    private static SimTrainingRecord Record(Guid job, string hash, string state = "trained", string? verdict = "Inconclusive") =>
        new(hash, job, state, state == "trained" ? null : "rows: 5 usable closed trades, 1000 needed", 3, 1, 1200, 1, 7, 8, 5, 7, new DateOnly(2026, 1, 1), new DateOnly(2039, 10, 31), new DateOnly(2040, 1, 1),
            state == "trained" ? 0.61 : null, state == "trained" ? 0.64 : null, state == "trained" ? "base_rate" : null, state == "trained" ? 0.21 : null, state == "trained" ? 0.22 : null, state == "trained" ? verdict : null, state == "trained" ? 7L : null,
            state == "trained" ? new string('c', 64) : null, "summary", new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero));

    private static TrainingSimCell Cell(string key) => new(key, "ORB", "15m", "BUY", "all", false, 40, 18, 35.5, 0.44, 0.31, 0.57, 0.12, true);

    [Fact]
    public async Task The_training_reader_streams_the_closed_trades_of_a_training_run_and_nothing_of_a_research_run()
    {
        var training = await StoredAsync(RunPurpose.Training);
        var research = await StoredAsync(RunPurpose.Research);
        using var scope = Db.Host.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITrainingRunReader>();

        var trades = await reader.StreamTradesAsync(training.RunKey, Ct).ToListAsync(Ct);

        trades.Count.ShouldBe(training.Trades.Count);
        trades.Select(t => t.Trade.Key).ShouldBe(training.Trades.Select(t => t.Trade.Key));
        (await reader.StreamTradesAsync(research.RunKey, Ct).ToListAsync(Ct)).ShouldBeEmpty("a research run is not visible to the training reader");
        (await reader.StreamTradesAsync(new string('0', 64), Ct).ToListAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_training_is_written_once_with_its_cells_on_the_training_sim_basis_and_the_research_cell_statistics_stay_untouched()
    {
        var job = await JobAsync();
        var hash = new string('d', 64);
        using var scope = Db.Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISimTrainingStore>();

        (await store.StoreAsync(Record(job, hash), new DateOnly(2039, 10, 31), [Cell("ORB|15m|BUY|swing|simulated|*|*|cash"), Cell("ORB|15m|SELL|swing|simulated|*|*|cash")], Ct)).ShouldBeTrue();
        (await store.StoreAsync(Record(job, hash), new DateOnly(2039, 10, 31), [Cell("ORB|15m|BUY|swing|simulated|*|*|cash")], Ct)).ShouldBeFalse("the same inputs write nothing");

        var found = (await store.FindByJobAsync(job, Ct)).ShouldNotBeNull();
        found.ShouldBe(Record(job, hash));
        (await store.FindAsync(hash, Ct)).ShouldBe(found);
        (await store.FindAsync(new string('e', 64), Ct)).ShouldBeNull();
        (await CountAsync("SELECT count(*) FROM research.sim_training_runs")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM research.training_sim_cell_stats WHERE basis = 'training_sim'")).ShouldBe(2);
        (await CountAsync("SELECT count(*) FROM research.cell_stats")).ShouldBe(0, "the research and forward basis never sees the training_sim cells");
        (await CountAsync("SELECT count(*) FROM research.sim_training_runs WHERE hold_out_read")).ShouldBe(0);
    }

    [Fact]
    public async Task A_not_evaluable_training_stores_the_missing_quantity_and_no_model()
    {
        var job = await JobAsync();
        using var scope = Db.Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISimTrainingStore>();

        (await store.StoreAsync(Record(job, new string('f', 64), "not_evaluable"), new DateOnly(2039, 10, 31), [], Ct)).ShouldBeTrue();

        var found = (await store.FindByJobAsync(job, Ct)).ShouldNotBeNull();
        found.State.ShouldBe("not_evaluable");
        found.NotEvaluable.ShouldStartWith("rows: ");
        found.ModelId.ShouldBeNull();
    }

    [Theory]
    [InlineData("UPDATE research.sim_training_runs SET hold_out_read = true", "ck_sim_training_runs_hold_out_unread")]
    [InlineData("UPDATE research.sim_training_runs SET verdict = 'Eligible'", "ck_sim_training_runs_shape")]
    [InlineData("UPDATE research.sim_training_runs SET state = 'trained', model_id = NULL", "ck_sim_training_runs_shape")]
    [InlineData("UPDATE research.training_sim_cell_stats SET basis = 'research'", "ck_training_sim_cell_stats_basis")]
    public async Task The_database_itself_refuses_a_hold_out_read_an_eligible_verdict_a_trained_record_without_a_model_and_another_basis(string statement, string constraint)
    {
        var job = await JobAsync();
        using (var scope = Db.Host.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISimTrainingStore>();
            await store.StoreAsync(Record(job, new string('d', 64)), new DateOnly(2039, 10, 31), [Cell("ORB|15m|BUY|swing|simulated|*|*|cash")], Ct);
        }

        var failure = await Should.ThrowAsync<PostgresException>(() => StoreKit.ExecuteAsync(Db.ConnectionString, statement, Ct));

        failure.ConstraintName.ShouldBe(constraint);
    }

    [Fact]
    public async Task The_migration_goes_down_to_the_job_tables_and_up_again()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_simtrain_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();

        await migrator.MigrateAsync(Before, Ct);

        (await StoreKit.CountAsync(scratch.ConnectionString, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'research' AND table_name IN ('sim_training_runs', 'training_sim_cell_stats')", Ct)).ShouldBe(0);
        (await StoreKit.CountAsync(scratch.ConnectionString, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'research' AND table_name IN ('sim_all_jobs', 'sim_all_chunks')", Ct)).ShouldBe(2, "the earlier migration is untouched");

        await migrator.MigrateAsync(null, Ct);

        (await StoreKit.CountAsync(scratch.ConnectionString, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'research' AND table_name IN ('sim_training_runs', 'training_sim_cell_stats')", Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task A_training_sweep_of_a_version_the_family_descends_from_is_a_linked_look_in_the_honest_count_and_an_unrelated_sweep_is_not()
    {
        var family = "TUNED" + Guid.NewGuid().ToString("N")[..6];
        var parentFamily = "PARENT" + Guid.NewGuid().ToString("N")[..6];
        var other = "OTHER" + Guid.NewGuid().ToString("N")[..6];
        string H(char c) => new(c, 64);
        string Strategy(string id) => $"INSERT INTO lab.strategies (id, style, built_in, created_at) VALUES ('{id}', 'intraday', false, now());";
        string Version(string id, int v, string hash, string? parent) => $"INSERT INTO lab.strategy_versions (strategy_id, version, schema_version, hash, source_text, canonical_json, parent_hash, created_at) VALUES ('{id}', {v}, 1, '{hash}', 't', '{{}}', {(parent is null ? "NULL" : "'" + parent + "'")}, now());";
        string Sweep(string id, string hash) => $"INSERT INTO lab.test_ledger (lab_run_id, run_key, strategy_id, version, strategy_hash, universe_label, window_from, window_to, method, created_at, trial_kind, extra_trials) VALUES (gen_random_uuid(), '{Guid.NewGuid():N}{Guid.NewGuid():N}', '{id}', 1, '{hash}', 'all', '2026-01-01', '2039-10-31', 'training', now(), 'training_sweep', 0);";
        await StoreKit.ExecuteAsync(Db.ConnectionString, Strategy(parentFamily) + Strategy(family) + Strategy(other)
            + Version(parentFamily, 1, H('1'), null) + Version(family, 1, H('2'), H('1')) + Version(family, 2, H('3'), H('2')) + Version(other, 1, H('9'), null)
            + Sweep(parentFamily, H('1')) + Sweep(family, H('3')) + Sweep(other, H('9')), Ct);
        using var scope = Db.Host.Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ITestLedger>();

        var counts = await ledger.CountTrialsAsync(family, Ct);

        counts.LinkedSweeps.ShouldBe(2, "the sweep of the parent version and the sweep of the family's own version");
        counts.TrainingSweeps.ShouldBe(1, "the family's own ledger rows, shown as before");
        counts.Total.ShouldBe(2, "linked sweeps are the only looks this family has");
        (await ledger.CountTrialsAsync(other, Ct)).LinkedSweeps.ShouldBe(1);
        (await ledger.CountTrialsAsync("NO_SUCH_FAMILY", Ct)).LinkedSweeps.ShouldBe(0);
    }
}
