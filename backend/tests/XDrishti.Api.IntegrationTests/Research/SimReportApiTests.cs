using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// AU-14 (ADR 0250) on a real TimescaleDB: reports by stock, basket and date range over the finished chunks of a simulate-everything job, while it runs. Strategy TRNA ran over stocks A (15 trades, +5 R) and B (12 trades, -4 R), TRNB over A (12 trades, 0 R);
/// the chunk of TRNB over B is still pending, so the job is partial. The evidence reports must never see any of it (golden), the answer says it is a training simulation and how complete it is, the range is cut before the hold-out, pagination is bounded.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class SimReportApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid BasketX = StoredRunFixtures.BasketOne;
    private static readonly Guid BasketY = StoredRunFixtures.BasketTwo;
    private static readonly Guid JobId = new("cccccccc-0000-0000-0000-000000000001");
    private static readonly DateOnly Monday = FabricatedRun.Monday;

    private Guid _a;
    private Guid _b;
    private SimulationRun _run1 = null!;
    private SimulationRun _run2 = null!;
    private SimulationRun _run3 = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanAsync();
        _a = await SeedAsync("TRA");
        _b = await SeedAsync("TRB");
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO lab.strategies (id, style, built_in, created_at) VALUES ('TRNA', 'intraday', false, now()), ('TRNB', 'swing', false, now()) ON CONFLICT DO NOTHING");
            await ExecuteAsync(connection, "DELETE FROM lab.holdout_releases; DELETE FROM lab.holdout_blocks; INSERT INTO lab.holdout_blocks (start_date, history_first, history_last, length_months, setting_hash, created_at) VALUES ('2031-01-01', '2026-03-02', '2031-06-30', 12, repeat('a', 64), now())");
        }

        _run1 = Build("TRNA", _a, "NSE:TRA", BasketX, 10, 5);
        _run2 = Build("TRNB", _a, "NSE:TRA", BasketX, 6, 6, salt: 1);
        _run3 = Build("TRNA", _b, "NSE:TRB", BasketY, 4, 8, salt: 2);
        foreach (var run in new[] { _run1, _run2, _run3 })
        {
            var stored = await StoreKit.WithStoreAsync(Factory.Services, s => s.StoreAsync(StoreKit.ToStore(run with { IsTraining = true }), Ct).AsTask());
            stored.IsSuccess.ShouldBeTrue(stored.IsFailure ? stored.Error.Message : string.Empty);
        }

        await SeedJobAsync("running", "2026-04-30", "2026-06-01", [("TRNA", _a, _run1), ("TRNB", _a, _run2), ("TRNA", _b, _run3)], pending: [("TRNB", _b)]);
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
        await ExecuteAsync(connection, $"DELETE FROM research.sim_all_chunks; DELETE FROM research.sim_all_jobs; DELETE FROM research.sim_trade_leg_charges WHERE run_id IN ({training}); DELETE FROM research.sim_trade_legs WHERE run_id IN ({training}); DELETE FROM research.sim_trades WHERE run_id IN ({training}); DELETE FROM research.sim_runs WHERE purpose = 'training'; DELETE FROM lab.holdout_blocks; DELETE FROM lab.strategies WHERE id IN ('TRNA', 'TRNB')");
    }

    private static SimulationRun Build(string strategy, Guid id, string symbol, Guid basket, int wins, int losses, int salt = 0)
    {
        var trades = new List<SimTrade>();
        for (var j = 0; j < wins + losses; j++)
        {
            var r = j < wins ? 1.0 : -1.0;
            trades.Add(FabricatedRun.Trade(id, symbol, Side.Buy, Monday.AddDays(j), j < wins ? ExitReason.Target : ExitReason.Stop, entryMinute: 600, target: j < wins ? 105.5 : null, rExact: r + 0.05, rFilled: r, strategy: strategy));
        }

        var ran = FabricatedRun.Ran(id, symbol, Monday, new DateOnly(2026, 4, 30)) with { TradesInWindow = wins + losses, CandidatesInWindow = wins + losses };
        var run = FabricatedRun.Assemble(FabricatedRun.List((id, symbol)), [ran], trades, [], from: Monday.AddDays(-salt), to: new DateOnly(2026, 4, 30), strategy: strategy);
        return run with { Trades = [.. run.Trades.Select(t => t with { BasketIds = [basket] })] };
    }

    private async Task SeedJobAsync(string state, string windowTo, string holdOut, (string Strategy, Guid Instrument, SimulationRun Run)[] done, (string Strategy, Guid Instrument)[] pending)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, $"INSERT INTO research.sim_all_jobs (id, state, window_from, window_to, hold_out_start, workers, started_by, created_at, updated_at, finished_at) VALUES (@id, '{state}', '2026-03-02', '{windowTo}', '{holdOut}', 1, 'owner:t@example.com', now(), now(), {(state == "completed" ? "now()" : "NULL")})", ("id", JobId));
        var ordinal = 1;
        foreach (var (strategy, instrument, run) in done)
        {
            await ExecuteAsync(connection, "INSERT INTO research.sim_all_chunks (job_id, strategy_id, version, instrument_id, strategy_hash, ordinal, state, run_key, trades, finished_at) VALUES (@job, @s, 1, @i, repeat('b', 64), @o, 'done', @key, @n, '2026-10-10T00:00:00Z')",
                ("job", JobId), ("s", strategy), ("i", instrument), ("o", ordinal++), ("key", run.RunKey), ("n", run.Trades.Count));
        }

        foreach (var (strategy, instrument) in pending)
        {
            await ExecuteAsync(connection, "INSERT INTO research.sim_all_chunks (job_id, strategy_id, version, instrument_id, strategy_hash, ordinal, state) VALUES (@job, @s, 1, @i, repeat('b', 64), @o, 'pending')", ("job", JobId), ("s", strategy), ("i", instrument), ("o", ordinal++));
        }
    }

    private async Task<JsonElement> OkAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private async Task<(HttpStatusCode Status, string Code)> ProblemAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (response.StatusCode, body.GetProperty("code").GetString()!);
    }

    private static Dictionary<string, int> Counts(JsonElement groups) =>
        groups.GetProperty("groups").EnumerateArray().ToDictionary(g => g.GetProperty("key").GetString()!.Replace("Instrument=", string.Empty, StringComparison.Ordinal), g => g.GetProperty("n").GetInt32());

    private static string Training(string extra = "") => "purpose=training&simJob=" + JobId.ToString("D") + extra;

    [Fact]
    public async Task While_the_job_runs_a_report_reads_the_finished_chunks_and_says_it_is_partial_and_a_training_simulation()
    {
        var groups = await OkAsync($"/api/reports/groups?lens=instrument&{Training()}");

        Counts(groups).ShouldBe(new Dictionary<string, int> { ["NSE:TRA"] = 27, ["NSE:TRB"] = 12 });
        var c = groups.GetProperty("evidence").GetProperty("completeness");
        c.GetProperty("kind").GetString().ShouldBe("training_simulation");
        c.GetProperty("label").GetString()!.ShouldContain("not evidence");
        c.GetProperty("complete").GetBoolean().ShouldBeFalse();
        c.GetProperty("chunksDone").GetInt32().ShouldBe(3);
        c.GetProperty("chunksTotal").GetInt32().ShouldBe(4);
        c.GetProperty("chunksPending").GetInt32().ShouldBe(1);
        c.GetProperty("jobState").GetString().ShouldBe("running");
        c.GetProperty("asOf").GetDateTimeOffset().UtcDateTime.ShouldBe(Now.UtcDateTime, TimeSpan.FromMinutes(1));
        c.GetProperty("trainingRecord").GetString().ShouldBe($"/api/simulate-all/{JobId:D}/training");
        groups.GetProperty("evidence").GetProperty("limits").EnumerateArray().Select(l => l.GetString()).ShouldContain(l => l!.Contains("no claim of out-of-sample", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_the_training_purpose_no_report_and_no_run_list_shows_a_training_run_and_the_answer_has_no_completeness()
    {
        var summary = await OkAsync("/api/reports/summary");
        summary.GetProperty("evidence").TryGetProperty("completeness", out _).ShouldBeFalse("an evidence report is byte-for-byte what it was");
        summary.GetProperty("evidence").GetProperty("runKeys").EnumerateArray().Select(k => k.GetString()).ShouldNotContain(k => k == _run1.RunKey || k == _run2.RunKey || k == _run3.RunKey);

        var runs = await OkAsync("/api/reports/runs");
        runs.GetProperty("runs").EnumerateArray().Select(r => r.GetProperty("runKey").GetString()).ShouldNotContain(_run1.RunKey);
        (await ProblemAsync($"/api/reports/summary?runs={_run1.RunKey}")).ShouldBe((HttpStatusCode.NotFound, "reports.not_found"));
    }

    [Fact]
    public async Task A_stock_filter_reads_only_that_stock_across_strategies_and_a_stock_with_no_result_is_named()
    {
        var summary = await OkAsync($"/api/reports/summary?symbol=NSE:TRA&{Training()}");
        var empty = await OkAsync($"/api/reports/summary?symbol=NSE:TRA,NSE:NOPE&strategy=NOSUCH&{Training()}");

        summary.GetProperty("all").GetProperty("n").GetInt32().ShouldBe(27);
        summary.GetProperty("all").GetProperty("wins").GetInt32().ShouldBe(16);
        summary.GetProperty("evidence").GetProperty("completeness").GetProperty("symbolsWithoutResults").GetArrayLength().ShouldBe(0);
        empty.GetProperty("all").GetProperty("n").GetInt32().ShouldBe(0);
        empty.GetProperty("evidence").GetProperty("completeness").GetProperty("symbolsWithoutResults").EnumerateArray().Select(s => s.GetString()).ShouldBe(["NSE:NOPE", "NSE:TRA"]);
    }

    [Fact]
    public async Task A_basket_filter_follows_the_basket_stored_on_each_trade()
    {
        var x = await OkAsync($"/api/reports/summary?basket={BasketX}&{Training()}");
        var y = await OkAsync($"/api/reports/summary?basket={BasketY}&{Training()}");

        x.GetProperty("all").GetProperty("n").GetInt32().ShouldBe(27);
        y.GetProperty("all").GetProperty("n").GetInt32().ShouldBe(12);
    }

    [Fact]
    public async Task A_date_range_keeps_the_trades_entered_in_it_and_an_inverted_range_is_reports_invalid()
    {
        var firstWeek = await OkAsync($"/api/reports/summary?symbol=NSE:TRB&from={Monday:yyyy-MM-dd}&to={Monday.AddDays(3):yyyy-MM-dd}&{Training()}");

        firstWeek.GetProperty("all").GetProperty("n").GetInt32().ShouldBe(4);
        (await ProblemAsync($"/api/reports/summary?from=2026-04-01&to=2026-03-01&{Training()}")).ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.invalid"));
    }

    [Fact]
    public async Task A_range_reaching_into_the_hold_out_answers_with_the_part_before_it_and_no_trade_after_the_window_end_is_read()
    {
        // the window ends 2026-03-12; the hold-out starts 2026-03-16. Trades entered after the window end exist in the store (the runs go to April) but are not read.
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "DELETE FROM research.sim_all_chunks; DELETE FROM research.sim_all_jobs");
        }

        await SeedJobAsync("completed", "2026-03-12", "2026-03-16", [("TRNA", _a, _run1), ("TRNB", _a, _run2), ("TRNA", _b, _run3)], []);

        var groups = await OkAsync($"/api/reports/groups?lens=instrument&from=2026-03-01&to=2026-12-31&{Training()}");

        // trades are entered on Monday + j days: those up to 2026-03-12 are j = 0..10
        Counts(groups).ShouldBe(new Dictionary<string, int> { ["NSE:TRA"] = 22, ["NSE:TRB"] = 11 });
        var c = groups.GetProperty("evidence").GetProperty("completeness");
        c.GetProperty("rangeCut").GetBoolean().ShouldBeTrue();
        c.GetProperty("rangeTo").GetString().ShouldBe("2026-03-12");
        c.GetProperty("locked").GetString()!.ShouldContain("hold-out block is locked");
        c.GetProperty("complete").GetBoolean().ShouldBeTrue();
        c.GetProperty("holdOutStart").GetString().ShouldBe("2026-03-16");

        var inside = await OkAsync($"/api/reports/summary?from=2026-03-20&to=2026-04-20&{Training()}");
        inside.GetProperty("all").GetProperty("n").GetInt32().ShouldBe(0);
        inside.GetProperty("evidence").GetProperty("completeness").GetProperty("rangeFrom").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Every_report_type_accepts_the_job_and_labels_its_answer()
    {
        foreach (var route in new[] { "summary", "groups?lens=strategy", "pivot?lens=strategy,instrument&metric=expectancy_r", "equity?lens=instrument", "distribution?lens=instrument", "mae-mfe?lens=instrument", "what-works", "insights", "trades" })
        {
            var separator = route.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var answer = await OkAsync($"/api/reports/{route}{separator}{Training()}");

            answer.GetProperty("evidence").GetProperty("completeness").GetProperty("kind").GetString().ShouldBe("training_simulation", route);
        }

        using var created = await Client.PostJsonAsync("/api/baskets", new { name = "TrainingBench" }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var basketId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        using var added = await Client.PostJsonAsync($"/api/baskets/{basketId}/members", new { instrumentIds = new[] { _a, _b } }, Ct);
        added.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var benchmarks = await OkAsync($"/api/reports/benchmarks?basket={basketId}&from=2026-03-02&to=2026-12-31&{Training()}");
        benchmarks.GetProperty("evidence").GetProperty("completeness").GetProperty("kind").GetString().ShouldBe("training_simulation");
        benchmarks.GetProperty("window").GetProperty("to").GetString().ShouldBe("2026-04-30", "the window is cut before the hold-out");
    }

    [Fact]
    public async Task The_trade_list_is_paged_with_a_bounded_limit_and_the_pages_add_up_without_repeats()
    {
        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await OkAsync($"/api/reports/trades?limit=10&{Training()}" + (cursor is null ? string.Empty : "&cursor=" + cursor));
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("runKey").GetString() + "#" + i.GetProperty("ordinal").GetInt32()));
            page.GetProperty("total").GetInt32().ShouldBe(39);
            cursor = page.GetProperty("nextCursor").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 10);

        pages.ShouldBe(4);
        seen.Count.ShouldBe(39);
        seen.Distinct().Count().ShouldBe(39);
        (await ProblemAsync($"/api/reports/trades?limit=1001&{Training()}")).Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public async Task Bad_selectors_are_refused_and_an_unknown_job_is_not_found()
    {
        (await ProblemAsync("/api/reports/summary?simJob=" + JobId.ToString("D"))).ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.invalid"));
        (await ProblemAsync("/api/reports/summary?purpose=other")).ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.invalid"));
        (await ProblemAsync("/api/reports/summary?purpose=training&basis=net")).ShouldBe((HttpStatusCode.UnprocessableEntity, "reports.invalid"));
        (await ProblemAsync("/api/reports/summary?purpose=training&simJob=" + Guid.NewGuid().ToString("D"))).ShouldBe((HttpStatusCode.NotFound, "reports.not_found"));
        (await ProblemAsync("/api/reports/benchmarks?basket=" + BasketX + "&purpose=training&simJob=" + Guid.NewGuid().ToString("D"))).ShouldBe((HttpStatusCode.NotFound, "reports.not_found"));
    }

    [Fact]
    public async Task The_report_indexes_exist()
    {
        (await ScalarAsync<long>("SELECT count(*) FROM pg_indexes WHERE schemaname = 'research' AND indexname IN ('ix_sim_all_chunks_instrument', 'ix_sim_all_chunks_run_key')")).ShouldBe(2);
    }

    [Fact]
    public async Task The_job_list_is_a_bounded_page_with_a_cursor()
    {
        var page = await OkAsync("/api/reports/sim-jobs?limit=1");

        page.GetProperty("label").GetString()!.ShouldContain("not evidence");
        var job = page.GetProperty("items")[0];
        job.GetProperty("id").GetGuid().ShouldBe(JobId);
        job.GetProperty("complete").GetBoolean().ShouldBeFalse();
        job.GetProperty("chunksDone").GetInt32().ShouldBe(3);
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
        (await ProblemAsync("/api/reports/sim-jobs?limit=0")).Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public async Task A_report_of_one_stock_over_a_larger_store_answers_in_well_under_the_budget()
    {
        // 60 more training runs of 300 trades each for other stocks sit in the store; the report of one stock still reads only its own runs
        var others = new List<(string, Guid, SimulationRun)>();
        for (var i = 0; i < 60; i++)
        {
            var id = await SeedAsync("BIG" + i);
            var run = Build("TRNA", id, "NSE:BIG" + i, BasketY, 150, 150, salt: 3);
            (await StoreKit.WithStoreAsync(Factory.Services, s => s.StoreAsync(StoreKit.ToStore(run with { IsTraining = true }), Ct).AsTask())).IsSuccess.ShouldBeTrue();
            others.Add(("TRNA", id, run));
        }

        await using (var connection = await OpenAsync())
        {
            var ordinal = 100;
            foreach (var (strategy, instrument, run) in others)
            {
                await ExecuteAsync(connection, "INSERT INTO research.sim_all_chunks (job_id, strategy_id, version, instrument_id, strategy_hash, ordinal, state, run_key, trades, finished_at) VALUES (@job, @s, 1, @i, repeat('b', 64), @o, 'done', @key, @n, '2026-10-10T00:00:00Z')",
                    ("job", JobId), ("s", strategy), ("i", instrument), ("o", ordinal++), ("key", run.RunKey), ("n", run.Trades.Count));
            }
        }

        var timings = new List<long>();
        for (var i = 0; i < 20; i++)
        {
            var watch = Stopwatch.StartNew();
            var summary = await OkAsync($"/api/reports/summary?symbol=NSE:TRA&{Training()}");
            timings.Add(watch.ElapsedMilliseconds);
            summary.GetProperty("all").GetProperty("n").GetInt32().ShouldBe(27);
        }

        timings.Sort();
        var p95 = timings[18];
        Console.WriteLine($"one stock over {60 * 300 + 39} training trades: p95 {p95} ms, median {timings[10]} ms (HTTP hop included)");
        p95.ShouldBeLessThan(500);
        var basket = await OkAsync($"/api/reports/summary?basket={BasketX}&{Training()}");
        basket.GetProperty("all").GetProperty("n").GetInt32().ShouldBe(27);
    }
}
