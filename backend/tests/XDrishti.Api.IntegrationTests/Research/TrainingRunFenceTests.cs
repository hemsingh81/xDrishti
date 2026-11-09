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

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>Training rows are removed afterwards: the shared database is migrated down by other tests, and the down migration refuses while a training run exists (it would turn it into evidence).</summary>
    public async ValueTask DisposeAsync()
    {
        const string ids = "(SELECT id FROM research.sim_runs WHERE purpose = 'training')";
        foreach (var sql in new[]
        {
            $"DELETE FROM research.sim_trade_leg_charges WHERE run_id IN {ids}",
            $"DELETE FROM research.sim_trade_legs WHERE run_id IN {ids}",
            $"DELETE FROM research.sim_trades WHERE run_id IN {ids}",
            "DELETE FROM research.sim_runs WHERE purpose = 'training'",
            "DELETE FROM lab.test_ledger WHERE trial_kind = 'training_sweep'",
        })
        {
            await StoreKit.ExecuteAsync(factory.AdminConnectionString, sql, CancellationToken.None);
        }
    }

    private Task<T> WithStore<T>(Func<IRunStore, Task<T>> action) => StoreKit.WithStoreAsync(factory.Services, action);

    private async Task<SimulationRun> StoredAsync(RunPurpose purpose)
    {
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo());
        (await WithStore(s => s.StoreAsync(StoreKit.ToStore(run) with { Purpose = purpose }, Ct).AsTask())).IsSuccess.ShouldBeTrue();
        return run;
    }

    private Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters) => StoreKit.CountAsync(factory.AdminConnectionString, sql, Ct, parameters);

    [Fact]
    public async Task A_run_stored_without_a_purpose_reads_as_research_and_is_visible_everywhere()
    {
        var run = await StoredAsync(RunPurpose.Research);

        (await CountAsync("SELECT count(*) FROM research.sim_runs WHERE run_key = @k AND purpose = 'research'", ("k", run.RunKey))).ShouldBe(1);
        (await WithStore(s => s.FindAsync(run.RunKey, Ct).AsTask())).ShouldNotBeNull();
        (await CountAsync("SELECT count(*) FROM aiv.sim_runs WHERE run_key = @k", ("k", run.RunKey))).ShouldBe(1);
        using var scope = factory.Services.CreateScope();
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

        using var scope = factory.Services.CreateScope();
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

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreComparisonCommand, StoreOutcome>>()
            .HandleAsync(new StoreComparisonCommand(comparison, SimulateStore.ComparisonRecord(comparison.Report), SimulateStore.Actor), Ct);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task The_same_run_under_the_other_purpose_is_a_conflict_not_a_no_op()
    {
        var run = await StoredAsync(RunPurpose.Training);

        var again = await WithStore(s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask());

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe(StoreErrors.RunConflictCode);
    }

    [Fact]
    public async Task The_database_refuses_an_unknown_purpose()
    {
        var run = await StoredAsync(RunPurpose.Research);

        var failure = await Should.ThrowAsync<PostgresException>(() => StoreKit.ExecuteAsync(factory.AdminConnectionString, "UPDATE research.sim_runs SET purpose = 'bogus' WHERE run_key = @k", Ct, ("k", run.RunKey)));

        failure.ConstraintName.ShouldBe("ck_sim_runs_purpose");
    }

    [Fact]
    public async Task A_training_sweep_ledger_row_is_accepted_but_no_ledger_reader_counts_or_lists_it()
    {
        var strategy = "AU11" + Guid.NewGuid().ToString("N")[..8];
        var hash = new string('a', 64);
        using var scope = factory.Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ITestLedger>();
        var row = new LedgerRow(Guid.NewGuid(), new string('b', 64), strategy, 1, hash, null, "all", new DateOnly(2020, 1, 1), new DateOnly(2024, 1, 1), "full", DateTimeOffset.UtcNow, "training_sweep", 0);

        (await ledger.AppendAsync(row, Ct)).ShouldBeTrue();

        (await ledger.ListAsync(strategy, Ct)).ShouldBeEmpty();
        (await ledger.CountVariantsAsync(strategy, Ct)).ShouldBe(0);
        (await ledger.HasHashAsync(strategy, hash, Ct)).ShouldBeFalse();
        (await ledger.CountTrialsAsync(strategy, Ct)).ShouldBe(new TrialCounts(0, 0, 0, 0, 0));
        (await CountAsync("SELECT count(*) FROM aiv.test_ledger WHERE strategy_id = @s", ("s", strategy))).ShouldBe(0);
        (await CountAsync("SELECT count(*) FROM lab.test_ledger WHERE strategy_id = @s AND trial_kind = 'training_sweep'", ("s", strategy))).ShouldBe(1);
    }
}
