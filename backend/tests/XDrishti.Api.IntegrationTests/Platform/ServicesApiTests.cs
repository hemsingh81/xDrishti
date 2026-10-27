using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Api.IntegrationTests.Platform;

/// <summary>
/// The Services API end to end against the real database (ADR 0018, spec 13 to 17): a derived host with a fake service next to the real ones, the list, save with revisions and issues, the dry run, pause,
/// resume, restore default, run now with its history and notices, the timeline, the audit trail, the access rules. The scheduler's own loop is off for the suite: runs here start by Run now.
/// Monday 2026-10-12 12:00 India time.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ServicesApiTests(ApiFactory factory) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private static readonly string[] WeekDays = ["mon", "tue", "wed", "thu", "fri"];

    private readonly FakeJobService _fake = new();

    private WebApplicationFactory<Program> _host = null!;

    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceRunner Runner => _host.Services.GetRequiredService<ServiceRunner>();

    public async ValueTask InitializeAsync()
    {
        factory.Clock.Pin(Now);
        await factory.ResetAllAsync();
        await factory.AssertCleanAsync();
        _host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services => services.AddSingleton<IScheduledService>(_fake)));
        _client = await SignInAsync(_host);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        await factory.ResetClockAsync();
        await factory.ResetServicesAsync();
        GC.SuppressFinalize(this);
    }

    private async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.18.250.78");
        using var response = await client.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);
        response.EnsureSuccessStatusCode();
        var session = (await response.ReadAsync<XDrishti.Application.Auth.SessionInfo>(Ct));
        client.DefaultRequestHeaders.Add("Cookie", $"xd.sid={ApiFactory.SessionCookieValue(response)}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", session.CsrfToken);
        return client;
    }

    private static object Body(string time = "10:00", long revision = 0, bool enabled = true, int retries = 0, int timeoutMinutes = 5, object? parameters = null) => new
    {
        enabled,
        schedule = new { type = "daily", time, days = WeekDays, tradingDaysOnly = true },
        retries,
        timeoutMinutes,
        catchUp = true,
        notifyOnFailure = true,
        parameters,
        revision,
    };

    private async Task<ServiceView> GetServiceAsync(string id = FakeJobService.Id)
    {
        using var response = await _client.GetAsync($"/api/services/{id}", Ct);
        return await response.ReadAsync<ServiceView>(Ct);
    }

    private async Task<ServiceRunPage> RunsAsync(string id = FakeJobService.Id)
    {
        using var response = await _client.GetAsync($"/api/services/{id}/runs", Ct);
        return await response.ReadAsync<ServiceRunPage>(Ct);
    }

    private async Task<List<string>> AuditEventsAsync() =>
        await factory.WithDbAsync(async db => await db.AccountEvents.AsNoTracking().Where(e => e.AccountId == null).OrderBy(e => e.At).Select(e => e.Event.ToString()).Where(e => e.StartsWith("Service")).ToListAsync(Ct));

    // ---- access ----

    public static TheoryData<string, string> AllEndpoints => new()
    {
        { "GET", "/api/services" },
        { "GET", "/api/services/timeline" },
        { "GET", "/api/services/notices" },
        { "GET", "/api/services/eod" },
        { "GET", "/api/services/eod/runs" },
        { "PUT", "/api/services/eod/schedule" },
        { "POST", "/api/services/eod/schedule/preview" },
        { "POST", "/api/services/eod/schedule/reset" },
        { "POST", "/api/services/eod/pause" },
        { "POST", "/api/services/eod/resume" },
        { "POST", "/api/services/eod/run" },
    };

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task Without_a_session_every_endpoint_answers_401(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = method is "POST" or "PUT" ? new StringContent("{}", System.Text.Encoding.UTF8, "application/json") : null,
        };

        using var response = await factory.CreateAnonymousClient().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/services/eod/pause")]
    [InlineData("/api/services/eod/run")]
    [InlineData("/api/services/eod/schedule/reset")]
    public async Task A_change_without_the_CSRF_token_is_refused_even_with_a_session(string path)
    {
        var signedIn = await factory.SignInAsync(Ct);
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        using var response = await client.PostAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuditEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task With_a_session_and_the_CSRF_token_no_step_up_is_needed()
    {
        using var response = await _client.PostAsync($"/api/services/{FakeJobService.Id}/pause", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- list and detail ----

    [Fact]
    public async Task The_list_has_the_real_services_the_fake_one_and_the_read_only_sync()
    {
        using var response = await _client.GetAsync("/api/services", Ct);

        var list = await response.ReadAsync<ServiceList>(Ct);

        list.Items.Select(s => s.Id).ShouldBe(["eod", "instrument-master", "holdings-quotes", "portfolio-broker-history", "nightly", "suggestions", "review-cutoff", "preopen", "plan-expiry", "paper-capture", "weekly", "briefing", "weekly-review", "paper-review", "live-feed", "depth-recorder", "post-trade-prompts", "fake-job", "account-sync"]);
        var feed = list.Items.Single(s => s.Id == "live-feed");
        (feed.Name, feed.RunsAs, feed.Enabled, feed.Schedule.Type, feed.Schedule.From, feed.Schedule.To).ShouldBe(("Live feed", "xd-feed", true, "window", "09:00", "15:35")); // run by xd-feed, shown and edited here
        feed.Live.ShouldNotBeNull();
        var depth = list.Items.Single(s => s.Id == "depth-recorder");
        (depth.RunsAs, depth.Enabled, depth.Schedule.Type, depth.Schedule.From, depth.Schedule.To).ShouldBe(("xd-feed", false, "window", "09:14", "15:31")); // starts paused, run by xd-feed
        depth.Live.ShouldBeNull(); // the live feed's health and manual run are not the depth recorder's
        var fake = list.Items.Single(s => s.Id == FakeJobService.Id);
        (fake.Name, fake.RunsAs, fake.Enabled, fake.Status, fake.IsDefault, fake.Revision, fake.NextRun).ShouldBe(("Fake job", "xd-test", true, "healthy", true, 0L, new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.FromMinutes(330))));
        fake.Parameters.Single().Name.ShouldBe("lookback");
        var sync = list.Items[^1];
        (sync.ReadOnly, sync.ManageUrl).ShouldBe((true, "/accounts"));
        var eod = list.Items[0];
        (eod.Enabled, eod.Status, eod.NextRun).ShouldBe((false, "paused", null)); // the suite seeds it switched off
    }

    [Fact]
    public async Task An_unknown_service_is_404_and_a_problem_with_the_code()
    {
        using var response = await _client.GetAsync("/api/services/nobody", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("services.not_found");
    }

    // ---- save, preview, pause, resume, reset ----

    [Fact]
    public async Task A_save_stores_the_choices_returns_the_service_and_is_audited()
    {
        using var response = await _client.PutJsonAsync($"/api/services/{FakeJobService.Id}/schedule", Body("11:15", retries: 2, parameters: new { lookback = 12 }), Ct);

        var saved = await response.ReadAsync<ServiceView>(Ct);

        (saved.Schedule.Time, saved.Execution.Retries, saved.Revision > 0, saved.IsDefault).ShouldBe(("11:15", 2, true, false));
        saved.Parameters.Single().Value.GetInt32().ShouldBe(12);
        saved.NextRun.ShouldBe(new DateTimeOffset(2026, 10, 13, 11, 15, 0, TimeSpan.FromMinutes(330))); // 11:15 is over for today (it is 12:00)
        (await GetServiceAsync()).Revision.ShouldBe(saved.Revision);
        (await AuditEventsAsync()).ShouldBe(["ServiceScheduleChanged"]);
    }

    [Fact]
    public async Task A_save_on_a_stale_revision_is_409_services_revision_changed()
    {
        var first = await (await _client.PutJsonAsync($"/api/services/{FakeJobService.Id}/schedule", Body("11:15"), Ct)).ReadAsync<ServiceView>(Ct);

        using var stale = await _client.PutJsonAsync($"/api/services/{FakeJobService.Id}/schedule", Body("12:30", revision: first.Revision - 1), Ct);
        using var current = await _client.PutJsonAsync($"/api/services/{FakeJobService.Id}/schedule", Body("12:30", revision: first.Revision), Ct);

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ReadProblemAsync(Ct)).Code.ShouldBe("services.revision_changed");
        current.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_refused_schedule_is_400_services_invalid_with_every_issue_named_by_service_and_field()
    {
        var body = new
        {
            enabled = true,
            schedule = new { type = "interval", from = "10:00", to = "09:00", days = WeekDays, everyMinutes = 200 },
            retries = 9,
            timeoutMinutes = 0,
            catchUp = true,
            notifyOnFailure = true,
            parameters = new { lookback = 99, mystery = 1 },
            revision = 0,
        };

        using var response = await _client.PutJsonAsync($"/api/services/{FakeJobService.Id}/schedule", body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("code").GetString().ShouldBe("services.invalid");
        var issues = json.RootElement.GetProperty("issues").EnumerateArray().Select(i => (i.GetProperty("field").GetString(), i.GetProperty("severity").GetString(), i.GetProperty("code").GetString())).ToList();
        issues.ShouldContain(("schedule.to", "error", "window_order"));
        issues.ShouldContain(("schedule.everyMinutes", "error", "interval_range"));
        issues.ShouldContain(("retries", "error", "retries_range"));
        issues.ShouldContain(("timeoutMinutes", "error", "timeout_range"));
        issues.ShouldContain(("parameters.lookback", "error", "parameter_invalid"));
        issues.ShouldContain(("parameters.mystery", "error", "parameter_unknown"));
        json.RootElement.GetProperty("errors").GetProperty("retries").GetArrayLength().ShouldBe(1);
        (await GetServiceAsync()).Revision.ShouldBe(0); // nothing was stored
        (await AuditEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_circular_run_after_chain_is_refused_naming_the_other_service()
    {
        await (await _client.PutJsonAsync("/api/services/eod/schedule", new { enabled = true, schedule = new { type = "after", after = FakeJobService.Id }, retries = 0, timeoutMinutes = 60, catchUp = true, notifyOnFailure = true, revision = 0 }, Ct)).ReadAsync<ServiceView>(Ct);

        using var response = await _client.PutJsonAsync($"/api/services/{FakeJobService.Id}/schedule", new { enabled = true, schedule = new { type = "after", after = "eod" }, retries = 0, timeoutMinutes = 5, catchUp = true, notifyOnFailure = true, revision = 0 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var circular = json.RootElement.GetProperty("issues").EnumerateArray().Where(i => i.GetProperty("code").GetString() == "chain_circular").ToList();
        circular.Count.ShouldBe(2);
        circular.Count(i => i.GetProperty("serviceId").ValueKind == JsonValueKind.String && i.GetProperty("serviceId").GetString() == "eod").ShouldBe(1);
    }

    [Fact]
    public async Task Warnings_are_saved_with_the_schedule_and_the_dry_run_gives_the_issues_and_the_next_three_runs_without_storing()
    {
        using var preview = await _client.PostJsonAsync("/api/services/eod/schedule/preview", Body("15:20", timeoutMinutes: 60), Ct);
        var dry = await preview.ReadAsync<SchedulePreview>(Ct);

        dry.NextRuns.Select(r => r.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(["2026-10-12 15:30", "2026-10-13 15:30", "2026-10-14 15:30"]);
        dry.Issues.ShouldHaveSingleItem().Code.ShouldBe("start_too_early");
        (await GetServiceAsync("eod")).Revision.ShouldBe(0);

        using var saved = await _client.PutJsonAsync("/api/services/eod/schedule", Body("15:20", timeoutMinutes: 60), Ct);
        var view = await saved.ReadAsync<ServiceView>(Ct);
        view.Issues.ShouldHaveSingleItem().Severity.ShouldBe("warning");
    }

    [Fact]
    public async Task Pause_resume_and_restore_default_are_audited_and_visible_at_once()
    {
        await _client.PutJsonAsync($"/api/services/{FakeJobService.Id}/schedule", Body("11:15", retries: 3), Ct);

        var paused = await (await _client.PostAsync($"/api/services/{FakeJobService.Id}/pause", Ct)).ReadAsync<ServiceView>(Ct);
        var resumed = await (await _client.PostAsync($"/api/services/{FakeJobService.Id}/resume", Ct)).ReadAsync<ServiceView>(Ct);
        var reset = await (await _client.PostAsync($"/api/services/{FakeJobService.Id}/schedule/reset", Ct)).ReadAsync<ServiceView>(Ct);

        (paused.Status, paused.NextRun, paused.Enabled).ShouldBe(("paused", null, false));
        (resumed.Status, resumed.Schedule.Time).ShouldBe(("healthy", "11:15"));
        (reset.IsDefault, reset.Schedule.Time, reset.Execution.Retries).ShouldBe((true, "10:00", 0));
        (await AuditEventsAsync()).ShouldBe(["ServiceScheduleChanged", "ServicePaused", "ServiceResumed", "ServiceScheduleChanged"]);
    }

    [Fact]
    public async Task The_read_only_sync_refuses_every_write_with_409_services_read_only()
    {
        foreach (var path in new[] { "pause", "resume", "run", "schedule/reset" })
        {
            using var response = await _client.PostAsync($"/api/services/account-sync/{path}", Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("services.read_only");
        }

        using var save = await _client.PutJsonAsync("/api/services/account-sync/schedule", Body(), Ct);
        save.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ---- run now, history, notices ----

    [Fact]
    public async Task Run_now_is_202_with_the_run_the_history_shows_it_and_a_second_start_while_it_runs_is_409()
    {
        _fake.Hold();
        using var started = await _client.PostAsync($"/api/services/{FakeJobService.Id}/run", Ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var run = await started.Content.ReadFromJsonAsync<ServiceRunView>(Ct);
        (run!.Trigger, run.Status, run.Attempt).ShouldBe(("manual", "running", 1));
        (await GetServiceAsync()).Status.ShouldBe("running");

        using var again = await _client.PostAsync($"/api/services/{FakeJobService.Id}/run", Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ReadProblemAsync(Ct)).Code.ShouldBe("services.already_running");

        _fake.Release();
        await Runner.WhenIdleAsync();
        var history = (await RunsAsync()).Items;
        history.Select(r => (r.Status, r.Trigger)).Order().ShouldBe([("skipped", "manual"), ("succeeded", "manual")]);
        history.Single(r => r.Status == "skipped").Message.ShouldNotBeNull().ShouldContain("running already");
        (await GetServiceAsync()).Status.ShouldBe("healthy");
        _fake.Runs.ShouldBe(1);
        (await AuditEventsAsync()).ShouldBe(["ServiceRunRequested", "ServiceRunRequested"]);
    }

    [Fact]
    public async Task Run_now_works_on_a_paused_service()
    {
        await _client.PostAsync($"/api/services/{FakeJobService.Id}/pause", Ct);

        using var started = await _client.PostAsync($"/api/services/{FakeJobService.Id}/run", Ct);
        await Runner.WhenIdleAsync();

        started.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        _fake.Runs.ShouldBe(1);
        (await GetServiceAsync()).Status.ShouldBe("paused");
    }

    [Fact]
    public async Task A_failed_run_raises_a_notice_and_the_next_success_clears_it()
    {
        _fake.Outcome = () => XDrishti.Application.Abstractions.Platform.ServiceOutcome.Failed("The source was down");
        await _client.PostAsync($"/api/services/{FakeJobService.Id}/run", Ct);
        await Runner.WhenIdleAsync();

        using var response = await _client.GetAsync("/api/services/notices", Ct);
        var notices = await response.ReadAsync<ServiceNotices>(Ct);

        var notice = notices.Items.ShouldHaveSingleItem();
        (notice.Kind, notice.Severity, notice.ServiceId, notice.Text).ShouldBe(("service_failed", "error", FakeJobService.Id, "Fake job failed on 12 Oct: The source was down"));
        (await GetServiceAsync()).Status.ShouldBe("failed");

        _fake.Outcome = () => XDrishti.Application.Abstractions.Platform.ServiceOutcome.Succeeded();
        await _client.PostAsync($"/api/services/{FakeJobService.Id}/run", Ct);
        await Runner.WhenIdleAsync();
        using var after = await _client.GetAsync("/api/services/notices", Ct);
        (await after.ReadAsync<ServiceNotices>(Ct)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_history_is_paged_newest_first_and_a_bad_page_is_400()
    {
        for (var i = 0; i < 3; i++)
        {
            await _client.PostAsync($"/api/services/{FakeJobService.Id}/run", Ct);
            await Runner.WhenIdleAsync();
        }

        using var response = await _client.GetAsync($"/api/services/{FakeJobService.Id}/runs?page=2&pageSize=2", Ct);
        var page = await response.ReadAsync<ServiceRunPage>(Ct);

        (page.Items.Count, page.Total, page.Page, page.PageSize).ShouldBe((1, 3, 2, 2));
        using var bad = await _client.GetAsync($"/api/services/{FakeJobService.Id}/runs?page=0", Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var missing = await _client.GetAsync("/api/services/nobody/runs", Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- timeline ----

    [Fact]
    public async Task The_timeline_has_market_hours_and_the_marks_of_the_enabled_services()
    {
        using var response = await _client.GetAsync("/api/services/timeline?date=2026-10-12", Ct);

        var timeline = await response.ReadAsync<ServicesTimeline>(Ct);

        (timeline.TradingDay, timeline.MarketOpen, timeline.MarketClose, timeline.ReviewCutoff).ShouldBe((true, "09:15", "15:30", "09:05"));
        timeline.Entries.Select(e => e.ServiceId).ShouldBe(["holdings-quotes", "live-feed", FakeJobService.Id]); // eod, the instrument master and the trades and ledger read are seeded paused
        var quotes = timeline.Entries[0];
        (quotes.Kind, quotes.Band, quotes.EveryMinutes).ShouldBe(("band", new TimelineBand("09:15", "15:30"), 5));
        var feed = timeline.Entries[1];
        (feed.Kind, feed.Band).ShouldBe(("band", new TimelineBand("09:00", "15:35")));
        var entry = timeline.Entries[2];
        (entry.ServiceId, entry.Kind, entry.Marks.Single().Time).ShouldBe((FakeJobService.Id, "mark", "10:00"));
        using var saturday = await _client.GetAsync("/api/services/timeline?date=2026-10-10", Ct);
        (await saturday.ReadAsync<ServicesTimeline>(Ct)).TradingDay.ShouldBeFalse();
        using var bad = await _client.GetAsync("/api/services/timeline?date=tomorrow", Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- the body ----

    [Theory]
    [InlineData("enabled")]
    [InlineData("schedule")]
    [InlineData("retries")]
    [InlineData("timeoutMinutes")]
    [InlineData("catchUp")]
    [InlineData("notifyOnFailure")]
    public async Task A_body_that_leaves_out_a_required_field_is_a_400_and_stores_nothing(string missing)
    {
        var body = new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["schedule"] = new { type = "daily", time = "11:00", days = WeekDays, tradingDaysOnly = true },
            ["retries"] = 0,
            ["timeoutMinutes"] = 5,
            ["catchUp"] = true,
            ["notifyOnFailure"] = true,
            ["revision"] = 0,
        };
        body.Remove(missing);

        using var save = await _client.PutJsonAsync($"/api/services/{FakeJobService.Id}/schedule", body, Ct);
        using var preview = await _client.PostJsonAsync($"/api/services/{FakeJobService.Id}/schedule/preview", body, Ct);

        save.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        preview.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetServiceAsync()).Revision.ShouldBe(0);
    }

    // ---- a stored schedule that cannot be read ----

    [Fact]
    public async Task A_damaged_stored_schedule_degrades_that_service_only_and_a_save_repairs_it()
    {
        await factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO platform.service_schedules (service_id, enabled, schedule, retries, timeout_minutes, catch_up, notify_on_failure, parameters, updated_by, updated_at)
                VALUES ('fake-job', true, '{{"type":"daily"}}', 0, 5, true, true, '[1,2]', 'test', now())
                """);
            return 0;
        });

        using var response = await _client.GetAsync("/api/services", Ct);
        var list = await response.ReadAsync<ServiceList>(Ct);

        var fake = list.Items.Single(s => s.Id == FakeJobService.Id);
        (fake.Schedule.Time, fake.Enabled).ShouldBe(("10:00", true)); // its defaults
        var issue = fake.Issues[0];
        (issue.Severity, issue.Code, issue.Field).ShouldBe(("error", "schedule.stored_invalid", "schedule"));
        list.Items.Where(s => s.Id != FakeJobService.Id).ShouldAllBe(s => s.Issues.All(i => i.Code != "schedule.stored_invalid"));

        using var saved = await _client.PutJsonAsync($"/api/services/{FakeJobService.Id}/schedule", Body("11:00", revision: fake.Revision), Ct);
        (await saved.ReadAsync<ServiceView>(Ct)).Issues.ShouldBeEmpty();
    }
}
