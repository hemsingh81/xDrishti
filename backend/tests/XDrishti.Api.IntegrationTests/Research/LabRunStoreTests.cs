using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// The Lab run table as the runtime role (spec P5-02, criterion 10; ADR 0058) on a throw-away server where <c>xd_app</c> exists with the default privileges of the init script: it inserts a run and updates only the state columns, never deletes; the
/// store works with exactly those rights; the key is held by a queued, running or succeeded run only; two claims never share a run.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabRunStoreTests(ApiFactory factory, ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] IdentityColumns = ["id", "key", "strategy_id", "version", "strategy_hash", "universe", "window_from", "window_to", "method", "config_hash", "engine_version", "labels", "created_at"];

    private static readonly string[] StateColumns = ["state", "stage", "progress_done", "progress_total", "sim_run_keys", "counts", "problems", "stages", "error", "host", "cancel_requested", "started_at", "finished_at", "updated_at"];

    private static readonly string Hash = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", server.App));

    private async Task ResetAsync()
    {
        await StoreKit.ExecuteAsync(server.Owner, "DELETE FROM lab.lab_results; DELETE FROM lab.lab_runs; DELETE FROM lab.strategy_versions; DELETE FROM lab.strategies;", Ct);
        await StoreKit.ExecuteAsync(server.Owner, "INSERT INTO lab.strategies (id, style, built_in, created_at) VALUES ('RUNSTORE', 'intraday', false, now()); INSERT INTO lab.strategy_versions (strategy_id, version, schema_version, hash, source_text, canonical_json, created_at) VALUES ('RUNSTORE', 1, 1, '" + Hash + "', 't', '{}', now());", Ct);
    }

    private static NewLabRun New(string key, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), key, "RUNSTORE", 1, Hash, new LabUniverse("symbols", null, "symbols as listed", 1, [new LabInstrument(Guid.NewGuid(), "AAA")], false, null),
            new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "quick", new string('c', 64), "sim-5", ["Quick test: exploration, not a verdict."], DateTimeOffset.UtcNow);

    private static string Key(char c) => new(c, 64);

    private static async Task<string?> SqlStateAsync(string connectionString, string sql)
    {
        try
        {
            await StoreKit.ExecuteAsync(connectionString, sql, Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    private static async Task<T> WithStoreAsync<T>(WebApplicationFactory<Program> host, Func<ILabRunStore, Task<T>> action)
    {
        using var scope = host.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ILabRunStore>());
    }

    [Fact]
    public async Task The_runtime_role_reads_and_inserts_updates_only_the_state_columns_and_never_deletes()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        async Task<bool> HasAsync(string sql, params string[] args)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var (value, index) in args.Select((a, i) => (a, i)))
            {
                command.Parameters.AddWithValue("p" + index, value);
            }

            return (bool)(await command.ExecuteScalarAsync(Ct))!;
        }

        (await HasAsync("SELECT has_table_privilege('xd_app', 'lab.lab_runs', 'SELECT')")).ShouldBeTrue();
        (await HasAsync("SELECT has_table_privilege('xd_app', 'lab.lab_runs', 'INSERT')")).ShouldBeTrue();
        (await HasAsync("SELECT has_table_privilege('xd_app', 'lab.lab_runs', 'DELETE')")).ShouldBeFalse();
        (await HasAsync("SELECT has_table_privilege('xd_app', 'lab.lab_runs', 'TRUNCATE')")).ShouldBeFalse();
        (await HasAsync("SELECT has_table_privilege('xd_app', 'lab.lab_runs', 'UPDATE')")).ShouldBeFalse("only by column");
        foreach (var column in StateColumns)
        {
            (await HasAsync("SELECT has_column_privilege('xd_app', 'lab.lab_runs', @p0, 'UPDATE')", column)).ShouldBeTrue("update " + column);
        }

        foreach (var column in IdentityColumns)
        {
            (await HasAsync("SELECT has_column_privilege('xd_app', 'lab.lab_runs', @p0, 'UPDATE')", column)).ShouldBeFalse("update " + column);
        }

        (await HasAsync("SELECT has_table_privilege('xd_app', 'lab.strategy_versions', 'UPDATE')")).ShouldBeFalse("the migration did not loosen the strategy tables");
        (await HasAsync("SELECT has_table_privilege('xd_app', 'lab.strategies', 'DELETE')")).ShouldBeFalse("the migration did not loosen the strategy tables");
    }

    [Fact]
    public async Task As_xd_app_the_state_can_change_and_the_identity_and_the_rows_cannot()
    {
        await ResetAsync();
        var id = Guid.NewGuid();
        await StoreKit.ExecuteAsync(
            server.App,
            "INSERT INTO lab.lab_runs (id, key, strategy_id, version, strategy_hash, universe, window_from, window_to, method, config_hash, engine_version, state, progress_done, progress_total, sim_run_keys, problems, stages, labels, cancel_requested, created_at, updated_at) "
            + $"VALUES ('{id}', '{Key('1')}', 'RUNSTORE', 1, '{Hash}', '{{}}', '2025-05-01', '2026-04-30', 'quick', '{Key('c')}', 'sim-5', 'queued', 0, 0, '{{}}', '[]', '[]', '[]', false, now(), now())",
            Ct);

        (await SqlStateAsync(server.App, $"UPDATE lab.lab_runs SET state = 'running', stage = 'validate', host = 'xd-api', progress_done = 1, updated_at = now() WHERE id = '{id}'")).ShouldBeNull();
        (await SqlStateAsync(server.App, $"UPDATE lab.lab_runs SET key = '{Key('2')}' WHERE id = '{id}'")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.App, $"UPDATE lab.lab_runs SET strategy_hash = '{Key('2')}' WHERE id = '{id}'")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.App, "UPDATE lab.lab_runs SET created_at = now()")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.App, "DELETE FROM lab.lab_runs")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.App, "TRUNCATE lab.lab_runs")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.App, $"UPDATE lab.lab_runs SET state = 'paused' WHERE id = '{id}'")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.lab_runs WHERE state = 'running'", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task The_key_is_held_by_a_queued_running_or_succeeded_run_and_free_after_a_failed_cancelled_or_interrupted_one()
    {
        await ResetAsync();
        await using var host = Host();

        var first = await WithStoreAsync(host, s => s.InsertOrGetAsync(New(Key('3')), Ct).AsTask());
        var second = await WithStoreAsync(host, s => s.InsertOrGetAsync(New(Key('3')), Ct).AsTask());
        (await SqlStateAsync(server.Owner, $"INSERT INTO lab.lab_runs (id, key, strategy_id, version, strategy_hash, universe, window_from, window_to, method, config_hash, engine_version, state, sim_run_keys, problems, stages, labels, created_at, updated_at) VALUES (gen_random_uuid(), '{Key('3')}', 'RUNSTORE', 1, '{Hash}', '{{}}', '2025-05-01', '2026-04-30', 'quick', '{Key('c')}', 'sim-5', 'succeeded', '{{}}', '[]', '[]', '[]', now(), now())")).ShouldBe(PostgresErrorCodes.UniqueViolation);

        first.Created.ShouldBeTrue();
        second.Created.ShouldBeFalse();
        second.Run.Id.ShouldBe(first.Run.Id);
        foreach (var state in new[] { "failed", "cancelled", "interrupted" })
        {
            await StoreKit.ExecuteAsync(server.Owner, $"UPDATE lab.lab_runs SET state = '{state}' WHERE id = '{first.Run.Id}'", Ct);
            var next = await WithStoreAsync(host, s => s.InsertOrGetAsync(New(Key('3')), Ct).AsTask());
            next.Created.ShouldBeTrue(state);
            next.Run.Id.ShouldNotBe(first.Run.Id);
            await StoreKit.ExecuteAsync(server.Owner, $"UPDATE lab.lab_runs SET state = 'failed' WHERE id = '{next.Run.Id}'", Ct);
        }
    }

    [Fact]
    public async Task Posts_racing_for_one_key_leave_exactly_one_row()
    {
        await ResetAsync();
        await using var host = Host();

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => WithStoreAsync(host, s => s.InsertOrGetAsync(New(Key('4')), Ct).AsTask())));

        results.Count(r => r.Created).ShouldBe(1);
        results.Select(r => r.Run.Id).Distinct().Count().ShouldBe(1);
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.lab_runs", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task The_queue_hands_the_oldest_run_to_one_claimer_only_and_the_life_of_a_run_works_with_the_rights_of_xd_app()
    {
        await ResetAsync();
        await using var host = Host();
        var a = await WithStoreAsync(host, s => s.InsertOrGetAsync(New(Key('5')), Ct).AsTask());
        var b = await WithStoreAsync(host, s => s.InsertOrGetAsync(New(Key('6')), Ct).AsTask());
        await StoreKit.ExecuteAsync(server.Owner, $"UPDATE lab.lab_runs SET created_at = now() - interval '1 hour' WHERE id = '{a.Run.Id}'", Ct);

        var claims = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => WithStoreAsync(host, s => s.ClaimNextAsync("xd-api", DateTimeOffset.UtcNow, Ct).AsTask())));

        claims.Count(c => c is not null).ShouldBe(2, "two queued runs, each claimed once");
        claims.Where(c => c is not null).Select(c => c!.Id).Order().ShouldBe(new[] { a.Run.Id, b.Run.Id }.Order());
        claims.ShouldAllBe(c => c == null || (c.State == LabRunState.Running && c.Stage == "validate"));

        var id = a.Run.Id;
        (await WithStoreAsync(host, s => s.ReportAsync(id, "quick_test", 3, 12, [new LabStageLog("quick_test", DateTimeOffset.UtcNow, null, 0)], DateTimeOffset.UtcNow, Ct).AsTask())).ShouldBeTrue();
        var mid = await WithStoreAsync(host, s => s.GetAsync(id, Ct).AsTask());
        mid!.Done.ShouldBe(3);
        mid.Total.ShouldBe(12);
        mid.Stages.Single().Name.ShouldBe("quick_test");

        var finished = await WithStoreAsync(host, s => s.FinishAsync(new LabFinish(id, LabRunState.Succeeded, "quick_test", ["k"], new LabCounts(1, 1, 0, 5, 0), [new LabProblem("compile", "p", "c", "m", "warning", 1, 2)], [], null, DateTimeOffset.UtcNow), Ct).AsTask());
        var again = await WithStoreAsync(host, s => s.FinishAsync(new LabFinish(id, LabRunState.Failed, "quick_test", [], null, [], [], "x", DateTimeOffset.UtcNow), Ct).AsTask());
        var end = await WithStoreAsync(host, s => s.GetAsync(id, Ct).AsTask());

        finished.ShouldBeTrue();
        again.ShouldBeFalse("a finished run never changes");
        end!.State.ShouldBe(LabRunState.Succeeded);
        end.SimRunKeys.ShouldBe(["k"]);
        end.Counts.ShouldBe(new LabCounts(1, 1, 0, 5, 0));
        end.Problems.Single().Severity.ShouldBe("warning");
        end.FinishedAt.ShouldNotBeNull();
        end.Universe.Instruments.Single().Symbol.ShouldBe("AAA");
        end.From.ShouldBe(new DateOnly(2025, 5, 1));
        (await WithStoreAsync(host, s => s.ListAsync("RUNSTORE", LabRunState.Succeeded, 10, Ct).AsTask())).Single().Id.ShouldBe(id);
        (await WithStoreAsync(host, s => s.ListAsync("OTHER", null, 10, Ct).AsTask())).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancel_and_interrupt_move_only_the_runs_they_should()
    {
        await ResetAsync();
        await using var host = Host();
        var queued = (await WithStoreAsync(host, s => s.InsertOrGetAsync(New(Key('7')), Ct).AsTask())).Run.Id;
        var running = (await WithStoreAsync(host, s => s.InsertOrGetAsync(New(Key('8')), Ct).AsTask())).Run.Id;
        var other = (await WithStoreAsync(host, s => s.InsertOrGetAsync(New(Key('9')), Ct).AsTask())).Run.Id;
        await StoreKit.ExecuteAsync(server.Owner, $"UPDATE lab.lab_runs SET state = 'running', host = 'xd-api' WHERE id = '{running}'; UPDATE lab.lab_runs SET state = 'running', host = 'another-host' WHERE id = '{other}'", Ct);

        var cancelQueued = await WithStoreAsync(host, s => s.RequestCancelAsync(queued, DateTimeOffset.UtcNow, Ct).AsTask());
        var cancelRunning = await WithStoreAsync(host, s => s.RequestCancelAsync(running, DateTimeOffset.UtcNow, Ct).AsTask());
        var cancelAgain = await WithStoreAsync(host, s => s.RequestCancelAsync(queued, DateTimeOffset.UtcNow, Ct).AsTask());
        var cancelNone = await WithStoreAsync(host, s => s.RequestCancelAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, Ct).AsTask());
        var flagged = await WithStoreAsync(host, s => s.IsCancelRequestedAsync(running, Ct).AsTask());
        var interrupted = await WithStoreAsync(host, s => s.InterruptRunningAsync("xd-api", DateTimeOffset.UtcNow, Ct).AsTask());

        cancelQueued.ShouldBe(LabCancelOutcome.Cancelled);
        cancelRunning.ShouldBe(LabCancelOutcome.Requested);
        cancelAgain.ShouldBe(LabCancelOutcome.NotCancellable);
        cancelNone.ShouldBe(LabCancelOutcome.NotFound);
        flagged.ShouldBeTrue();
        interrupted.ShouldBe([running]);
        (await WithStoreAsync(host, s => s.GetAsync(queued, Ct).AsTask()))!.State.ShouldBe(LabRunState.Cancelled);
        (await WithStoreAsync(host, s => s.GetAsync(running, Ct).AsTask()))!.State.ShouldBe(LabRunState.Interrupted);
        (await WithStoreAsync(host, s => s.GetAsync(other, Ct).AsTask()))!.State.ShouldBe(LabRunState.Running);
    }
}
