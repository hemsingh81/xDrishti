using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning.Jobs;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Learning;

/// <summary>
/// Spec P8-18 Part B (ADR 0129) end to end on a real database and the real engine: the capability and the password come first, one job at a time, the daily limit, the replay of a stored two-instrument run with ONE exit changed through the existing simulation, store and judge, a cancel,
/// an honest failure that is not a trial, the proof that the unchanged rebuild is the baseline, the proof that nothing that configures anything changed, and the database guards (the capability trigger, the one-active index, the frozen final state, the grants of the runtime role).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExperimentJobApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Start = new("/api/experiments", UriKind.Relative);

    private const string BaselineActor = "test:p8-18";

    private const string OwnerActor = "owner:" + ApiFactory.OwnerEmail;

    private static readonly string[] ConfigTables =
    [
        "lab.strategies", "lab.strategy_versions", "workflow.autonomy_settings", "platform.app_settings", "platform.service_schedules", "research.learning_changelog", "research.hypotheses", "research.lessons", "research.cell_stats",
        "research.model_versions", "research.model_designations", "trading.plans", "trading.plan_items", "trading.plan_events", "workflow.approvals", "platform.capability_changes", "workflow.charge_sheet_events", "research.exit_selections",
    ];

    private static readonly string[] Rewrites =
    [
        "DELETE FROM research.experiment_jobs",
        "TRUNCATE research.experiment_jobs",
        "UPDATE research.experiment_jobs SET job_key = repeat('2', 64)",
        "UPDATE research.experiment_jobs SET created_by = 'owner:other@y.z'",
        "UPDATE research.experiment_jobs SET baseline_run_key = repeat('b', 64)",
    ];

    private static readonly string[] AllowedColumns = ["state = 'running'", "stage = 'x'", "progress_done = 1", "progress_total = 1", "cancel_requested = true", "candidate_run_key = repeat('c', 64)", "experiment_id = 1", "error_code = 'x'", "error_text = 'x'", "replay_ms = 1", "compare_ms = 1", "other_heavy_jobs_seen = true", "started_at = now()", "finished_at = now()", "host = 'x'", "updated_at = now()"];

    private static readonly string[] RefusedColumns = ["reason = 'x'", "created_by = 'owner:z@y.z'", "candidate_value = '9'", "baseline_run_key = repeat('b', 64)", "knob_name = 'x'", "knob_kind = 'exit'", "baseline_value = '9'", "hold_out_months = 1", "instruments = 9", "job_key = repeat('3', 64)", "created_at = now()", "id = gen_random_uuid()"];

    private static readonly double[] TwoValues = [3.0, 4.0];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanAsync();
    }

    private async Task CleanAsync()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.experiment_jobs; DELETE FROM research.lessons; DELETE FROM research.hypotheses; DELETE FROM research.experiments; DELETE FROM research.sim_trade_leg_charges WHERE run_id IN (SELECT id FROM research.sim_runs WHERE created_by = 'test:p8-18' OR created_by LIKE 'experiment:%'); DELETE FROM research.sim_trade_legs WHERE run_id IN (SELECT id FROM research.sim_runs WHERE created_by = 'test:p8-18' OR created_by LIKE 'experiment:%'); DELETE FROM research.sim_trades WHERE run_id IN (SELECT id FROM research.sim_runs WHERE created_by = 'test:p8-18' OR created_by LIKE 'experiment:%'); DELETE FROM research.sim_runs WHERE created_by = 'test:p8-18' OR created_by LIKE 'experiment:%'; DELETE FROM platform.app_settings WHERE key LIKE 'research.cost_sheet.%'");
    }

    private async Task LoadAsync(Guid id, IEnumerable<Bar> bars)
    {
        using var scope = Factory.Services.CreateScope();
        await using var session = await scope.ServiceProvider.GetRequiredService<IBarStore>().BeginMergeAsync(id, BarSource.Dhan, Ct);
        await session.AddAsync(bars.ToArray(), Ct);
        await session.CompleteAsync(null, Ct);
    }

    private async Task<Guid> InstrumentAsync(string ticker, IEnumerable<Bar> bars)
    {
        var id = await SeedAsync(ticker);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.instruments SET tick_size = 5 WHERE id = @id", ("id", id));
        }

        await LoadAsync(id, bars);
        return id;
    }

    private async Task<IReadOnlyList<Guid>> SeedAsync()
    {
        var a = await InstrumentAsync("ABB", ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.F1(ReplayDays.D2), ReplayDays.Flat(ReplayDays.D3, 105.00)));
        var b = await InstrumentAsync("TCS", ReplayDays.Join(ReplayDays.F2(ReplayDays.D1), ReplayDays.Flat(ReplayDays.D2, 98.50), ReplayDays.Flat(ReplayDays.D3, 98.50)));
        return [a, b];
    }

    private static SimulationRequest Request(IReadOnlyList<Guid> ids, CostChoice? costs = null) =>
        new("ORB", new Dictionary<string, double>(), "15m", ReplayDays.D1, ReplayDays.D3, ids, null, costs ?? CostChoice.Gross, false, null, new FillRulesInput(0.1, 1, 0.05, 2),
            new ExitChoice([new ExitCandidate("FIXED_RR", [new ExitParameterValue("rr", [2])])], null));

    /// <summary>Runs the baseline with its exit named and stores it, as <c>xd simulate --exit FIXED_RR:rr=2 --store</c> does.</summary>
    private async Task<SimulationRun> StoreBaselineAsync(IReadOnlyList<Guid> ids, CostChoice? costs = null)
    {
        using var scope = Factory.Services.CreateScope();
        var run = await scope.ServiceProvider.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>().HandleAsync(new RunSimulationQuery(Request(ids, costs)), Ct);
        run.IsSuccess.ShouldBeTrue(run.IsFailure ? run.Error.Message : string.Empty);
        var stored = await scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>().HandleAsync(new StoreRunCommand(run.Value, scope.ServiceProvider.GetRequiredService<IRunRecordWriter>().Write(run.Value), BaselineActor), Ct);
        stored.IsSuccess.ShouldBeTrue();
        return run.Value;
    }

    private async Task SwitchOnAsync()
    {
        using var response = await Client.PutAsJsonAsync(new Uri("/api/capabilities/experiments_ui", UriKind.Relative), new { enabled = true, reason = "to replay", password = Factory.OwnerPassword }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> PostAsync(string baseline, string? password, string name = "FIXED_RR.rr", double value = 3, string kind = "exit", string reason = "a wider target", int? holdOut = 0) =>
        Client.PostAsJsonAsync(Start, new { baselineRunKey = baseline, knob = new { kind, name, candidateValue = value }, holdOutMonths = holdOut, reason, password, actor = "owner:someone-else" }, Ct);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    private async Task DrainAsync()
    {
        using var scope = Factory.Services.CreateScope();
        await Factory.Services.GetRequiredService<ExperimentJobDispatcher>().DrainAsync(Ct);
        _ = scope;
    }

    private async Task<JsonElement> JobAsync(string id) => await ReadAsync(await Client.GetAsync(new Uri("/api/experiments/jobs/" + id, UriKind.Relative), Ct), HttpStatusCode.OK);

    private Task<long> CountAsync(string table, string where = "true") => ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE {where}");

    private async Task<Dictionary<string, string>> ChecksumsAsync()
    {
        var sums = new Dictionary<string, string>();
        foreach (var table in ConfigTables)
        {
            sums[table] = await ScalarAsync<string>($"SELECT coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), 'empty') FROM {table} t");
        }

        return sums;
    }

    [Fact]
    public async Task While_the_capability_is_off_the_start_is_refused_even_with_the_right_password_and_nothing_is_created()
    {
        var ids = await SeedAsync();
        var baseline = await StoreBaselineAsync(ids);
        var runs = await CountAsync("research.sim_runs");

        var answer = await ReadAsync(await PostAsync(baseline.RunKey, Factory.OwnerPassword), HttpStatusCode.Forbidden);

        answer.GetProperty("code").GetString().ShouldBe("capabilities.off");
        (await CountAsync("research.experiment_jobs")).ShouldBe(0);
        (await CountAsync("research.experiments")).ShouldBe(0);
        (await CountAsync("research.sim_runs")).ShouldBe(runs);
        (await CountAsync("accounts.account_events", "event = 'ExperimentJobStarted'")).ShouldBe(0);
    }

    [Fact]
    public async Task With_the_capability_on_a_wrong_or_missing_password_is_refused_and_creates_nothing_and_there_is_no_session_without_a_401()
    {
        var ids = await SeedAsync();
        var baseline = await StoreBaselineAsync(ids);
        await SwitchOnAsync();
        try
        {
            foreach (var password in new string?[] { null, "not-the-password" })
            {
                (await ReadAsync(await PostAsync(baseline.RunKey, password), HttpStatusCode.Unauthorized)).GetProperty("code").GetString().ShouldBe("auth.invalid_credentials");
            }

            (await CountAsync("research.experiment_jobs")).ShouldBe(0);
            using var anonymous = await Factory.CreateAnonymousClient().PostAsJsonAsync(Start, new { baselineRunKey = baseline.RunKey, password = Factory.OwnerPassword }, Ct);
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await Factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task A_capability_switched_off_between_the_handlers_check_and_the_insert_is_refused_by_the_trigger_and_the_store_answers_off()
    {
        var ids = await SeedAsync();
        var baseline = await StoreBaselineAsync(ids);
        using var scope = Factory.Services.CreateScope();

        var inserted = await scope.ServiceProvider.GetRequiredService<IExperimentJobStore>().InsertAsync(
            new NewExperimentJob(Guid.NewGuid(), new string('1', 64), baseline.RunKey, "exit", "FIXED_RR.rr", "2", "3", 0, "x", OwnerActor, 2, Now), 3, TimeSpan.FromHours(24), Ct);

        inserted.Status.ShouldBe(ExperimentJobInsertStatus.CapabilityOff);
        (await CountAsync("research.experiment_jobs")).ShouldBe(0);
    }

    [Fact]
    public async Task The_preview_is_read_only_and_tells_the_truth_before_the_click()
    {
        var ids = await SeedAsync();
        var baseline = await StoreBaselineAsync(ids);
        var before = await CountAsync("research.sim_runs");

        var preview = await ReadAsync(await Client.GetAsync(new Uri("/api/experiments/preview?baselineRunKey=" + baseline.RunKey, UriKind.Relative), Ct), HttpStatusCode.OK);

        preview.GetProperty("rebuildable").GetBoolean().ShouldBeTrue();
        preview.GetProperty("knobs").EnumerateArray().Select(k => k.GetProperty("name").GetString()).ShouldBe(["FIXED_RR.rr", "slippage.ticks", "slippage.bar_range_frac", "slippage.stop_exit_extra_ticks"]);
        preview.GetProperty("nextTrial").GetInt32().ShouldBe(1);
        preview.GetProperty("requiredImprovementR").GetDouble().ShouldBe(0.026, 1e-9);
        preview.GetProperty("bestPossibleOutcome").GetString().ShouldBe("Inconclusive");
        preview.GetProperty("minFoldsForBetter").GetInt32().ShouldBe(6);
        preview.GetProperty("replayEstimate").GetProperty("state").GetString().ShouldBe("not_evaluable");
        preview.GetProperty("jobsPerDay").GetInt32().ShouldBe(3);
        (await CountAsync("research.experiment_jobs")).ShouldBe(0);
        (await CountAsync("research.sim_runs")).ShouldBe(before);
        using var unknown = await Client.GetAsync(new Uri("/api/experiments/preview?baselineRunKey=" + new string('9', 64), UriKind.Relative), Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var bad = await Client.GetAsync(new Uri("/api/experiments/preview?baselineRunKey=zz", UriKind.Relative), Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_unchanged_rebuild_of_a_stored_run_is_the_baseline_itself_the_determinism_proof_of_the_request_rebuild()
    {
        var ids = await SeedAsync();
        var baseline = await StoreBaselineAsync(ids);
        using var scope = Factory.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunStore>();
        var header = (await runs.FindAsync(baseline.RunKey, Ct))!;
        var plan = await scope.ServiceProvider.GetRequiredService<ReplayPlanner>().PlanAsync(header, (await runs.ReadRecordTextAsync(baseline.RunKey, Ct))!, Ct);

        plan.IsSuccess.ShouldBeTrue(plan.IsFailure ? plan.Error.Message : string.Empty);
        var rebuilt = await scope.ServiceProvider.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>().HandleAsync(new RunSimulationQuery(plan.Value.BaselineRequest()), Ct);

        rebuilt.Value.RunKey.ShouldBe(baseline.RunKey);
    }

    [Fact]
    public async Task A_valid_start_runs_the_existing_replay_stores_the_candidate_once_and_records_one_experiment_and_changes_no_configuration_table()
    {
        var ids = await SeedAsync();
        var baseline = await StoreBaselineAsync(ids);
        await SwitchOnAsync();
        var sums = await ChecksumsAsync();
        var counts = new[] { "research.sim_runs", "research.sim_trades", "research.experiments", "research.experiment_jobs", "accounts.account_events" };
        var before = new List<long>();
        foreach (var table in counts)
        {
            before.Add(await CountAsync(table));
        }

        var accepted = await ReadAsync(await PostAsync(baseline.RunKey, Factory.OwnerPassword), HttpStatusCode.Accepted);
        var id = accepted.GetProperty("id").GetString()!;
        accepted.GetProperty("state").GetString().ShouldBe("queued");
        accepted.GetProperty("instruments").GetInt32().ShouldBe(2);
        (await CountAsync("research.experiment_jobs")).ShouldBe(1);
        await DrainAsync();

        var job = await JobAsync(id);
        job.GetProperty("state").GetString().ShouldBe("succeeded", job.ToString());
        job.GetProperty("stage").GetString().ShouldBe("compare");
        var candidate = job.GetProperty("candidateRunKey").GetString()!;
        candidate.ShouldNotBe(baseline.RunKey);
        job.GetProperty("replayMs").GetInt64().ShouldBeGreaterThanOrEqualTo(0);
        job.GetProperty("timing").GetProperty("text").GetString()!.ShouldContain("for 2 instruments over 2026-03-02 to 2026-03-04, measured on this machine");
        job.GetProperty("timing").GetProperty("targetStatus").GetString().ShouldBe("not_comparable");
        job.GetProperty("limits").EnumerateArray().Select(l => l.GetString()).ShouldContain("Nothing was changed. Applying a change is your own separate act.");
        (await ScalarAsync<string>($"SELECT created_by FROM research.sim_runs WHERE run_key = '{candidate}'")).ShouldBe("experiment:" + id);
        (await CountAsync("research.sim_runs", $"run_key = '{candidate}'")).ShouldBe(1);
        (await ScalarAsync<string>("SELECT created_by FROM research.experiments")).ShouldBe(OwnerActor);
        (await ScalarAsync<long>("SELECT experiment_id FROM research.experiment_jobs")).ShouldBe(await ScalarAsync<long>("SELECT id FROM research.experiments"));
        (await ScalarAsync<string>("SELECT candidate_run_key FROM research.experiments")).ShouldBe(candidate);

        var after = new List<long>();
        foreach (var table in counts)
        {
            after.Add(await CountAsync(table));
        }

        after[0].ShouldBe(before[0] + 1, "one candidate run");
        after[1].ShouldBeGreaterThan(before[1]);
        after[2].ShouldBe(before[2] + 1, "one experiment");
        after[3].ShouldBe(before[3] + 1, "one job");
        after[4].ShouldBe(before[4] + 1, "one audit event");
        (await ChecksumsAsync()).ShouldBe(sums, "nothing that configures anything changed");
        (await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'ExperimentJobStarted'")).ShouldBe("kind=exit instruments=2");
        (await ScalarAsync<string>("SELECT actor FROM accounts.account_events WHERE event = 'ExperimentJobStarted'")).ShouldBe(OwnerActor);

        // the identical input answers with that job (200), writes nothing and counts nothing
        var again = await ReadAsync(await PostAsync(baseline.RunKey, Factory.OwnerPassword), HttpStatusCode.OK);
        again.GetProperty("id").GetString().ShouldBe(id);
        (await CountAsync("research.experiment_jobs")).ShouldBe(1);
        (await CountAsync("research.experiments")).ShouldBe(1);

        // the list and the ledger read it; the stored candidate is flagged so the default report set leaves it out
        var list = await ReadAsync(await Client.GetAsync(new Uri("/api/experiments/jobs", UriKind.Relative), Ct), HttpStatusCode.OK);
        list.GetProperty("items").GetArrayLength().ShouldBe(1);
        using var scope = Factory.Services.CreateScope();
        var runs = await scope.ServiceProvider.GetRequiredService<IReportTradeSource>().ListRunsAsync(Ct);
        runs.Single(r => r.RunKey == candidate).IsExperimentCandidate.ShouldBeTrue();
        runs.Single(r => r.RunKey == baseline.RunKey).IsExperimentCandidate.ShouldBeFalse();
    }

    [Fact]
    public async Task One_job_at_a_time_two_starts_at_once_leave_one_active_row_and_a_cancel_of_a_queued_job_is_at_once_a_finished_one_is_refused_and_an_unknown_one_is_404()
    {
        var ids = await SeedAsync();
        var baseline = await StoreBaselineAsync(ids);
        await SwitchOnAsync();

        var answers = await Task.WhenAll(TwoValues.Select(v => PostAsync(baseline.RunKey, Factory.OwnerPassword, value: v)));

        answers.Select(a => a.StatusCode).Order().ShouldBe([HttpStatusCode.Accepted, HttpStatusCode.Conflict]);
        (await CountAsync("research.experiment_jobs", "state IN ('queued', 'running')")).ShouldBe(1);
        var busy = answers.Single(a => a.StatusCode == HttpStatusCode.Conflict);
        var problem = await busy.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("experiments.job_active");
        var activeId = await ScalarAsync<Guid>("SELECT id FROM research.experiment_jobs");
        problem.GetProperty("title").GetString()!.ShouldContain(activeId.ToString("D"));
        await using (var connection = await OpenAsync())
        {
            var second = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, $"INSERT INTO research.experiment_jobs (id, job_key, baseline_run_key, knob_kind, knob_name, baseline_value, candidate_value, hold_out_months, reason, state, instruments, created_by, created_at, updated_at) VALUES (gen_random_uuid(), repeat('2', 64), '{baseline.RunKey}', 'exit', 'FIXED_RR.rr', '2', '9', 0, 'x', 'queued', 2, 'owner:x@y.z', now(), now())"));
            second.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation, "the index is the backstop of the rule");
        }

        using var cancelled = await Client.PostAsync(new Uri($"/api/experiments/jobs/{activeId:D}/cancel", UriKind.Relative), null, Ct);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await JobAsync(activeId.ToString("D"))).GetProperty("state").GetString().ShouldBe("cancelled");
        using var again = await Client.PostAsync(new Uri($"/api/experiments/jobs/{activeId:D}/cancel", UriKind.Relative), null, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("experiments.job_not_cancellable");
        using var unknown = await Client.PostAsync(new Uri($"/api/experiments/jobs/{Guid.NewGuid():D}/cancel", UriKind.Relative), null, Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CountAsync("research.experiments")).ShouldBe(0);
        (await CountAsync("accounts.account_events", "event = 'ExperimentJobCancelled'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_running_job_is_asked_to_stop_stays_running_until_its_runner_sees_it_and_the_final_state_never_changes_again()
    {
        var ids = await SeedAsync();
        var baseline = await StoreBaselineAsync(ids);
        await SwitchOnAsync();
        var id = (await ReadAsync(await PostAsync(baseline.RunKey, Factory.OwnerPassword), HttpStatusCode.Accepted)).GetProperty("id").GetString()!;
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "UPDATE research.experiment_jobs SET state = 'running', stage = 'replay', started_at = now()");

        using var asked = await Client.PostAsync(new Uri($"/api/experiments/jobs/{id}/cancel", UriKind.Relative), null, Ct);

        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var job = await JobAsync(id);
        job.GetProperty("state").GetString().ShouldBe("running");
        job.GetProperty("cancelRequested").GetBoolean().ShouldBeTrue();
        await ExecuteAsync(connection, "UPDATE research.experiment_jobs SET state = 'cancelled', finished_at = now()");
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "UPDATE research.experiment_jobs SET state = 'queued'"))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "UPDATE research.experiment_jobs SET error_text = 'x'"))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_fourth_start_in_24_hours_is_refused_with_429_and_the_time_the_oldest_leaves_and_failed_and_cancelled_jobs_count()
    {
        var ids = await SeedAsync();
        var baseline = await StoreBaselineAsync(ids);
        await SwitchOnAsync();
        await using (var connection = await OpenAsync())
        {
            for (var i = 0; i < 3; i++)
            {
                await ExecuteAsync(connection, $"INSERT INTO research.experiment_jobs (id, job_key, baseline_run_key, knob_kind, knob_name, baseline_value, candidate_value, hold_out_months, reason, state, instruments, created_by, created_at, updated_at) VALUES (gen_random_uuid(), repeat('{i + 5}', 64), '{baseline.RunKey}', 'exit', 'FIXED_RR.rr', '2', '{i + 5}', 0, 'x', 'queued', 2, 'owner:x@y.z', '2026-10-12T06:30:00Z'::timestamptz - interval '{(i + 1) * 2} hours', now())");
                await ExecuteAsync(connection, $"UPDATE research.experiment_jobs SET state = '{(i == 0 ? "failed" : "cancelled")}', finished_at = now() WHERE job_key = repeat('{i + 5}', 64)");
            }
        }

        using var response = await PostAsync(baseline.RunKey, Factory.OwnerPassword, value: 3);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        problem.GetProperty("code").GetString().ShouldBe("experiments.daily_limit");
        problem.GetProperty("retryAfterSeconds").GetInt32().ShouldBeInRange(17 * 3600, 18 * 3600 + 120);
        (await CountAsync("research.experiment_jobs")).ShouldBe(3);
    }

    [Theory]
    [InlineData("parameter", "or_min", 10.0, "experiments.knob_not_allowed")]
    [InlineData("exit", "FIXED_RR.rr", 2.0, "experiments.knob_not_allowed")]
    [InlineData("slippage", "slippage.ticks", 0.5, "experiments.knob_not_allowed")]
    [InlineData("exit", "FIXED_RR.rr", 99.0, "experiments.knob_not_allowed")]
    public async Task A_knob_that_is_not_allowed_is_422_and_nothing_is_queued(string kind, string name, double value, string code)
    {
        var ids = await SeedAsync();
        var baseline = await StoreBaselineAsync(ids);
        await SwitchOnAsync();

        var answer = await ReadAsync(await PostAsync(baseline.RunKey, Factory.OwnerPassword, name, value, kind), HttpStatusCode.UnprocessableEntity);

        answer.GetProperty("code").GetString().ShouldBe(code);
        (await CountAsync("research.experiment_jobs")).ShouldBe(0);
        using var missing = await PostAsync(new string('9', 64), Factory.OwnerPassword);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var blank = await PostAsync(baseline.RunKey, Factory.OwnerPassword, reason: " ");
        blank.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_job_that_cannot_be_judged_fails_with_the_ledgers_own_code_keeps_the_stored_candidate_and_records_no_experiment_and_no_trial()
    {
        var ids = await SeedAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(FixtureSheet.Sheet, "test", DateTimeOffset.UnixEpoch.AddDays(20_000), Ct);
            (await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
        }

        var baseline = await StoreBaselineAsync(ids, CostChoice.Sheet(FixtureSheet.Id));
        await SwitchOnAsync();
        var id = (await ReadAsync(await PostAsync(baseline.RunKey, Factory.OwnerPassword), HttpStatusCode.Accepted)).GetProperty("id").GetString()!;

        // the stored sheet is edited in place between the start and the run: the candidate would be costed with other rates
        using (var scope = Factory.Services.CreateScope())
        {
            var other = CostSheet.Create(FixtureSheet.Id, "FIXTURE sheet (invented numbers)", "invented for tests", null, [TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.IntradayA, TestCostSheet.DeliveryA)]).Value;
            await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(other, "test", DateTimeOffset.UnixEpoch.AddDays(20_001), Ct);
            (await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
        }

        await DrainAsync();

        var job = await JobAsync(id);
        job.GetProperty("state").GetString().ShouldBe("failed", job.ToString());
        job.GetProperty("errorCode").GetString().ShouldBe("experiments.assumptions_differ");
        job.GetProperty("candidateRunKey").GetString().ShouldNotBeNullOrEmpty();
        job.GetProperty("experimentId").ValueKind.ShouldBe(JsonValueKind.Null);
        job.GetProperty("limits").EnumerateArray().Select(l => l.GetString()).ShouldContain("Judged nothing; not counted as a trial.");
        (await CountAsync("research.experiments")).ShouldBe(0);
        (await CountAsync("research.sim_runs", "created_by LIKE 'experiment:%'")).ShouldBe(1);
    }

    [Fact]
    public async Task The_database_refuses_a_job_while_the_capability_is_off_and_the_runtime_role_can_insert_and_move_only_the_state_columns_never_delete()
    {
        await AppRoleProbe.RunAsync(
            Factory,
            async connection =>
            {
                var insert = "INSERT INTO research.experiment_jobs (id, job_key, baseline_run_key, knob_kind, knob_name, baseline_value, candidate_value, hold_out_months, reason, state, instruments, created_by, created_at, updated_at) VALUES (gen_random_uuid(), repeat('1', 64), repeat('a', 64), 'exit', 'FIXED_RR.rr', '2', '3', 0, 'x', 'queued', 2, 'owner:x@y.z', now(), now())";

                // the capability trigger comes before the foreign key: with no row for experiments_ui the insert is refused as a check violation
                (await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, insert, Ct))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
                await AppRoleProbe.ExecuteAsync(connection, "INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES ('experiments_ui', true, 'probe', 'owner:probe@example.test')", Ct);
                (await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, insert, Ct))).SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation, "now only the missing baseline run stops it");
                await AppRoleProbe.ExecuteAsync(connection, "INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES ('experiments_ui', false, 'probe', 'owner:probe@example.test')", Ct);
                (await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, insert, Ct))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation, "switched off again: refused again");
                foreach (var statement in Rewrites)
                {
                    (await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, statement, Ct))).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
                }

                // the allowed columns work (a statement over no row proves the privilege) and every other column is refused at the column level
                foreach (var column in AllowedColumns)
                {
                    await AppRoleProbe.ExecuteAsync(connection, $"UPDATE research.experiment_jobs SET {column} WHERE false", Ct);
                }

                foreach (var column in RefusedColumns)
                {
                    (await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, $"UPDATE research.experiment_jobs SET {column} WHERE false", Ct))).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege, column);
                }
            },
            Ct);
    }
}
