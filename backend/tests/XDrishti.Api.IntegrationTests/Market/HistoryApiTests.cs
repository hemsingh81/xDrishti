using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Market.History;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>
/// CF-04e over HTTP and a real TimescaleDB: the history card read and the two owner acts. The switch is OFF by default (the shared host); the ON cases run on a second host with the switch set by configuration (no route turns it on). Dhan is a counting fake.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed partial class HistoryApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Overview = "/api/data/history";
    private const string Backfill = "/api/data/history/backfill";
    private const string Probe = "/api/data/history/probe";

    private static readonly DateTimeOffset Seam = new(2024, 4, 1, 9, 15, 0, IndiaTime.Offset);

    [GeneratedRegex("^[a-z_=0-9 .\\-]+$")]
    private static partial Regex Allowed();

    private sealed class FixedDisk(long? free) : IFreeDiskProbe
    {
        public long? FreeBytes() => free;
    }

    private sealed class AllKnown : IBackfillCalendar
    {
        public ValueTask<IReadOnlySet<DateOnly>> KnownDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlySet<DateOnly>>(Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)).ToHashSet());
    }

    private readonly List<WebApplicationFactory<Program>> _hosts = [];

    public override async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        Factory.Dhan.Reset();
        await base.DisposeAsync();
    }

    /// <summary>A second host with the switch ON and the run window open all day; <paramref name="complete"/> puts in a full daily calendar and plenty of disk.</summary>
    private HttpClient OnClient(bool complete = false, long? freeDisk = null, string window = "00:00-00:00")
    {
        var host = Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Data:History:Backfill:Enabled", "true");
            b.UseSetting("Data:History:Backfill:RunWindow", window);
            b.ConfigureTestServices(s =>
            {
                if (complete)
                {
                    s.AddSingleton<IBackfillCalendar>(new AllKnown());
                    s.AddSingleton<IFreeDiskProbe>(new FixedDisk(freeDisk ?? long.MaxValue));
                }
            });
        });
        _hosts.Add(host);
        return WithSession(host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false }), Owner, _hosts.Count);
    }

    private static HttpClient WithSession(HttpClient client, SignedInClient owner, int n)
    {
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"198.19.0.{n + 10}");
        client.DefaultRequestHeaders.Add("Cookie", owner.CookieHeader);
        client.DefaultRequestHeaders.Add("X-CSRF-Token", owner.Session.CsrfToken);
        return client;
    }

    private async Task<Guid> SeedWithCsvAsync(string ticker)
    {
        var id = await SeedAsync(ticker);
        using var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { id } }, Ct);
        tracked.EnsureSuccessStatusCode();
        foreach (var bar in Bars())
        {
            await StoreAsync(id, bar, "csv");
        }

        return id;
    }

    private static Bar[] Bars() => [Bar(0, 100), Bar(1, 100.1), Bar(2, 99.95)];

    private static Bar Bar(int minute, double close) => new(Seam.AddMinutes(minute).ToUnixTimeSeconds(), close, close + 1, close - 1, close, 10);

    private FakeDhan.FakeSource UseCountingSource(Func<IntradayTarget, IReadOnlyList<Bar>>? respond = null)
    {
        Factory.Dhan.UseConnectedAccount();
        return Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, respond?.Invoke(t) ?? [], 1));
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> PostAsync(HttpClient client, string path, string? password, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), new { password }, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), response);
    }

    private static string CodeOf(JsonElement body) => body.GetProperty("code").GetString()!;

    private Task<long> CountAsync(string sql) => ScalarAsync<long>(sql);

    [Theory]
    [InlineData(Backfill, "correct")]
    [InlineData(Backfill, "wrong")]
    [InlineData(Backfill, null)]
    [InlineData(Probe, "correct")]
    [InlineData(Probe, "wrong")]
    [InlineData(Probe, null)]
    public async Task With_the_switch_off_both_acts_are_422_disabled_with_zero_calls_no_password_check_no_run_and_no_audit(string path, string? kind)
    {
        await SeedWithCsvAsync("OFF1");
        var source = UseCountingSource();
        Factory.Brokers.ClearRequests();
        var runs = await CountAsync("SELECT count(*) FROM platform.service_runs");
        var signIns = await CountAsync("SELECT count(*) FROM auth.audit_events");
        var chunks = await CountAsync("SELECT count(*) FROM market.backfill_chunks");
        var seams = await CountAsync("SELECT count(*) FROM market.backfill_seams");
        var password = kind switch { "correct" => Factory.OwnerPassword, "wrong" => "not-the-password", _ => null };

        var result = await PostAsync(Client, path, password, Ct);

        result.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        CodeOf(result.Body).ShouldBe("history.disabled");
        source.Plans.ShouldBeEmpty();
        Factory.Brokers.Requests.ShouldBeEmpty();
        (await CountAsync("SELECT count(*) FROM platform.service_runs")).ShouldBe(runs);
        (await CountAsync("SELECT count(*) FROM auth.audit_events")).ShouldBe(signIns, "the password was not checked");
        (await CountAsync("SELECT count(*) FROM market.backfill_chunks")).ShouldBe(chunks);
        (await CountAsync("SELECT count(*) FROM market.backfill_seams")).ShouldBe(seams);
        (await CountAsync("SELECT count(*) FROM accounts.account_events WHERE event LIKE 'History%'")).ShouldBe(0);
    }

    [Theory]
    [InlineData(Backfill)]
    [InlineData(Probe)]
    public async Task Without_a_session_it_is_401_and_without_the_CSRF_token_403(string path)
    {
        var anonymous = Factory.CreateAnonymousClient();
        var noCsrf = Factory.CreateAnonymousClient();
        noCsrf.DefaultRequestHeaders.Add("Cookie", Owner.CookieHeader);

        var unauthorized = await PostAsync(anonymous, path, Factory.OwnerPassword, Ct);
        var forbidden = await PostAsync(noCsrf, path, Factory.OwnerPassword, Ct);

        unauthorized.Status.ShouldBe(HttpStatusCode.Unauthorized);
        forbidden.Status.ShouldBe(HttpStatusCode.Forbidden);
        CodeOf(forbidden.Body).ShouldBe("auth.csrf_invalid");
    }

    [Theory]
    [InlineData(Backfill)]
    [InlineData(Probe)]
    public async Task With_the_switch_on_a_missing_password_is_403_and_a_wrong_one_401_and_nothing_starts(string path)
    {
        await SeedWithCsvAsync("ON1");
        var source = UseCountingSource();
        var on = OnClient(complete: true);

        var missing = await PostAsync(on, path, null, Ct);
        var wrong = await PostAsync(on, path, "not-the-password", Ct);

        missing.Status.ShouldBe(HttpStatusCode.Forbidden);
        CodeOf(missing.Body).ShouldBe("auth.step_up_required");
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        CodeOf(wrong.Body).ShouldBe("auth.invalid_credentials");
        source.Plans.ShouldBeEmpty();
        (await CountAsync("SELECT count(*) FROM platform.service_runs WHERE service_id = 'history-backfill'")).ShouldBe(0);
        (await CountAsync("SELECT count(*) FROM accounts.account_events WHERE event LIKE 'History%'")).ShouldBe(0);
    }

    [Theory]
    [InlineData(Backfill)]
    [InlineData(Probe)]
    public async Task Outside_the_run_window_both_acts_are_422_naming_the_start_of_the_window(string path)
    {
        await SeedWithCsvAsync("WIN1");
        var source = UseCountingSource();
        var on = OnClient(complete: true, window: "20:00-06:00"); // the pinned clock is 12:00 India time

        var result = await PostAsync(on, path, Factory.OwnerPassword, Ct);

        result.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        CodeOf(result.Body).ShouldBe("history.outside_window");
        result.Body.GetProperty("title").GetString()!.ShouldContain("20:00");
        source.Plans.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Backfill)]
    [InlineData(Probe)]
    public async Task A_running_history_run_is_409_run_active_with_zero_calls(string path)
    {
        await SeedWithCsvAsync("RUN1");
        var source = UseCountingSource();
        var on = OnClient(complete: true);
        using (var scope = Factory.Services.CreateScope())
        {
            var started = await scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Platform.IServiceRunStore>()
                .TryStartAsync(XDrishti.Domain.Platform.Services.ServiceRun.Start(HistoryBackfillService.Id, XDrishti.Domain.Platform.Services.ServiceRunTrigger.Manual, 1, Factory.Clock.GetUtcNow()), TimeSpan.FromHours(5), Ct);
            started.ShouldBeTrue();
        }

        var result = await PostAsync(on, path, Factory.OwnerPassword, Ct);

        result.Status.ShouldBe(HttpStatusCode.Conflict);
        CodeOf(result.Body).ShouldBe("history.run_active");
        source.Plans.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_daily_calendar_the_backfill_is_422_calendar_missing_and_starts_nothing()
    {
        await SeedWithCsvAsync("CAL1");
        var source = UseCountingSource();
        var on = OnClient(); // the real calendar: no daily file is loaded

        var result = await PostAsync(on, Backfill, Factory.OwnerPassword, Ct);

        result.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        CodeOf(result.Body).ShouldBe("history.calendar_missing");
        source.Plans.ShouldBeEmpty();
        (await CountAsync("SELECT count(*) FROM platform.service_runs WHERE service_id = 'history-backfill'")).ShouldBe(0);
    }

    [Fact]
    public async Task With_too_little_disk_the_backfill_is_422_disk_low_naming_both_numbers()
    {
        await SeedWithCsvAsync("DSK1");
        var source = UseCountingSource();
        var on = OnClient(complete: true, freeDisk: 1_000);

        var result = await PostAsync(on, Backfill, Factory.OwnerPassword, Ct);

        result.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        CodeOf(result.Body).ShouldBe("history.disk_low");
        result.Body.GetProperty("title").GetString()!.ShouldContain("1,000");
        source.Plans.ShouldBeEmpty();
        (await CountAsync("SELECT count(*) FROM platform.service_runs WHERE service_id = 'history-backfill'")).ShouldBe(0);
    }

    [Fact]
    public async Task With_nothing_pending_the_backfill_answers_200_not_started_and_no_run()
    {
        var on = OnClient(complete: true);
        UseCountingSource();

        var result = await PostAsync(on, Backfill, Factory.OwnerPassword, Ct); // no tracked instrument: nothing is pending

        result.Status.ShouldBe(HttpStatusCode.OK);
        result.Body.GetProperty("started").GetBoolean().ShouldBeFalse();
        (await CountAsync("SELECT count(*) FROM platform.service_runs WHERE service_id = 'history-backfill'")).ShouldBe(0);
    }

    [Fact]
    public async Task Pending_chunks_start_the_service_as_a_manual_run_and_audit_counts_only()
    {
        await SeedWithCsvAsync("GO1");
        using var release = new ManualResetEventSlim();
        var source = UseCountingSource(_ =>
        {
            release.Wait(Ct);
            return [];
        });
        var on = OnClient(complete: true);

        var result = await PostAsync(on, Backfill, Factory.OwnerPassword, Ct);

        result.Status.ShouldBe(HttpStatusCode.Accepted);
        result.Body.GetProperty("started").GetBoolean().ShouldBeTrue();
        result.Body.GetProperty("chunksPending").GetInt32().ShouldBeGreaterThan(0);
        result.Body.GetProperty("runId").GetGuid().ShouldNotBe(Guid.Empty);
        result.Response.Headers.Location!.ToString().ShouldBe("/api/services/history-backfill/runs");
        (await ScalarAsync<string>("SELECT trigger FROM platform.service_runs WHERE service_id = 'history-backfill' ORDER BY started_at DESC LIMIT 1")).ShouldBe("manual");
        var detail = await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'HistoryBackfillStarted'");
        Allowed().IsMatch(detail).ShouldBeTrue(detail);
        detail.ShouldNotContain("GO1");

        release.Set();
        var watch = Stopwatch.StartNew();
        var runner = _hosts[^1].Services.GetRequiredService<ServiceRunner>(); // the run belongs to the host that took the request
        while (runner.IsActive(HistoryBackfillService.Id) && watch.Elapsed < TimeSpan.FromSeconds(60))
        {
            await Task.Yield();
        }

        runner.IsActive(HistoryBackfillService.Id).ShouldBeFalse();
        source.Plans.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_probe_returns_the_counts_and_lines_leaves_bars_untouched_and_audits_counts_only()
    {
        await SeedWithCsvAsync("PRB1");
        await SeedWithCsvAsync("PRB2");
        var source = UseCountingSource(_ => Bars());
        var before = await ScalarAsync<string>("SELECT md5(string_agg(instrument_id::text || ts::text || close::text || source, ';' ORDER BY instrument_id, ts)) FROM market.bars_1m");
        var on = OnClient(complete: true);

        var result = await PostAsync(on, Probe, Factory.OwnerPassword, Ct);

        result.Status.ShouldBe(HttpStatusCode.OK);
        result.Body.GetProperty("probed").GetInt32().ShouldBe(2);
        result.Body.GetProperty("passed").GetInt32().ShouldBe(2);
        result.Body.GetProperty("requests").GetInt32().ShouldBe(2);
        result.Body.GetProperty("lines").GetArrayLength().ShouldBe(2);
        source.Plans.Count.ShouldBe(2);
        (await ScalarAsync<string>("SELECT md5(string_agg(instrument_id::text || ts::text || close::text || source, ';' ORDER BY instrument_id, ts)) FROM market.bars_1m")).ShouldBe(before);
        var detail = await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'HistorySeamProbed'");
        detail.ShouldBe("probed=2 passed=2 unresolved=0 no_overlap=0 failed=0 requests=2");
    }

    [Fact]
    public async Task Two_probes_at_once_give_one_200_and_one_409()
    {
        await SeedWithCsvAsync("CON1");
        using var entered = new SemaphoreSlim(0);
        using var release = new ManualResetEventSlim();
        UseCountingSource(_ =>
        {
            entered.Release();
            release.Wait(Ct);
            return Bars();
        });
        var on = OnClient(complete: true);

        var first = PostAsync(on, Probe, Factory.OwnerPassword, Ct);
        await entered.WaitAsync(Ct);
        var second = await PostAsync(on, Probe, Factory.OwnerPassword, Ct);
        release.Set();
        var firstResult = await first;

        firstResult.Status.ShouldBe(HttpStatusCode.OK);
        second.Status.ShouldBe(HttpStatusCode.Conflict);
        CodeOf(second.Body).ShouldBe("history.run_active");
    }

    [Fact]
    public async Task The_overview_lists_the_states_in_symbol_order_with_no_password_no_call_and_no_store()
    {
        var done = await SeedWithCsvAsync("DDD");
        var partial = await SeedWithCsvAsync("CCC");
        var unresolved = await SeedWithCsvAsync("BBB");
        var jump = await SeedWithCsvAsync("AAA");
        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IBackfillStore>();
            var plan = Enumerable.Range(0, 10).Select(i => (new DateOnly(2023, 1, 1).AddDays(i * 90), new DateOnly(2023, 1, 1).AddDays((i + 1) * 90))).ToList();
            await store.PlanChunksAsync(done, plan, Ct);
            await store.PlanChunksAsync(partial, plan, Ct);
            for (var i = 0; i < 10; i++)
            {
                var from = new DateOnly(2023, 1, 1).AddDays(i * 90);
                await store.SaveChunkAsync(new ChunkRecord(done, from, from.AddDays(90), "done", 5, 1, null), Ct);
                await store.SaveChunkAsync(new ChunkRecord(partial, from, from.AddDays(90), i < 4 ? "done" : i == 4 ? "failed" : "planned", 5, 1, null), Ct);
            }

            await store.SaveSeamAsync(new SeamRecord(unresolved, Seam.ToUniversalTime(), "unresolved", Factory.Clock.GetUtcNow(), 70.5, 12.0, "x", false, null), Ct);
            await store.SaveSeamAsync(new SeamRecord(jump, Seam.ToUniversalTime(), "passed", Factory.Clock.GetUtcNow(), 1.0, 100.0, null, true, 32.0), Ct);
        }

        var source = UseCountingSource();
        Factory.Brokers.ClearRequests();

        using var response = await Client.GetAsync(new Uri(Overview, UriKind.Relative), Ct);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        body.GetProperty("instruments").EnumerateArray().Select(r => r.GetProperty("symbol").GetString()).ShouldBe(["NSE:AAA", "NSE:BBB", "NSE:CCC", "NSE:DDD"]);
        body.GetProperty("instruments").EnumerateArray().Select(r => r.GetProperty("backfillStatus").GetString()).ShouldBe(["seam_jump", "seam_unresolved", "partial", "done"]);
        body.GetProperty("instruments")[0].GetProperty("seamJumpPct").GetDouble().ShouldBe(32.0);
        body.GetProperty("instruments")[2].GetProperty("chunks").GetProperty("failed").GetInt32().ShouldBe(1);
        var by = body.GetProperty("totals").GetProperty("byStatus");
        (by.GetProperty("partial").GetInt32() + by.GetProperty("done").GetInt32() + by.GetProperty("seam_unresolved").GetInt32() + by.GetProperty("seam_jump").GetInt32() + by.GetProperty("none").GetInt32() + by.GetProperty("planned").GetInt32()).ShouldBe(4);
        body.GetProperty("switch").GetProperty("enabled").GetBoolean().ShouldBeFalse();
        body.GetProperty("switch").GetProperty("editable").GetBoolean().ShouldBeFalse();
        body.GetProperty("runWindow").GetString().ShouldBe("20:00-06:00");
        body.GetProperty("running").GetBoolean().ShouldBeFalse();
        body.GetProperty("serviceId").GetString().ShouldBe("history-backfill");
        source.Plans.ShouldBeEmpty();
        Factory.Brokers.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_overview_needs_a_session()
    {
        using var response = await Factory.CreateAnonymousClient().GetAsync(new Uri(Overview, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
