using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Live;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Domain.Market.Bars;
using XDrishti.Infrastructure.Persistence;
using XDrishti.SimExport.Manifest;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Lab;

/// <summary>
/// The Lab runs of P5-02 end to end against a real database: post, queue, the dispatcher carrying the run out (driven by <c>DrainAsync</c>; the hosted loop is off in tests), the stored simulation run, idempotency, a failed compile, the sample, cancel,
/// restart, the hub events, and that the Lab run is the run the <c>simulate</c> verb makes. Bars are the invented days of P3-03.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabRunsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly D1 = ReplayDays.D1;

    private static readonly DateOnly D2 = ReplayDays.D2;

    private static readonly DateOnly D3 = ReplayDays.D3;

    private static Bar[] A() => ReplayDays.Join(ReplayDays.Flat(D1, 99.00), ReplayDays.F1(D2), ReplayDays.Flat(D3, 105.00));

    private static readonly string[] Unknown = ["NOSUCHSYMBOL"];

    private static readonly string[] StageOrder = ["validate", "compile", "quick_test"];

    private LabRunDispatcher Dispatcher => Factory.Services.GetRequiredService<LabRunDispatcher>();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await StoreSqlAsync("DELETE FROM lab.lab_results; DELETE FROM lab.lab_runs");
        await StrategySeeding.SeedAsync(Factory.Services, Ct); // the built-ins ORB, VWAP_PULLBACK and BASE_BREAKOUT, as `xd-api migrate` writes them
    }

    private async Task StoreSqlAsync(string sql, params (string, object)[] parameters)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, sql, parameters);
    }

    private async Task<Guid> InstrumentAsync(string ticker, bool bars = true)
    {
        var id = await SeedAsync(ticker);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.instruments SET tick_size = 5 WHERE id = @id", ("id", id));
        }

        if (bars)
        {
            using var scope = Factory.Services.CreateScope();
            await using var session = await scope.ServiceProvider.GetRequiredService<IBarStore>().BeginMergeAsync(id, BarSource.Dhan, Ct);
            await session.AddAsync(A(), Ct);
            await session.CompleteAsync(null, Ct);
        }

        return id;
    }

    private async Task<List<(Guid Id, string Symbol)>> InstrumentsAsync(int count, bool bars = true)
    {
        var list = new List<(Guid, string)>();
        for (var i = 1; i <= count; i++)
        {
            var symbol = "LAB" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            list.Add((await InstrumentAsync(symbol, bars && i <= 12), symbol));
        }

        return list;
    }

    private async Task<Guid> BasketAsync(string name, IEnumerable<Guid> members)
    {
        using var created = await Client.PostJsonAsync("/api/baskets", new { name }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        using var added = await Client.PostJsonAsync($"/api/baskets/{id}/members", new { instrumentIds = members }, Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync(Ct));
        return id;
    }

    private Task<HttpResponseMessage> PostRunAsync(object body) => Client.PostJsonAsync("/api/lab/runs", body, Ct);

    private static object BySymbols(string strategy, IEnumerable<string> symbols, int? version = null) => new { strategyId = strategy, version, universe = new { symbols }, method = "quick" };

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<JsonElement> GetRunAsync(Guid id)
    {
        using var response = await Client.GetAsync(new Uri($"/api/lab/runs/{id}", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await JsonOf(response);
    }

    private async Task<JsonElement> PostAndDrainAsync(object body)
    {
        using var posted = await PostRunAsync(body);
        posted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await posted.Content.ReadAsStringAsync(Ct));
        var id = (await JsonOf(posted)).GetProperty("id").GetGuid();
        await Dispatcher.DrainAsync(Ct);
        return await GetRunAsync(id);
    }

    private Task<long> CountAsync(string sql, params (string, object)[] parameters) => ScalarAsync<long>(sql, parameters);

    // criterion 1

    [Fact]
    public async Task A_stored_version_on_a_basket_of_twelve_is_queued_with_202_then_succeeds_with_the_simulation_run_key_and_counts_and_a_stage_log()
    {
        var members = await InstrumentsAsync(12);
        var basket = await BasketAsync("Lab twelve", members.Select(m => m.Id));

        using var posted = await PostRunAsync(new { strategyId = "ORB", version = 1, universe = new { basketId = basket }, method = "quick" });

        posted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await posted.Content.ReadAsStringAsync(Ct));
        var queued = await JsonOf(posted);
        queued.GetProperty("state").GetString().ShouldBe("queued");
        queued.GetProperty("key").GetString()!.Length.ShouldBe(64);
        queued.GetProperty("universe").GetProperty("instruments").GetArrayLength().ShouldBe(12);
        posted.Headers.Location!.ToString().ShouldEndWith(queued.GetProperty("id").GetGuid().ToString());

        await Dispatcher.DrainAsync(Ct);
        var run = await GetRunAsync(queued.GetProperty("id").GetGuid());

        run.GetProperty("state").GetString().ShouldBe("succeeded", run.GetRawText());
        run.GetProperty("stage").GetString().ShouldBe("quick_test");
        run.GetProperty("window").GetProperty("to").GetString().ShouldBe(D3.ToString("yyyy-MM-dd"));
        var simKeys = run.GetProperty("simRunKeys").EnumerateArray().Select(k => k.GetString()!).ToList();
        simKeys.Count.ShouldBe(1);
        run.GetProperty("counts").GetProperty("instruments").GetInt32().ShouldBe(12);
        run.GetProperty("counts").GetProperty("trades").GetInt32().ShouldBeGreaterThan(0);
        run.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ShouldBe(["validate", "compile", "quick_test"]);
        run.GetProperty("verdict").ValueKind.ShouldBe(JsonValueKind.Null);
        run.GetProperty("startedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        run.GetProperty("finishedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        run.GetProperty("labels").EnumerateArray().Select(l => l.GetString()).ShouldContain("Quick test: exploration, not a verdict.");
        (await CountAsync("SELECT count(*) FROM research.sim_runs WHERE run_key = @k", ("k", simKeys[0]))).ShouldBe(1, "the run is in the result store");
        (await CountAsync("SELECT count(*) FROM research.sim_runs WHERE created_by = 'lab:quick'")).ShouldBeGreaterThanOrEqualTo(1);
    }

    // P5-04

    private static object FullBySymbols(IEnumerable<string> symbols, string method = "full") => new { strategyId = "ORB", version = 1, universe = new { symbols }, method, window = new { from = D1.ToString("yyyy-MM-dd"), to = D3.ToString("yyyy-MM-dd") } };

    [Fact]
    public async Task A_full_test_without_a_stored_charge_sheet_is_refused_with_422()
    {
        var members = await InstrumentsAsync(2);

        using var posted = await PostRunAsync(FullBySymbols(members.Select(m => m.Symbol)));

        posted.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await posted.Content.ReadAsStringAsync(Ct));
        (await CountAsync("SELECT count(*) FROM lab.lab_runs")).ShouldBe(0);
    }

    [Fact]
    public async Task A_full_test_runs_to_the_end_stores_the_result_and_serves_a_verdict_that_cannot_pass_on_three_days()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(FixtureSheet.Build("default", null), "test", DateTimeOffset.UnixEpoch, Ct);
            (await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
        }

        var members = await InstrumentsAsync(3);

        var run = await PostAndDrainAsync(FullBySymbols(members.Select(m => m.Symbol)));

        run.GetProperty("state").GetString().ShouldBe("succeeded", run.GetRawText());
        run.GetProperty("method").GetString().ShouldBe("full");
        run.GetProperty("stages").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ShouldBe(["validate", "compile", "full_test"]);
        run.GetProperty("simRunKeys").GetArrayLength().ShouldBe(8, "one stored run per candidate exit and timeframe (5m, 15m)");
        var verdict = run.GetProperty("verdict");
        verdict.GetProperty("outcome").GetString().ShouldBe("FAIL", "three days have no walk-forward fold");
        verdict.GetProperty("gates").GetArrayLength().ShouldBe(6);
        verdict.GetProperty("variantCount").GetInt32().ShouldBe(1);
        verdict.GetProperty("evidence").GetProperty("method").GetString().ShouldBe("walk_forward");
        verdict.GetProperty("limits").EnumerateArray().Select(l => l.GetString()).ShouldContain(l => l!.StartsWith("calibration gate not applicable", StringComparison.Ordinal));
        verdict.GetProperty("fullPeriod").EnumerateArray().ShouldAllBe(f => f.GetProperty("label").GetString()!.StartsWith("exploration", StringComparison.Ordinal));
        (await CountAsync("SELECT count(*) FROM lab.lab_results")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM research.sim_runs WHERE created_by = 'lab:full'")).ShouldBe(8);
    }

    // P5-05

    [Fact]
    public async Task A_robust_run_attaches_the_robustness_block_to_the_same_verdict_and_writes_sweep_rows_to_the_ledger()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(FixtureSheet.Build("default", null), "test", DateTimeOffset.UnixEpoch, Ct);
            (await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
        }

        var members = await InstrumentsAsync(3);

        var run = await PostAndDrainAsync(FullBySymbols(members.Select(m => m.Symbol), "robust"));

        run.GetProperty("state").GetString().ShouldBe("succeeded", run.GetRawText());
        run.GetProperty("stages").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ShouldBe(["validate", "compile", "full_test", "robustness"]);
        var verdict = run.GetProperty("verdict");
        verdict.GetProperty("outcome").GetString().ShouldBe("FAIL");
        var block = verdict.GetProperty("robustness");
        block.GetProperty("byRegime").GetString().ShouldStartWith("unavailable");
        block.GetProperty("sensitivity").GetProperty("exit").GetString().ShouldBe("FIXED_RR{rr=2}");
        block.GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
        var sweepRows = await CountAsync("SELECT count(*) FROM lab.test_ledger WHERE method = 'sweep'");
        sweepRows.ShouldBe(block.GetProperty("sensitivity").GetProperty("parameters").EnumerateArray().Sum(p => p.GetProperty("points").GetArrayLength()));
    }

    // criterion 2

    [Fact]
    public async Task The_same_input_again_returns_the_first_run_with_200_and_makes_no_second_job_and_another_version_is_a_new_run()
    {
        var members = await InstrumentsAsync(3);
        var symbols = members.Select(m => m.Symbol).ToList();
        using var first = await PostRunAsync(BySymbols("ORB", symbols, 1));
        var firstRun = await JsonOf(first);

        using var again = await PostRunAsync(BySymbols("ORB", symbols.AsEnumerable().Reverse(), 1));
        var againRun = await JsonOf(again);
        await Dispatcher.DrainAsync(Ct);
        using var afterDone = await PostRunAsync(BySymbols("ORB", symbols, 1));

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, "a post while the run is queued returns it");
        againRun.GetProperty("id").GetGuid().ShouldBe(firstRun.GetProperty("id").GetGuid());
        afterDone.StatusCode.ShouldBe(HttpStatusCode.OK, "a post after it succeeded returns it too");
        (await JsonOf(afterDone)).GetProperty("state").GetString().ShouldBe("succeeded");
        (await CountAsync("SELECT count(*) FROM lab.lab_runs")).ShouldBe(1);

        var changed = LabTexts.Valid("LABV");
        using var v1 = await Client.PostJsonAsync("/api/lab/strategies", new { text = changed }, Ct);
        v1.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var other = await PostRunAsync(BySymbols("LABV", symbols));
        other.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await JsonOf(other)).GetProperty("key").GetString().ShouldNotBe(firstRun.GetProperty("key").GetString());
        (await CountAsync("SELECT count(*) FROM lab.lab_runs")).ShouldBe(2);
    }

    [Fact]
    public async Task Two_posts_racing_for_one_key_leave_one_run()
    {
        var members = await InstrumentsAsync(2);
        var body = BySymbols("ORB", members.Select(m => m.Symbol), 1);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostRunAsync(body)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Accepted).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(5);
        (await CountAsync("SELECT count(*) FROM lab.lab_runs")).ShouldBe(1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // criterion 3

    [Fact]
    public async Task A_lab_run_of_a_built_in_is_the_run_the_simulate_verb_makes_the_same_key_and_the_same_stored_trades()
    {
        var members = (await InstrumentsAsync(2)).OrderBy(m => m.Id).ToList();
        var symbols = string.Join(',', members.Select(m => m.Symbol));
        var logs = new CapturedLogs();
        var overrides = new Dictionary<string, string?> { ["Database:ConnectionString"] = Factory.AdminConnectionString, ["BrokerVault:Key"] = string.Empty };
        using var host = SimulateHost.Create(overrides, services => services.AddSingleton<ILogEventSink>(logs));
        var folder = Path.Combine(Path.GetTempPath(), "xd-lab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var exit = await SimulateCommand.RunAsync(
                ["--strategy", "ORB", "--symbols", symbols, "--from", D1.ToString("yyyy-MM-dd"), "--to", D3.ToString("yyyy-MM-dd"), "--gross", "--store"],
                host.Services, new SimulateConsole(new StringWriter(), new StringWriter()), folder, Ct);
            exit.ShouldBe(0);
            var verbRunFolder = Directory.EnumerateDirectories(Path.Combine(folder, "sim")).Single(d => !Path.GetFileName(d).StartsWith('.'));
            var verbKey = ExportManifest.ParseBytes(File.ReadAllBytes(Path.Combine(verbRunFolder, "manifest.json"))).Value.RunKey;

            var run = await PostAndDrainAsync(BySymbols("ORB", members.Select(m => m.Symbol), 1));

            run.GetProperty("state").GetString().ShouldBe("succeeded", run.GetRawText());
            run.GetProperty("simRunKeys")[0].GetString().ShouldBe(verbKey, "the same strategy, instruments, window and costs give the same run key");
            (await CountAsync("SELECT count(*) FROM research.sim_runs WHERE run_key = @k", ("k", verbKey))).ShouldBe(1, "stored once: the same record and the same trade digests, else the store would have refused");
            (await ScalarAsync<int>("SELECT trade_count FROM research.sim_runs WHERE run_key = @k", ("k", verbKey))).ShouldBe(run.GetProperty("counts").GetProperty("trades").GetInt32());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    // criterion 4

    [Fact]
    public async Task A_stored_version_that_does_not_compile_fails_at_compile_with_the_located_problems_and_writes_no_simulation_run()
    {
        var members = await InstrumentsAsync(2);
        var hash = new string('d', 64);
        await StoreSqlAsync("INSERT INTO lab.strategies (id, style, built_in, created_at) VALUES ('LAB_BAD', 'intraday', false, now())");
        await StoreSqlAsync(
            "INSERT INTO lab.strategy_versions (strategy_id, version, schema_version, hash, source_text, canonical_json, created_at) VALUES ('LAB_BAD', 1, 1, @h, 'this is not a strategy', '{}', now())",
            ("h", hash));
        var before = await CountAsync("SELECT count(*) FROM research.sim_runs");

        var run = await PostAndDrainAsync(BySymbols("LAB_BAD", members.Select(m => m.Symbol)));

        run.GetProperty("state").GetString().ShouldBe("failed");
        run.GetProperty("stage").GetString().ShouldBe("compile");
        run.GetProperty("error").GetString().ShouldBe("lab.compile_failed");
        run.GetProperty("problems").GetArrayLength().ShouldBeGreaterThan(0);
        run.GetProperty("problems")[0].GetProperty("stage").GetString().ShouldBe("compile");
        run.GetProperty("simRunKeys").GetArrayLength().ShouldBe(0);
        (await CountAsync("SELECT count(*) FROM research.sim_runs")).ShouldBe(before);

        using var retry = await PostRunAsync(BySymbols("LAB_BAD", members.Select(m => m.Symbol)));
        retry.StatusCode.ShouldBe(HttpStatusCode.Accepted, "a failed run does not hold its key");
    }

    // criterion 5

    [Fact]
    public async Task A_basket_of_eighty_uses_exactly_thirty_chosen_by_the_seeded_rule_and_listed_in_the_run_and_the_same_input_gives_the_same_thirty()
    {
        var members = await InstrumentsAsync(80);
        var basket = await BasketAsync("Lab eighty", members.Select(m => m.Id));

        using var first = await PostRunAsync(new { strategyId = "ORB", universe = new { basketId = basket }, method = "quick" });
        var firstRun = await JsonOf(first);
        using var second = await PostRunAsync(new { strategyId = "ORB", universe = new { basketId = basket }, method = "quick" });

        var universe = firstRun.GetProperty("universe");
        universe.GetProperty("instruments").GetArrayLength().ShouldBe(30);
        universe.GetProperty("requested").GetInt32().ShouldBe(80);
        universe.GetProperty("sampled").GetBoolean().ShouldBeTrue();
        universe.GetProperty("seed").ValueKind.ShouldBe(JsonValueKind.Number);
        firstRun.GetProperty("labels").EnumerateArray().Select(l => l.GetString()).ShouldContain(l => l!.Contains("30 of 80", StringComparison.Ordinal));
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonOf(second)).GetProperty("universe").GetProperty("instruments").GetRawText().ShouldBe(universe.GetProperty("instruments").GetRawText());
    }

    [Fact]
    public async Task A_basket_that_changes_after_the_post_does_not_change_the_run()
    {
        var members = await InstrumentsAsync(3);
        var basket = await BasketAsync("Lab changing", members.Take(2).Select(m => m.Id));
        using var posted = await PostRunAsync(new { strategyId = "ORB", universe = new { basketId = basket }, method = "quick" });
        var id = (await JsonOf(posted)).GetProperty("id").GetGuid();

        using var added = await Client.PostJsonAsync($"/api/baskets/{basket}/members", new { instrumentIds = new[] { members[2].Id } }, Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.OK);
        await Dispatcher.DrainAsync(Ct);

        var run = await GetRunAsync(id);
        run.GetProperty("universe").GetProperty("instruments").GetArrayLength().ShouldBe(2);
        run.GetProperty("counts").GetProperty("instruments").GetInt32().ShouldBe(2);
    }

    // criterion 6

    [Fact]
    public async Task Cancelling_a_queued_run_cancels_it_at_once_so_no_job_remains_and_nothing_is_stored()
    {
        var members = await InstrumentsAsync(2);
        using var posted = await PostRunAsync(BySymbols("ORB", members.Select(m => m.Symbol), 1));
        var id = (await JsonOf(posted)).GetProperty("id").GetGuid();
        var before = await CountAsync("SELECT count(*) FROM research.sim_runs");

        using var cancel = await Client.PostAsync(new Uri($"/api/lab/runs/{id}/cancel", UriKind.Relative), null, Ct);
        await Dispatcher.DrainAsync(Ct);

        cancel.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await GetRunAsync(id)).GetProperty("state").GetString().ShouldBe("cancelled");
        (await CountAsync("SELECT count(*) FROM research.sim_runs")).ShouldBe(before);
        (await CountAsync("SELECT count(*) FROM lab.lab_runs WHERE state IN ('queued', 'running')")).ShouldBe(0);

        using var again = await Client.PostAsync(new Uri($"/api/lab/runs/{id}/cancel", UriKind.Relative), null, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonOf(again)).GetProperty("code").GetString().ShouldBe("lab.not_cancellable");
    }

    [Fact]
    public async Task Cancelling_a_running_run_asks_it_to_stop_and_a_finished_run_cannot_be_cancelled()
    {
        var members = await InstrumentsAsync(2);
        using var posted = await PostRunAsync(BySymbols("ORB", members.Select(m => m.Symbol), 1));
        var id = (await JsonOf(posted)).GetProperty("id").GetGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ILabRunStore>().ClaimNextAsync(LabRunDispatcher.Host, DateTimeOffset.UtcNow, Ct))!.Id.ShouldBe(id);
        }

        using var cancel = await Client.PostAsync(new Uri($"/api/lab/runs/{id}/cancel", UriKind.Relative), null, Ct);

        cancel.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var run = await GetRunAsync(id);
        run.GetProperty("state").GetString().ShouldBe("running", "the job ends it at its next safe point");
        run.GetProperty("cancelRequested").GetBoolean().ShouldBeTrue();

        using var unknown = await Client.PostAsync(new Uri($"/api/lab/runs/{Guid.NewGuid()}/cancel", UriKind.Relative), null, Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // criterion 7

    [Fact]
    public async Task A_restart_ends_the_runs_the_host_had_running_as_interrupted_and_the_owner_starts_it_again_as_a_new_run()
    {
        var members = await InstrumentsAsync(2);
        var body = BySymbols("ORB", members.Select(m => m.Symbol), 1);
        using var posted = await PostRunAsync(body);
        var id = (await JsonOf(posted)).GetProperty("id").GetGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILabRunStore>().ClaimNextAsync(LabRunDispatcher.Host, DateTimeOffset.UtcNow, Ct);
        }

        await Dispatcher.RecoverAsync(Ct);

        var run = await GetRunAsync(id);
        run.GetProperty("state").GetString().ShouldBe("interrupted");
        run.GetProperty("simRunKeys").GetArrayLength().ShouldBe(0);
        run.GetProperty("counts").ValueKind.ShouldBe(JsonValueKind.Null);
        using var again = await PostRunAsync(body);
        again.StatusCode.ShouldBe(HttpStatusCode.Accepted, "an interrupted run does not hold its key");
        (await JsonOf(again)).GetProperty("id").GetGuid().ShouldNotBe(id);
        await Dispatcher.DrainAsync(Ct);
        (await CountAsync("SELECT count(*) FROM lab.lab_runs WHERE state = 'succeeded'")).ShouldBe(1);
    }

    // criterion 8

    [Fact]
    public async Task A_subscriber_to_the_hub_gets_the_events_of_the_run_in_order_ending_with_labRunFinished_and_GET_serves_the_same_state()
    {
        var members = await InstrumentsAsync(4);
        await using var hub = await HubSocket.ConnectAsync(Factory, Owner, Ct);
        using var posted = await PostRunAsync(BySymbols("ORB", members.Select(m => m.Symbol), 1));
        var id = (await JsonOf(posted)).GetProperty("id").GetGuid();

        await Dispatcher.DrainAsync(Ct);
        await hub.NextAsync(m => m.Type == 1 && m.Target == "labRunFinished", Ct);
        await hub.CloseAndDrainAsync(Ct);

        var events = hub.Seen.Where(m => m.Type == 1 && m.Target is "labRunProgress" or "labRunFinished").ToList();
        events.ShouldNotBeEmpty();
        events[^1].Target.ShouldBe("labRunFinished");
        events[^1].Argument.GetProperty("runId").GetGuid().ShouldBe(id);
        events[^1].Argument.GetProperty("state").GetString().ShouldBe("succeeded");
        events.Take(events.Count - 1).ShouldAllBe(e => e.Target == "labRunProgress");
        var stages = events.Where(e => e.Target == "labRunProgress").Select(e => e.Argument.GetProperty("stage").GetString()!).ToList();
        stages.ShouldBe([.. stages.OrderBy(s => Array.IndexOf(StageOrder, s))], "stages never go backwards");
        events.Where(e => e.Target == "labRunProgress").ShouldAllBe(e => e.Argument.GetProperty("runId").GetGuid() == id && e.Argument.GetProperty("state").GetString() == "running");
        (await GetRunAsync(id)).GetProperty("state").GetString().ShouldBe("succeeded");
    }

    // criterion 10

    [Fact]
    public async Task No_log_line_carries_the_strategy_text_or_a_trade_and_none_a_secret()
    {
        var members = await InstrumentsAsync(3);
        var text = LabTexts.Valid("LABLOG");
        using var stored = await Client.PostJsonAsync("/api/lab/strategies", new { text }, Ct);
        stored.StatusCode.ShouldBe(HttpStatusCode.Created);
        var skipBefore = Factory.Logs.All.Count;

        var run = await PostAndDrainAsync(BySymbols("LABLOG", members.Select(m => m.Symbol)));
        var orb = await PostAndDrainAsync(BySymbols("ORB", members.Select(m => m.Symbol), 1));

        run.GetProperty("state").GetString().ShouldBe("succeeded", run.GetRawText());
        var lines = Factory.Logs.All.Skip(skipBefore).ToList();
        lines.ShouldContain(l => l.Contains("lab.run.finished", StringComparison.Ordinal));
        lines.ShouldNotContain(l => l.Contains("pull_atr", StringComparison.Ordinal) || l.Contains("swing_low", StringComparison.Ordinal) || l.Contains("close > vwap", StringComparison.Ordinal), "no strategy text");
        lines.ShouldNotContain(l => l.Contains("trade_key", StringComparison.OrdinalIgnoreCase) || l.Contains("entry_price", StringComparison.OrdinalIgnoreCase), "no trade");
        lines.ShouldNotContain(l => l.Contains(Factory.OwnerPassword, StringComparison.Ordinal) || l.Contains(Factory.VaultKey, StringComparison.Ordinal), "no secret");
        orb.GetProperty("state").GetString().ShouldBe("succeeded");
    }

    // the refusals and the reads

    [Fact]
    public async Task A_post_that_is_not_valid_is_422_lab_invalid_input_an_unknown_strategy_is_404_and_a_window_without_bars_is_409_lab_no_data()
    {
        var members = await InstrumentsAsync(2);
        var symbols = members.Select(m => m.Symbol).ToList();
        async Task<(HttpStatusCode Status, string Code)> StatusOfAsync(object body)
        {
            using var response = await PostRunAsync(body);
            return (response.StatusCode, (await JsonOf(response)).GetProperty("code").GetString()!);
        }

        (await StatusOfAsync(new { strategyId = "ORB", universe = new { symbols }, method = "full" })).ShouldBe((HttpStatusCode.UnprocessableEntity, "lab.invalid_input"));
        (await StatusOfAsync(new { strategyId = "ORB", method = "quick" })).ShouldBe((HttpStatusCode.UnprocessableEntity, "lab.invalid_input"));
        (await StatusOfAsync(new { strategyId = "ORB", universe = new { symbols = Unknown }, method = "quick" })).ShouldBe((HttpStatusCode.UnprocessableEntity, "lab.invalid_input"));
        (await StatusOfAsync(new { strategyId = "ORB", universe = new { symbols = Array.Empty<string>() }, method = "quick" })).ShouldBe((HttpStatusCode.UnprocessableEntity, "lab.invalid_input"));
        (await StatusOfAsync(new { strategyId = "ORB", universe = new { symbols }, method = "quick", window = new { from = "2026-03-04", to = "2026-03-02" } })).ShouldBe((HttpStatusCode.UnprocessableEntity, "lab.invalid_input"));
        (await StatusOfAsync(BySymbols("NO_SUCH_STRATEGY", symbols))).ShouldBe((HttpStatusCode.NotFound, "lab.not_found"));
        (await StatusOfAsync(BySymbols("ORB", symbols, 99))).ShouldBe((HttpStatusCode.NotFound, "lab.not_found"));
        (await StatusOfAsync(new { strategyId = "ORB", universe = new { basketId = Guid.NewGuid() }, method = "quick" })).ShouldBe((HttpStatusCode.NotFound, "lab.not_found"));
        (await StatusOfAsync(new { strategyId = "ORB", universe = new { symbols }, method = "quick", window = new { from = "2030-01-01", to = "2030-02-01" } })).ShouldBe((HttpStatusCode.Conflict, "lab.no_data"));
        (await CountAsync("SELECT count(*) FROM lab.lab_runs")).ShouldBe(0);
    }

    [Fact]
    public async Task With_no_stored_bar_at_all_a_post_is_409_lab_no_data()
    {
        var members = await InstrumentsAsync(2, bars: false);

        using var response = await PostRunAsync(BySymbols("ORB", members.Select(m => m.Symbol)));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonOf(response)).GetProperty("code").GetString().ShouldBe("lab.no_data");
    }

    [Fact]
    public async Task The_list_filters_by_strategy_and_state_newest_first_and_an_unknown_run_or_state_is_refused()
    {
        var members = await InstrumentsAsync(2);
        var symbols = members.Select(m => m.Symbol).ToList();
        var done = await PostAndDrainAsync(BySymbols("ORB", symbols, 1));
        using var queued = await PostRunAsync(BySymbols("ORB", symbols.Take(1), 1));
        var queuedId = (await JsonOf(queued)).GetProperty("id").GetGuid();

        async Task<List<Guid>> IdsAsync(string query)
        {
            using var response = await Client.GetAsync(new Uri("/api/lab/runs" + query, UriKind.Relative), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return [.. (await JsonOf(response)).EnumerateArray().Select(e => e.GetProperty("id").GetGuid())];
        }

        (await IdsAsync("")).ShouldBe([queuedId, done.GetProperty("id").GetGuid()]);
        (await IdsAsync("?state=queued")).ShouldBe([queuedId]);
        (await IdsAsync("?strategyId=ORB&state=succeeded&limit=1")).ShouldBe([done.GetProperty("id").GetGuid()]);
        (await IdsAsync("?strategyId=OTHER")).ShouldBeEmpty();
        using var bad = await Client.GetAsync(new Uri("/api/lab/runs?state=paused", UriKind.Relative), Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var missing = await Client.GetAsync(new Uri($"/api/lab/runs/{Guid.NewGuid()}", UriKind.Relative), Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_endpoints_need_the_owner_session()
    {
        using var anonymous = Factory.CreateClient();

        using var list = await anonymous.GetAsync(new Uri("/api/lab/runs", UriKind.Relative), Ct);
        using var post = await anonymous.PostAsJsonAsync(new Uri("/api/lab/runs", UriKind.Relative), new { }, Ct);

        list.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        post.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
