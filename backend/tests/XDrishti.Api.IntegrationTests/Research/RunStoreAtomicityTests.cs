using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Research;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Criterion 8 of P3-10: the write is atomic. Each test runs on its own scratch database where the owner changes the table first (a constraint, a trigger), so that the copy fails or loses a row without any seam in the code: nothing is left of the
/// header, and the same key can be stored afterwards.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class RunStoreAtomicityTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(long Runs, long Trades)> CountsAsync(ScratchDatabase scratch) =>
        (await StoreKit.CountAsync(scratch.ConnectionString, "SELECT count(*) FROM research.sim_runs", Ct), await StoreKit.CountAsync(scratch.ConnectionString, "SELECT count(*) FROM research.sim_trades", Ct));

    [Fact]
    public async Task A_trade_row_that_violates_a_constraint_rolls_back_the_header_and_the_key_is_not_consumed()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_atomic_constraint_probe", Ct);
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo());
        await StoreKit.ExecuteAsync(scratch.ConnectionString, "ALTER TABLE research.sim_trades ADD CONSTRAINT ck_probe CHECK (ordinal < 5)", Ct);

        var failed = await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask());

        failed.IsFailure.ShouldBeTrue();
        failed.Error.Code.ShouldBe("store.write_failed");
        failed.Error.Message.ShouldContain("ck_probe");
        (await CountsAsync(scratch)).ShouldBe((0, 0));

        await StoreKit.ExecuteAsync(scratch.ConnectionString, "ALTER TABLE research.sim_trades DROP CONSTRAINT ck_probe", Ct);
        var again = await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask());
        again.IsSuccess.ShouldBeTrue();
        again.Value.WasAlreadyStored.ShouldBeFalse();
        (await CountsAsync(scratch)).ShouldBe((1, run.Trades.Count + run.OpenAtEnd.Count));
    }

    [Fact]
    public async Task A_count_that_differs_from_the_trades_of_the_run_rolls_back_with_write_failed()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_atomic_count_probe", Ct);
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo());
        await StoreKit.ExecuteAsync(
            scratch.ConnectionString,
            """
            CREATE FUNCTION research.drop_one_row() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.ordinal = 3 AND NOT NEW.open_at_end THEN RETURN NULL; END IF; RETURN NEW; END $$;
            CREATE TRIGGER drop_one_row BEFORE INSERT ON research.sim_trades FOR EACH ROW EXECUTE FUNCTION research.drop_one_row();
            """,
            Ct);

        var failed = await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask());

        failed.IsFailure.ShouldBeTrue();
        failed.Error.Code.ShouldBe("store.write_failed");
        (await CountsAsync(scratch)).ShouldBe((0, 0));

        await StoreKit.ExecuteAsync(scratch.ConnectionString, "DROP TRIGGER drop_one_row ON research.sim_trades; DROP FUNCTION research.drop_one_row()", Ct);
        (await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask())).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_cancellation_during_the_copy_leaves_both_tables_as_they_were()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_atomic_cancel_probe", Ct);
        var run = StoredRunFixtures.Synthetic(60_000, costed: true);
        var toStore = StoreKit.ToStore(run);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(40));

        await Should.ThrowAsync<OperationCanceledException>(() => StoreKit.WithStoreAsync(scratch.Host.Services, s => s.StoreAsync(toStore, cancel.Token).AsTask()));

        (await CountsAsync(scratch)).ShouldBe((0, 0));
        var after = await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.StoreAsync(toStore, Ct).AsTask());
        after.IsSuccess.ShouldBeTrue();
        (await CountsAsync(scratch)).ShouldBe((1, run.Trades.Count + run.OpenAtEnd.Count));
    }

    [Fact]
    public async Task A_database_without_the_migration_names_the_migration()
    {
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo());
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_atomic_missing_probe", Ct);
        await StoreKit.ExecuteAsync(scratch.ConnectionString, "DROP TABLE research.experiments; DROP TABLE research.exit_selection_folds; DROP TABLE research.exit_selections; DROP TABLE research.sim_comparisons; DROP TABLE research.sim_trade_leg_charges; DROP TABLE research.sim_trade_legs; DROP TABLE research.sim_trades; DROP TABLE research.sim_runs", Ct);

        var failed = await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask());

        failed.Error.Code.ShouldBe("store.write_failed");
        failed.Error.Message.ShouldContain("AddResearchResultStore");
    }

    [Fact]
    public async Task No_log_line_of_the_success_conflict_or_failure_path_holds_a_symbol_a_strategy_definition_or_the_record_text()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_log_probe", Ct);
        var source = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo(), fixtureEdges: false);
        var first = source.Trades[0] with { Trade = source.Trades[0].Trade with { Symbol = "NSE:SENTINELSYM" } };
        var run = source with { Trades = [first, .. source.Trades.Skip(1)], Strategy = source.Strategy with { DefinitionJson = "{\"id\":\"SENTINELDEF\"}" } };
        var other = run with { RunKey = new string('f', 64) };
        var record = StoreKit.Record(run);

        async Task<XDrishti.Domain.Common.Result<StoreOutcome>> Handle(XDrishti.Domain.Research.SimulationRun r, RunRecordText text)
        {
            using var scope = scratch.Host.Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Messaging.ICommandHandler<XDrishti.Application.Research.StoreRunCommand, StoreOutcome>>();
            return await handler.HandleAsync(new XDrishti.Application.Research.StoreRunCommand(r, text, "cli:simulate"), Ct);
        }

        (await Handle(run, record)).IsSuccess.ShouldBeTrue();
        (await Handle(run, new RunRecordText(record.Json, record.RecordSha256, new string('9', 64)))).Error.Code.ShouldBe("store.run_conflict");
        await StoreKit.ExecuteAsync(scratch.ConnectionString, "DROP TABLE research.experiments; DROP TABLE research.exit_selection_folds; DROP TABLE research.exit_selections; DROP TABLE research.sim_comparisons; DROP TABLE research.sim_trade_leg_charges; DROP TABLE research.sim_trade_legs; DROP TABLE research.sim_trades; DROP TABLE research.sim_runs", Ct);
        (await Handle(other, StoreKit.Record(other))).Error.Code.ShouldBe("store.write_failed");

        var lines = factory.Logs.All.Where(l => l.Contains(run.RunKey[..12], StringComparison.Ordinal) || l.Contains(other.RunKey[..12], StringComparison.Ordinal)).ToList();
        foreach (var marker in new[] { "store.started", "store.completed", "store.conflict", "store.failed" })
        {
            lines.ShouldContain(l => l.Contains(marker, StringComparison.Ordinal), marker);
        }

        lines.ShouldAllBe(l => !l.Contains("SENTINEL", StringComparison.Ordinal) && !l.Contains("schema_version", StringComparison.Ordinal) && !l.Contains("definition", StringComparison.OrdinalIgnoreCase));
    }
}
