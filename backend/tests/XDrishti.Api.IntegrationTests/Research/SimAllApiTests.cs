using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.SimAll;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research.SimAll;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// AU-12 (ADR 0243, ADR 0248) end to end on a real database and the real engine with a small seeded universe: nothing starts before the backfill is complete, a start makes one chunk per (strategy version, instrument), a second start is a conflict, the job writes training runs only
/// (invisible to the evidence reader, visible to the one non-evidence reader), one sweep ledger row per version, a repeat writes nothing more, pause and cancel work, and the store refuses a chunk whose window reaches the hold-out.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class SimAllApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Route = new("/api/simulate-all", UriKind.Relative);

    private static readonly Uri Current = new("/api/simulate-all/current", UriKind.Relative);

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
        const string training = "SELECT id FROM research.sim_runs WHERE purpose = 'training'";
        await ExecuteAsync(connection, $"DELETE FROM research.sim_all_chunks; DELETE FROM research.sim_all_jobs; DELETE FROM research.sim_trade_leg_charges WHERE run_id IN ({training}); DELETE FROM research.sim_trade_legs WHERE run_id IN ({training}); DELETE FROM research.sim_trades WHERE run_id IN ({training}); DELETE FROM research.sim_runs WHERE purpose = 'training'; DELETE FROM lab.test_ledger WHERE trial_kind = 'training_sweep'; DELETE FROM lab.strategy_versions WHERE strategy_id IN (SELECT id FROM lab.strategies WHERE built_in); DELETE FROM lab.strategies WHERE built_in; DELETE FROM lab.holdout_unlocks; DELETE FROM lab.holdout_blocks; DELETE FROM accounts.account_events WHERE event LIKE 'SimulateAll%'");
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

    private async Task SeedUniverseAsync()
    {
        (await StrategySeeding.SeedAsync(Factory.Services, Ct)).ShouldBeGreaterThan(0); // the built-in strategies as stored text, as the deployment step does
        await TrackedAsync("ABB", ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.F1(ReplayDays.D2), ReplayDays.Flat(ReplayDays.D3, 105.00)));
        await TrackedAsync("TCS", ReplayDays.Join(ReplayDays.F2(ReplayDays.D1), ReplayDays.Flat(ReplayDays.D2, 98.50), ReplayDays.Flat(ReplayDays.D3, 98.50)));
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO lab.holdout_blocks (start_date, history_first, history_last, length_months, setting_hash, created_at) VALUES ('2031-01-01', '2026-03-02', '2031-06-30', 12, repeat('a', 64), now())");
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    private async Task<JsonElement> PostAsync(string path, HttpStatusCode expected) =>
        await ReadAsync(await Client.PostAsync(new Uri("/api/simulate-all" + path, UriKind.Relative), null, Ct), expected);

    private Task<long> CountAsync(string table, string where = "true") => ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE {where}");

    private Task TickAsync() => Factory.Services.GetRequiredService<SimAllRunner>().TickAsync(Ct);

    [Fact]
    public async Task Nothing_starts_without_a_session_or_before_the_backfill_is_complete_and_nothing_is_created()
    {
        using var anonymous = await Factory.CreateAnonymousClient().PostAsync(Route, null, Ct);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var refused = await PostAsync(string.Empty, HttpStatusCode.UnprocessableEntity);

        refused.GetProperty("code").GetString().ShouldBe(SimAllErrors.BackfillIncompleteCode);
        (await CountAsync("research.sim_all_jobs")).ShouldBe(0);
        (await CountAsync("lab.test_ledger", "trial_kind = 'training_sweep'")).ShouldBe(0);
        (await ReadAsync(await Client.GetAsync(Current, Ct), HttpStatusCode.NotFound)).GetProperty("code").GetString().ShouldBe(SimAllErrors.NoActiveJobCode);
    }

    [Fact]
    public async Task A_job_runs_every_latest_strategy_over_every_instrument_as_training_runs_and_a_repeat_writes_nothing_more()
    {
        await SeedUniverseAsync();
        var versions = await ScalarAsync<long>("SELECT count(*) FROM lab.strategies s WHERE EXISTS (SELECT 1 FROM lab.strategy_versions v WHERE v.strategy_id = s.id)");

        var started = await PostAsync(string.Empty, HttpStatusCode.Accepted);

        started.GetProperty("chunksTotal").GetInt32().ShouldBe((int)versions * 2);
        started.GetProperty("state").GetString().ShouldBe("running");
        started.GetProperty("windowTo").GetString().ShouldBe("2030-12-01");
        (await PostAsync(string.Empty, HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe(SimAllErrors.ActiveCode);
        (await CountAsync("accounts.account_events", "event = 'SimulateAllStarted'")).ShouldBe(1);

        await TickAsync();

        var done = await ReadAsync(await Client.GetAsync(Current, Ct), HttpStatusCode.OK);
        done.GetProperty("state").GetString().ShouldBeOneOf("completed", "completed_with_failures");
        done.GetProperty("chunksDone").GetInt32().ShouldBeGreaterThan(0);
        (done.GetProperty("chunksDone").GetInt32() + done.GetProperty("chunksFailed").GetInt32()).ShouldBe(done.GetProperty("chunksTotal").GetInt32());
        done.GetProperty("chunksRemaining").GetInt32().ShouldBe(0);
        done.GetProperty("coveredFrom").GetString().ShouldNotBeNull();
        var stored = await CountAsync("research.sim_runs", "purpose = 'training'");
        stored.ShouldBe(done.GetProperty("chunksDone").GetInt32());
        (await CountAsync("research.sim_runs", "purpose = 'training' AND created_by <> 'simulate-all'")).ShouldBe(0);
        (await CountAsync("lab.test_ledger", "trial_kind = 'training_sweep'")).ShouldBe(versions);

        using var scope = Factory.Services.CreateScope();
        var key = await ScalarAsync<string>("SELECT run_key FROM research.sim_all_chunks WHERE state = 'done' LIMIT 1");
        (await scope.ServiceProvider.GetRequiredService<IRunStore>().FindAsync(key, Ct)).ShouldBeNull("the evidence reader does not see a training run");
        (await scope.ServiceProvider.GetRequiredService<ITrainingRunReader>().FindAsync(key, Ct)).ShouldNotBeNull();

        // the same job again: the same run keys, so the store writes nothing more
        await PostAsync(string.Empty, HttpStatusCode.Accepted);
        await TickAsync();
        (await CountAsync("research.sim_runs", "purpose = 'training'")).ShouldBe(stored);
        (await CountAsync("research.sim_all_jobs")).ShouldBe(2);
    }

    [Fact]
    public async Task Pause_stops_the_work_resume_goes_on_and_cancel_leaves_the_rest_pending()
    {
        await SeedUniverseAsync();
        await PostAsync(string.Empty, HttpStatusCode.Accepted);

        (await PostAsync("/pause", HttpStatusCode.OK)).GetProperty("pauseReason").GetString().ShouldBe("owner");
        await TickAsync();
        (await CountAsync("research.sim_all_chunks", "state = 'done'")).ShouldBe(0);

        (await PostAsync("/resume", HttpStatusCode.OK)).GetProperty("state").GetString().ShouldBe("running");
        (await PostAsync("/resume", HttpStatusCode.Conflict)).GetProperty("code").GetString().ShouldBe(SimAllErrors.NotPausedCode);
        (await PostAsync("/cancel", HttpStatusCode.OK)).GetProperty("state").GetString().ShouldBe("cancelled");
        await TickAsync();

        (await CountAsync("research.sim_all_chunks", "state = 'pending'")).ShouldBeGreaterThan(0);
        (await CountAsync("research.sim_runs", "purpose = 'training'")).ShouldBe(0);
        (await PostAsync("/cancel", HttpStatusCode.NotFound)).GetProperty("code").GetString().ShouldBe(SimAllErrors.NoActiveJobCode);
        (await CountAsync("accounts.account_events", "event IN ('SimulateAllPaused', 'SimulateAllResumed', 'SimulateAllCancelled')")).ShouldBe(3);
    }

    [Fact]
    public async Task A_restart_puts_a_chunk_left_running_back_and_the_job_finishes_without_redoing_done_ones()
    {
        await SeedUniverseAsync();
        await PostAsync(string.Empty, HttpStatusCode.Accepted);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE research.sim_all_chunks SET state = 'running', started_at = now() WHERE ordinal = 1");
        }

        var runner = Factory.Services.GetRequiredService<SimAllRunner>();
        await runner.RecoverAsync(Ct);
        await runner.TickAsync(Ct);

        (await CountAsync("research.sim_all_chunks", "state IN ('pending', 'running')")).ShouldBe(0);
        (await CountAsync("research.sim_all_chunks", "ordinal = 1 AND state IN ('done', 'failed')")).ShouldBe(1);
    }

    [Fact]
    public async Task The_store_refuses_a_chunk_whose_window_reaches_the_hold_out_and_the_chunk_fails_with_that_reason()
    {
        await SeedUniverseAsync();
        var instrument = await ScalarAsync<Guid>("SELECT instrument_id FROM market.tracked_instruments LIMIT 1");
        var hash = await ScalarAsync<string>("SELECT hash FROM lab.strategy_versions WHERE strategy_id = 'ORB' ORDER BY version DESC LIMIT 1");
        var job = new SimAllJob(Guid.NewGuid(), SimAllStates.Running, null, ReplayDays.D1, new DateOnly(2031, 2, 1), new DateOnly(2031, 1, 1), 2, "owner:probe@example.com", Now, Now, null);
        using var scope = Factory.Services.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISimAllChunkExecutor>().ExecuteAsync(job, new SimAllChunk(job.Id, "ORB", 1, hash, instrument), Ct);

        result.State.ShouldBe(SimAllStates.Failed);
        result.FailCode.ShouldBe(StoreErrors.TrainingWindowRefusedCode);
        (await CountAsync("research.sim_runs", "purpose = 'training'")).ShouldBe(0);
    }

    [Fact]
    public async Task The_database_allows_one_active_job_and_keeps_a_finished_one_final()
    {
        await SeedUniverseAsync();
        await PostAsync(string.Empty, HttpStatusCode.Accepted);
        await using var connection = await OpenAsync();

        var second = await Should.ThrowAsync<Npgsql.PostgresException>(() => ExecuteAsync(connection, "INSERT INTO research.sim_all_jobs (id, state, window_from, window_to, hold_out_start, workers, started_by, created_at, updated_at) VALUES (gen_random_uuid(), 'running', '2020-01-01', '2020-02-01', '2021-01-01', 2, 'owner:x@y.z', now(), now())"));
        second.SqlState.ShouldBe(Npgsql.PostgresErrorCodes.UniqueViolation);

        await PostAsync("/cancel", HttpStatusCode.OK);
        var reopened = await Should.ThrowAsync<Npgsql.PostgresException>(() => ExecuteAsync(connection, "UPDATE research.sim_all_jobs SET state = 'running', pause_reason = NULL, finished_at = NULL"));
        reopened.MessageText.ShouldContain("finished simulate-everything job");
    }
}
