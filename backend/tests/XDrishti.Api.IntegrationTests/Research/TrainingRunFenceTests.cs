using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// AU-11 (ADR 0243) over a real TimescaleDB: a <c>training</c> run is stored but no evidence reader sees it (the run store reads, the report source, the comparison store, the AI views), an existing or default run reads as <c>research</c>,
/// the same run under the other purpose is a conflict, and a <c>training_sweep</c> ledger row is invisible to the ledger readers.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class TrainingRunFenceTests(ApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly List<ScratchDatabase> _scratches = [];

    private ScratchDatabase Db => _scratches[0];

    public async ValueTask InitializeAsync()
    {
        // a scratch database per test: a training run needs a stored hold-out block, and the shared database must keep its own
        var scratch = await ScratchDatabase.CreateAsync(factory, "xd_train_" + Guid.NewGuid().ToString("N")[..8], CancellationToken.None);
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

    private Task<T> WithStore<T>(Func<IRunStore, Task<T>> action) => StoreKit.WithStoreAsync(Db.Host.Services, action);

    private async Task<SimulationRun> StoredAsync(RunPurpose purpose)
    {
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo());
        (await WithStore(s => s.StoreAsync(StoreKit.ToStore(run) with { Purpose = purpose }, Ct).AsTask())).IsSuccess.ShouldBeTrue();
        return run;
    }

    private Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters) => StoreKit.CountAsync(Db.ConnectionString, sql, Ct, parameters);

    [Fact]
    public async Task A_run_stored_without_a_purpose_reads_as_research_and_is_visible_everywhere()
    {
        var run = await StoredAsync(RunPurpose.Research);

        (await CountAsync("SELECT count(*) FROM research.sim_runs WHERE run_key = @k AND purpose = 'research'", ("k", run.RunKey))).ShouldBe(1);
        (await WithStore(s => s.FindAsync(run.RunKey, Ct).AsTask())).ShouldNotBeNull();
        (await CountAsync("SELECT count(*) FROM aiv.sim_runs WHERE run_key = @k", ("k", run.RunKey))).ShouldBe(1);
        using var scope = Db.Host.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IReportTradeSource>().ListRunsAsync(Ct)).ShouldContain(r => r.RunKey == run.RunKey);
    }

    [Fact]
    public async Task A_training_run_is_stored_but_every_run_store_read_and_the_report_source_and_the_views_do_not_see_it()
    {
        var run = await StoredAsync(RunPurpose.Training);

        (await CountAsync("SELECT count(*) FROM research.sim_runs WHERE run_key = @k AND purpose = 'training'", ("k", run.RunKey))).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM research.sim_trades t JOIN research.sim_runs r ON r.id = t.run_id WHERE r.run_key = @k", ("k", run.RunKey))).ShouldBeGreaterThan(0);

        (await WithStore(s => s.FindAsync(run.RunKey, Ct).AsTask())).ShouldBeNull();
        (await WithStore(s => s.ReadRecordTextAsync(run.RunKey, Ct).AsTask())).ShouldBeNull();
        var list = await WithStore(s => s.ListAsync(new RunListFilter(), new PageRequest(100, null), Ct).AsTask());
        list.Value.Items.ShouldNotContain(h => h.RunKey == run.RunKey);
        (await WithStore(async s => await s.StreamTradesAsync(run.RunKey, false, Ct).ToListAsync(Ct))).ShouldBeEmpty();
        (await WithStore(s => s.ReadTradeAsync(run.RunKey, false, 0, Ct).AsTask())).ShouldBeNull();

        using var scope = Db.Host.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportTradeSource>();
        (await reports.ListRunsAsync(Ct)).ShouldNotContain(r => r.RunKey == run.RunKey);
        (await reports.StreamTradesAsync([run.RunKey], new ReportTradeFilter([], [], [], [], [], null, null), Ct).ToListAsync(Ct)).ShouldBeEmpty();

        (await CountAsync("SELECT count(*) FROM aiv.sim_runs WHERE run_key = @k", ("k", run.RunKey))).ShouldBe(0);
        (await CountAsync("SELECT count(*) FROM aiv.sim_trades WHERE run_key = @k", ("k", run.RunKey))).ShouldBe(0);
        (await CountAsync("SELECT count(*) FROM aiv.sim_trade_legs WHERE run_key = @k", ("k", run.RunKey))).ShouldBe(0);
    }

    [Fact]
    public async Task A_comparison_cannot_take_a_training_run_as_a_member_there_is_no_conversion_path()
    {
        var a = await StoredAsync(RunPurpose.Training);
        var b = await StoredAsync(RunPurpose.Training);
        var exit = new ExitCandidate("FIXED_RR", [new ExitParameterValue("rr", [2])]);
        var candidates = new[] { new CandidateRun("FIXED_RR{rr=2}", exit, a, 0), new CandidateRun("FIXED_RR{rr=3}", exit, b, 3) };
        var comparison = new ComparisonRun(candidates, ComparisonReport.Build(candidates, true), new string('e', 64));

        using var scope = Db.Host.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreComparisonCommand, StoreOutcome>>()
            .HandleAsync(new StoreComparisonCommand(comparison, SimulateStore.ComparisonRecord(comparison.Report), SimulateStore.Actor), Ct);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task A_research_run_and_a_training_run_of_the_same_inputs_are_stored_side_by_side_each_seen_only_by_its_own_readers()
    {
        var research = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo());
        // a training header adds the line purpose=training to the key text (RunAssembler), so the same inputs give another key
        var training = research with { RunKey = StoreKit.Sha256(research.RunKey + "|purpose=training") };

        (await WithStore(s => s.StoreAsync(StoreKit.ToStore(training) with { Purpose = RunPurpose.Training }, Ct).AsTask())).IsSuccess.ShouldBeTrue();
        (await WithStore(s => s.StoreAsync(StoreKit.ToStore(research), Ct).AsTask())).Value.WasAlreadyStored.ShouldBeFalse();

        (await WithStore(s => s.FindAsync(research.RunKey, Ct).AsTask())).ShouldNotBeNull();
        (await WithStore(s => s.FindAsync(training.RunKey, Ct).AsTask())).ShouldBeNull();
        // the same key under the other purpose is still a true mismatch: refused, never a silent no-op
        var clash = await WithStore(s => s.StoreAsync(StoreKit.ToStore(training), Ct).AsTask());
        clash.IsFailure.ShouldBeTrue();
        clash.Error.Code.ShouldBe(StoreErrors.RunConflictCode);
    }

    [Fact]
    public async Task Trade_pages_and_a_cell_source_of_a_training_key_are_not_found()
    {
        var run = await StoredAsync(RunPurpose.Training);

        var page = await WithStore(s => s.PageTradesAsync(run.RunKey, new TradePageFilter(), new PageRequest(10, null), Ct).AsTask());
        page.IsFailure.ShouldBeTrue();
        page.Error.Code.ShouldBe("store.run_not_found");

        using var scope = Db.Host.Services.CreateScope();
        var cells = await XDrishti.Application.Learning.CellSourceReader.ReadAsync(scope.ServiceProvider.GetRequiredService<IRunStore>(), [new XDrishti.Application.Learning.CellSource(run.RunKey, "momentum", "FIXED_RR{rr=2}")], Ct);
        cells.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Every_view_over_the_run_tables_carries_the_research_predicate()
    {
        // found in the catalogue, not in the source: a view added by any later migration, in any schema, is checked
        var names = new List<string>();
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("""
            SELECT schemaname || '.' || viewname FROM pg_views
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
              AND (definition ~* 'sim_runs|sim_trades|sim_trade_legs|sim_trade_leg_charges')
              AND definition !~* 'purpose\s*=\s*''research''::text'
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            names.Add(reader.GetString(0));
        }

        names.ShouldBeEmpty("a view over the run tables without purpose = 'research' lets a training run into its readers (ADR 0243)");
    }

    [Fact]
    public async Task The_database_refuses_an_unknown_purpose()
    {
        var run = await StoredAsync(RunPurpose.Research);

        var failure = await Should.ThrowAsync<PostgresException>(() => StoreKit.ExecuteAsync(Db.ConnectionString, "UPDATE research.sim_runs SET purpose = 'bogus' WHERE run_key = @k", Ct, ("k", run.RunKey)));

        failure.ConstraintName.ShouldBe("ck_sim_runs_purpose");
    }

    [Fact]
    public async Task A_training_sweep_ledger_row_is_listed_and_counted_separately_but_is_no_look_and_no_variant()
    {
        var strategy = "AU11" + Guid.NewGuid().ToString("N")[..8];
        var hash = new string('a', 64);
        using var scope = Db.Host.Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ITestLedger>();
        var row = new LedgerRow(Guid.NewGuid(), new string('b', 64), strategy, 1, hash, null, "all", new DateOnly(2020, 1, 1), new DateOnly(2024, 1, 1), "full", DateTimeOffset.UtcNow, "training_sweep", 0);

        (await ledger.AppendAsync(row, Ct)).ShouldBeTrue();

        (await ledger.ListAsync(strategy, Ct)).Single().TrialKind.ShouldBe("training_sweep");
        (await ledger.CountVariantsAsync(strategy, Ct)).ShouldBe(0);
        (await ledger.HasHashAsync(strategy, hash, Ct)).ShouldBeFalse();
        var counts = await ledger.CountTrialsAsync(strategy, Ct);
        counts.ShouldBe(new TrialCounts(0, 0, 0, 0, 0, 1, 0));
        counts.Total.ShouldBe(0);
        (await CountAsync("SELECT count(*) FROM aiv.test_ledger WHERE strategy_id = @s AND trial_kind = 'training_sweep'", ("s", strategy))).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM lab.test_ledger WHERE strategy_id = @s AND trial_kind = 'training_sweep'", ("s", strategy))).ShouldBe(1);
    }
}
