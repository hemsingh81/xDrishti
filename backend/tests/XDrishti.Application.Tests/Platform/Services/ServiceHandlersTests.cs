using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Platform.Services.ServicesWorld;
using static XDrishti.Application.Tests.Platform.Services.Svc;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>
/// The use cases behind the Services screen (ADR 0018, spec 13 to 17): the list with status, last and next run, the schedule checks on save and preview, the revision, the audit events, pause, resume,
/// restore default, run now, the history, the timeline and the notices. The clock is Monday 2026-10-12, 11:00 India time.
/// </summary>
public sealed class ServiceHandlersTests : IDisposable
{
    private const string Owner = "owner@example.invalid";

    private readonly TestService _eod = Eod();

    private readonly TestService _nightly = Nightly();

    private readonly ServicesWorld _world;

    public ServiceHandlersTests()
    {
        _world = new ServicesWorld(_eod, _nightly);
        _world.SetClock(At(12, 11, 0));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _world.Dispose();

    private static string[] Fields(Domain.Common.Error error) => [.. error.Fields!.Select(f => f.Field)];

    // ---- list and detail (13) ----

    [Fact]
    public async Task The_list_has_every_registered_service_then_the_read_only_account_sync()
    {
        var list = await _world.ListAsync();

        list.Items.Select(s => s.Id).ShouldBe(["eod", "nightly", "account-sync"]);
        var sync = list.Items[^1];
        (sync.ReadOnly, sync.ManageUrl, sync.Name, sync.AllowedScheduleTypes).ShouldBe((true, "/accounts", "Account & portfolio sync", []));
        list.Items.Take(2).ShouldAllBe(s => !s.ReadOnly && s.ManageUrl == null);
    }

    [Fact]
    public async Task A_service_shows_its_schedule_words_execution_settings_and_a_zero_revision_until_it_has_a_row()
    {
        var eod = await _world.ViewAsync("eod");

        (eod.Name, eod.RunsAs, eod.Enabled, eod.Status, eod.IsDefault, eod.Revision).ShouldBe(("Eod", "xd-test", true, "healthy", true, 0));
        (eod.Schedule.Type, eod.Schedule.Time, eod.Schedule.TradingDaysOnly).ShouldBe(("daily", "15:50", true));
        eod.Schedule.Days.ShouldBe(["mon", "tue", "wed", "thu", "fri"]);
        eod.ScheduleWords.ShouldBe("Every trading day at 15:50 IST, never before the session has ended");
        eod.Execution.ShouldBe(new ExecutionView(0, 30, true, true));
        eod.AllowedScheduleTypes.ShouldBe(["daily", "weekly", "after", "window", "interval", "continuous"]);
        eod.NextRun.ShouldBe(At(12, 15, 50));
        eod.LastRun.ShouldBeNull();
    }

    [Fact]
    public async Task The_parameters_are_listed_with_their_schema_and_value()
    {
        var parameters = new[] { ParameterDefinition.Whole("lookback", "Look-back", 1, 30, 5, "Sessions", "sessions") };
        using var world = new ServicesWorld(Make("job", Schedule.Daily(T(10, 0)), parameters: parameters));
        world.SetClock(At(12, 11, 0));
        world.Data.Store("job", Settings(Schedule.Daily(T(10, 0))) with { Parameters = new Dictionary<string, JsonElement> { ["lookback"] = JsonSerializer.SerializeToElement(12) } });

        var parameter = (await world.ViewAsync("job")).Parameters.ShouldHaveSingleItem();

        (parameter.Name, parameter.Kind, parameter.Min, parameter.Max, parameter.Value.GetInt32(), parameter.Default.GetInt32(), parameter.Unit).ShouldBe(("lookback", "integer", 1d, 30d, 12, 5, "sessions"));
    }

    [Fact]
    public async Task The_status_is_running_while_a_run_is_in_progress_failed_when_the_latest_run_failed_and_healthy_after_a_skipped_run()
    {
        var gate = _eod.Gate();
        await _world.RunNowAsync("eod");
        await _eod.WhenStartedAsync();
        (await _world.ViewAsync("eod")).Status.ShouldBe("running");

        gate.SetResult(ServiceOutcome.Failed("Dhan refused the token"));
        await _world.Runner.WhenIdleAsync();
        var failed = await _world.ViewAsync("eod");
        (failed.Status, failed.LastRun!.Status, failed.LastRun.Message, failed.LastRun.Trigger).ShouldBe(("failed", "failed", "Dhan refused the token", "manual"));

        _world.SetClock(At(12, 11, 5));
        _eod.Behavior = (_, _) => ValueTask.FromResult(ServiceOutcome.Skipped("Not connected."));
        await _world.RunNowAsync("eod");
        await _world.Runner.WhenIdleAsync();
        (await _world.ViewAsync("eod")).Status.ShouldBe("healthy");
    }

    [Fact]
    public async Task A_service_with_a_stored_row_shows_its_revision_and_is_no_longer_the_default_when_it_differs()
    {
        var row = _world.Data.Store("eod", Settings(Schedule.Daily(T(16, 5)), retries: 2));

        var eod = await _world.ViewAsync("eod");

        (eod.Revision, eod.IsDefault, eod.Execution.Retries).ShouldBe(((long)row.Revision, false, 2));
    }

    [Fact]
    public async Task The_findings_over_the_whole_set_are_part_of_each_service()
    {
        _world.Data.Store("eod", Settings(Schedule.Daily(T(19, 0)))); // after the nightly's own time of 18:30

        var list = await _world.ListAsync();

        list.Items.Single(s => s.Id == "eod").Issues.ShouldBeEmpty();
        var nightly = list.Items.Single(s => s.Id == "nightly").Issues.ShouldHaveSingleItem();
        (nightly.Severity, nightly.Code, nightly.RelatedServiceId).ShouldBe(("warning", "must_follow", "eod"));
    }

    [Fact]
    public async Task One_service_by_id_or_not_found()
    {
        var get = await _world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<GetServiceQuery, Domain.Common.Result<ServiceView>>>().HandleAsync(new GetServiceQuery("eod"), Ct));
        var sync = await _world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<GetServiceQuery, Domain.Common.Result<ServiceView>>>().HandleAsync(new GetServiceQuery("account-sync"), Ct));
        var missing = await _world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<GetServiceQuery, Domain.Common.Result<ServiceView>>>().HandleAsync(new GetServiceQuery("nobody"), Ct));

        get.Value.Id.ShouldBe("eod");
        sync.Value.ReadOnly.ShouldBeTrue();
        missing.Error.ShouldBe(ServiceErrors.NotFound);
    }

    // ---- save (15, 3, 4, 5) ----

    [Fact]
    public async Task A_save_stores_the_choices_audits_it_and_returns_the_service_with_its_new_revision()
    {
        var saved = await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 5)), retries: 2, timeout: 60, catchUp: false, notify: false));

        var view = saved.Value;
        (view.Schedule.Time, view.Execution, view.Revision, view.NextRun).ShouldBe(("16:05", new ExecutionView(2, 60, false, false), (long)_world.Data.Rows["eod"].Revision, At(12, 16, 5)));
        var row = _world.Data.Rows["eod"];
        (row.UpdatedBy, row.Retries, row.TimeoutMinutes).ShouldBe((Owner, 2, 60));
        var audit = _world.Data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Actor, audit.AccountId).ShouldBe((AccountEventType.ServiceScheduleChanged, Owner, null));
        var detail = audit.Detail.ShouldNotBeNull();
        detail.ShouldContain("eod:");
        detail.ShouldContain("retries 0 → 2");
        detail.ShouldContain("→ Every trading day at 16:05 IST");
    }

    [Fact]
    public async Task A_save_made_on_a_stale_revision_is_refused_and_stores_nothing()
    {
        var first = (await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 5))))).Value;

        var stale = await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 30)), revision: 0));
        var current = await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 30)), revision: first.Revision));

        stale.Error.ShouldBe(ServiceErrors.RevisionChanged);
        current.IsSuccess.ShouldBeTrue();
        current.Value.Schedule.Time.ShouldBe("16:30");
        _world.Data.Events.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_first_save_of_a_service_without_a_row_needs_revision_zero()
    {
        var wrong = await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 5)), revision: 7));

        wrong.Error.ShouldBe(ServiceErrors.RevisionChanged);
        _world.Data.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Two_saves_at_once_one_of_them_loses_with_revision_changed()
    {
        _world.Data.Collisions.Enqueue(() => _world.Data.Store("eod", Settings(Schedule.Daily(T(17, 0)))));

        var lost = await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 5))));

        lost.Error.ShouldBe(ServiceErrors.RevisionChanged);
        _world.Data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_save_that_changes_nothing_stores_and_audits_nothing()
    {
        var first = (await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 5))))).Value;
        var saves = _world.Data.Saves;

        var same = await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 5)), revision: first.Revision));

        same.Value.Revision.ShouldBe(first.Revision);
        (_world.Data.Saves, _world.Data.Events.Count).ShouldBe((saves, 1));
    }

    [Fact]
    public async Task An_unknown_service_is_not_found_and_the_account_sync_is_read_only()
    {
        (await _world.SaveAsync("nobody", Input(Schedule.Daily(T(10, 0))))).Error.ShouldBe(ServiceErrors.NotFound);
        (await _world.SaveAsync("account-sync", Input(Schedule.Daily(T(10, 0))))).Error.ShouldBe(ServiceErrors.ReadOnly);
        (await _world.PauseAsync("account-sync")).Error.ShouldBe(ServiceErrors.ReadOnly);
        (await _world.ResumeAsync("account-sync")).Error.ShouldBe(ServiceErrors.ReadOnly);
        (await _world.RunNowAsync("account-sync")).Error.ShouldBe(ServiceErrors.ReadOnly);
        (await _world.PreviewAsync("account-sync", Input(Schedule.Daily(T(10, 0))))).Error.ShouldBe(ServiceErrors.ReadOnly);
        _world.Data.Events.ShouldBeEmpty();
    }

    public static TheoryData<string, ScheduleSpec, int, int, string> RefusedSaves => new()
    {
        { "window start not before end", new ScheduleSpec("window", From: "10:00", To: "09:00", Days: ["mon"]), 0, 30, "schedule.to" },
        { "interval below 1", new ScheduleSpec("interval", From: "09:00", To: "10:00", Days: ["mon"], EveryMinutes: 0), 0, 30, "schedule.everyMinutes" },
        { "interval above 120", new ScheduleSpec("interval", From: "00:00", To: "23:00", Days: ["mon"], EveryMinutes: 121), 0, 30, "schedule.everyMinutes" },
        { "interval longer than its window", new ScheduleSpec("interval", From: "09:00", To: "09:30", Days: ["mon"], EveryMinutes: 45), 0, 30, "schedule.everyMinutes" },
        { "no day chosen", new ScheduleSpec("daily", "10:00", []), 0, 30, "schedule.days" },
        { "invalid time", new ScheduleSpec("daily", "25:00", ["mon"]), 0, 30, "schedule.time" },
        { "unknown type", new ScheduleSpec("hourly"), 0, 30, "schedule.type" },
        { "retries above 5", new ScheduleSpec("daily", "10:00", ["mon"]), 6, 30, "retries" },
        { "retries below 0", new ScheduleSpec("daily", "10:00", ["mon"]), -1, 30, "retries" },
        { "timeout below 1", new ScheduleSpec("daily", "10:00", ["mon"]), 0, 0, "timeoutMinutes" },
        { "timeout above 240", new ScheduleSpec("daily", "10:00", ["mon"]), 0, 241, "timeoutMinutes" },
        { "a circular chain", new ScheduleSpec("after", After: "nightly"), 0, 30, "schedule.after" },
    };

    [Theory]
    [MemberData(nameof(RefusedSaves))]
    public async Task A_save_is_refused_with_services_invalid_naming_the_field_and_stores_nothing(string reason, ScheduleSpec schedule, int retries, int timeout, string field)
    {
        _ = reason;
        if (schedule.After == "nightly")
        {
            _world.Data.Store("nightly", Settings(Schedule.AfterService("eod")));
        }

        var result = await _world.SaveAsync("eod", new ServiceScheduleInput(true, schedule, retries, timeout, true, true, null, 0));

        result.Error.Code.ShouldBe(ServiceErrors.InvalidCode);
        var fields = result.Error.Fields.ShouldNotBeNull();
        fields.Select(f => f.Field).ShouldContain(field);
        fields.ShouldAllBe(f => f.Code != null);
        _world.Data.Rows.Keys.Except(["nightly"]).ShouldBeEmpty();
        _world.Data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_schedule_type_the_service_does_not_allow_is_refused()
    {
        using var world = new ServicesWorld(Make("job", Schedule.Daily(T(10, 0)), allowed: [ScheduleType.Daily]));
        world.SetClock(At(12, 11, 0));

        var result = await world.SaveAsync("job", Input(Schedule.Continuous()));

        Fields(result.Error).ShouldBe(["schedule.type"]);
    }

    [Fact]
    public async Task An_unknown_parameter_and_one_outside_its_bounds_are_refused_with_their_fields()
    {
        var parameters = new[] { ParameterDefinition.Whole("lookback", "Look-back", 1, 30, 5) };
        using var world = new ServicesWorld(Make("job", Schedule.Daily(T(10, 0)), parameters: parameters));
        world.SetClock(At(12, 11, 0));
        var given = new Dictionary<string, JsonElement> { ["lookback"] = JsonSerializer.SerializeToElement(99), ["mystery"] = JsonSerializer.SerializeToElement(1) };

        var result = await world.SaveAsync("job", Input(Schedule.Daily(T(10, 0)), parameters: given));

        Fields(result.Error).Order().ShouldBe(["parameters.lookback", "parameters.mystery"]);
    }

    [Fact]
    public async Task A_circle_through_another_service_blames_each_service_in_it()
    {
        _world.Data.Store("nightly", Settings(Schedule.AfterService("eod")));

        var result = await _world.SaveAsync("eod", Input(Schedule.AfterService("nightly")));

        result.Error.Fields!.Select(f => (f.Scope, f.Field)).Order().ShouldBe([(null, "schedule.after"), ("nightly", "schedule.after")]);
    }

    [Fact]
    public async Task Warnings_are_saved_and_come_back_with_the_service()
    {
        var saved = await _world.SaveAsync("eod", Input(Schedule.Daily(T(15, 20))));

        saved.IsSuccess.ShouldBeTrue();
        var warning = saved.Value.Issues.ShouldHaveSingleItem();
        (warning.ServiceId, warning.Field, warning.Severity, warning.Code).ShouldBe(("eod", "schedule.time", "warning", "start_too_early"));
        _world.Data.Rows["eod"].Schedule.ShouldBe(Schedule.Daily(T(15, 20)));
    }

    [Fact]
    public async Task Moving_the_end_of_day_run_after_a_dependent_services_time_is_reported_on_the_dependent_in_the_list()
    {
        await _world.SaveAsync("eod", Input(Schedule.Daily(T(19, 0))));

        var nightly = await _world.ViewAsync("nightly");

        nightly.Issues.ShouldHaveSingleItem().Code.ShouldBe("must_follow");
    }

    [Fact]
    public async Task A_saved_parameter_is_kept_and_a_left_out_one_takes_the_default()
    {
        var parameters = new[] { ParameterDefinition.Whole("lookback", "Look-back", 1, 30, 5), ParameterDefinition.Whole("depth", "Depth", 1, 9, 3) };
        using var world = new ServicesWorld(Make("job", Schedule.Daily(T(10, 0)), parameters: parameters));
        world.SetClock(At(12, 11, 0));

        var saved = await world.SaveAsync("job", Input(Schedule.Daily(T(10, 0)), parameters: new Dictionary<string, JsonElement> { ["lookback"] = JsonSerializer.SerializeToElement(12) }));

        saved.Value.Parameters.Select(p => (p.Name, p.Value.GetInt32())).ShouldBe([("lookback", 12), ("depth", 3)]);
        world.Data.Events.Single().Detail!.ShouldContain("parameter lookback (default) → 12");
    }

    // ---- preview (15) ----

    [Fact]
    public async Task A_preview_gives_the_next_three_runs_the_words_and_the_findings_and_stores_nothing()
    {
        var preview = (await _world.PreviewAsync("eod", Input(Schedule.Daily(T(15, 20))))).Value;

        preview.NextRuns.ShouldBe([At(12, 15, 30), At(13, 15, 30), At(14, 15, 30)]); // not before the session ends
        preview.ScheduleWords.ShouldBe("Every trading day at 15:20 IST, never before the session has ended");
        preview.Issues.Select(i => (i.Severity, i.Code)).ShouldBe([("warning", "start_too_early")]);
        _world.Data.Rows.ShouldBeEmpty();
        _world.Data.Events.ShouldBeEmpty();
        _world.Data.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_preview_with_an_error_names_it_and_lists_no_runs()
    {
        var preview = (await _world.PreviewAsync("eod", Input(Schedule.Window(T(10, 0), T(9, 0))))).Value;

        preview.NextRuns.ShouldBeEmpty();
        preview.Issues.Where(i => i.Severity == "error").Select(i => i.Field).ShouldBe(["schedule.to"]);
    }

    [Fact]
    public async Task A_preview_of_an_unreadable_schedule_reports_the_field_and_still_checks_the_rest()
    {
        var input = new ServiceScheduleInput(true, new ScheduleSpec("daily", "99:99", ["mon"]), 9, 30, true, true);

        var preview = (await _world.PreviewAsync("eod", input)).Value;

        preview.Issues.Select(i => i.Field).Order().ShouldBe(["retries", "schedule.time"]);
        preview.NextRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_preview_ignores_the_revision_and_an_unknown_service_is_not_found()
    {
        (await _world.PreviewAsync("eod", Input(Schedule.Daily(T(16, 0)), revision: 12345))).IsSuccess.ShouldBeTrue();
        (await _world.PreviewAsync("nobody", Input(Schedule.Daily(T(16, 0))))).Error.ShouldBe(ServiceErrors.NotFound);
    }

    [Fact]
    public async Task A_preview_of_a_service_that_follows_another_has_no_runs_of_its_own()
    {
        var preview = (await _world.PreviewAsync("nightly", Input(Schedule.AfterService("eod")))).Value;

        preview.NextRuns.ShouldBeEmpty();
        preview.Issues.ShouldBeEmpty();
        preview.ScheduleWords.ShouldBe("Each time Eod finishes successfully");
    }

    // ---- reset, pause, resume (15, 16) ----

    [Fact]
    public async Task Restore_default_writes_the_definitions_defaults_and_audits_it()
    {
        await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 5)), retries: 3, enabled: false));

        var reset = await _world.WithScopeAsync(sp => sp.GetRequiredService<ICommandHandler<ResetServiceScheduleCommand, ServiceView>>().HandleAsync(new ResetServiceScheduleCommand("eod", Owner), Ct));

        var view = reset.Value;
        (view.IsDefault, view.Enabled, view.Schedule.Time, view.Execution.Retries).ShouldBe((true, true, "15:50", 0));
        _world.Data.Rows.ShouldContainKey("eod"); // nothing is deleted
        var audit = _world.Data.Events.Last();
        (audit.Event, audit.Detail!).ShouldBe((AccountEventType.ServiceScheduleChanged, audit.Detail!));
        audit.Detail!.ShouldStartWith("Restored the default.");
    }

    [Fact]
    public async Task Pausing_and_resuming_are_audited_keep_every_other_choice_and_are_idempotent()
    {
        await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 5)), retries: 2));

        var paused = (await _world.PauseAsync("eod")).Value;
        await _world.PauseAsync("eod");
        var resumed = (await _world.ResumeAsync("eod")).Value;
        await _world.ResumeAsync("eod");

        (paused.Enabled, paused.Status, paused.NextRun, paused.Schedule.Time, paused.Execution.Retries).ShouldBe((false, "paused", null, "16:05", 2));
        (resumed.Enabled, resumed.Status, resumed.NextRun).ShouldBe((true, "healthy", At(12, 16, 5)));
        _world.Data.Events.Select(e => e.Event).ShouldBe([AccountEventType.ServiceScheduleChanged, AccountEventType.ServicePaused, AccountEventType.ServiceResumed]);
        _world.Data.Events.Skip(1).ShouldAllBe(e => e.Actor == Owner);
        _world.Data.Events[1].Detail.ShouldBe("eod: paused");
    }

    [Fact]
    public async Task Pausing_a_service_without_a_row_creates_the_row()
    {
        (await _world.PauseAsync("eod")).Value.Enabled.ShouldBeFalse();

        _world.Data.Rows["eod"].Enabled.ShouldBeFalse();
    }

    // ---- run now (12, 15, 16) ----

    [Fact]
    public async Task Run_now_works_on_a_paused_service_with_the_manual_trigger_and_is_audited()
    {
        await _world.PauseAsync("eod");

        var run = (await _world.RunNowAsync("eod")).Value;
        await _world.Runner.WhenIdleAsync();

        (run.Trigger, run.Status, run.ServiceId).ShouldBe(("manual", "running", "eod"));
        _eod.Calls.ShouldHaveSingleItem().RequestedBy.ShouldBe(Owner);
        _world.Data.Events.Select(e => e.Event).ShouldBe([AccountEventType.ServicePaused, AccountEventType.ServiceRunRequested]);
    }

    [Fact]
    public async Task Run_now_while_the_service_runs_is_refused_as_already_running_and_a_skipped_run_is_recorded()
    {
        var gate = _eod.Gate();
        (await _world.RunNowAsync("eod")).IsSuccess.ShouldBeTrue();
        await _eod.WhenStartedAsync();

        var again = await _world.RunNowAsync("eod");

        again.Error.ShouldBe(ServiceErrors.AlreadyRunning);
        _world.Data.RunsOf("eod").Count(r => r.Status == ServiceRunStatus.Skipped).ShouldBe(1);
        _world.Data.Events.Count(e => e.Event == AccountEventType.ServiceRunRequested).ShouldBe(2); // both requests are audited
        gate.SetResult(ServiceOutcome.Succeeded());
        await _world.Runner.WhenIdleAsync();
    }

    // ---- runs (14) ----

    [Fact]
    public async Task The_history_is_newest_first_in_pages_and_bounded()
    {
        for (var i = 0; i < 230; i++)
        {
            var run = ServiceRun.Start("eod", ServiceRunTrigger.Scheduled, 1, At(1, 0, 0, month: 9).AddMinutes(i));
            run.Succeed(run.StartedAt.AddSeconds(5));
            _world.Data.Runs.Add(run);
        }

        async Task<ServiceRunPage> PageAsync(int page, int size) =>
            (await _world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<ListServiceRunsQuery, Domain.Common.Result<ServiceRunPage>>>().HandleAsync(new ListServiceRunsQuery("eod", page, size), Ct))).Value;

        var first = await PageAsync(1, 100);
        var last = await PageAsync(3, 100);
        var beyond = await PageAsync(4, 100);

        (first.Items.Count, first.Total, first.Page).ShouldBe((100, 200, 1));
        first.Items.Select(r => r.StartedAt).ShouldBe(first.Items.Select(r => r.StartedAt).OrderDescending());
        first.Items[0].StartedAt.ShouldBe(At(1, 0, 0, month: 9).AddMinutes(229));
        last.Items.Count.ShouldBe(0); // only the latest 200 are shown
        beyond.Items.ShouldBeEmpty();
        (await PageAsync(2, 100)).Items.Count.ShouldBe(100);
    }

    [Theory]
    [InlineData(0, 25, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public async Task A_page_outside_its_bounds_is_refused(int page, int size, string field)
    {
        var result = await _world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<ListServiceRunsQuery, Domain.Common.Result<ServiceRunPage>>>().HandleAsync(new ListServiceRunsQuery("eod", page, size), Ct));

        Fields(result.Error).ShouldBe([field]);
    }

    [Fact]
    public async Task The_history_of_an_unknown_service_is_not_found_and_the_account_sync_has_none_here()
    {
        var handler = (IServiceProvider sp) => sp.GetRequiredService<IQueryHandler<ListServiceRunsQuery, Domain.Common.Result<ServiceRunPage>>>();

        (await _world.WithScopeAsync(sp => handler(sp).HandleAsync(new ListServiceRunsQuery("nobody"), Ct))).Error.ShouldBe(ServiceErrors.NotFound);
        (await _world.WithScopeAsync(sp => handler(sp).HandleAsync(new ListServiceRunsQuery("account-sync"), Ct))).Value.Items.ShouldBeEmpty();
    }

    // ---- timeline (14) ----

    private async Task<Domain.Common.Result<ServicesTimeline>> TimelineAsync(string? date = null) =>
        await _world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<GetServicesTimelineQuery, Domain.Common.Result<ServicesTimeline>>>().HandleAsync(new GetServicesTimelineQuery(date), Ct));

    [Fact]
    public async Task The_timeline_of_a_trading_day_has_market_hours_the_review_cutoff_and_every_enabled_services_marks()
    {
        var timeline = (await TimelineAsync("2026-10-12")).Value;

        (timeline.Date, timeline.TradingDay, timeline.MarketOpen, timeline.MarketClose, timeline.ReviewCutoff).ShouldBe(("2026-10-12", true, "09:15", "15:30", "09:05"));
        timeline.Entries.Select(e => (e.ServiceId, e.Kind, string.Join(',', e.Marks.Select(m => m.Time)))).ShouldBe([("eod", "mark", "15:50"), ("nightly", "mark", "18:30")]);
    }

    [Fact]
    public async Task Without_a_date_the_timeline_is_today_in_India_and_a_weekend_has_no_market_hours_or_marks()
    {
        (await TimelineAsync()).Value.Date.ShouldBe("2026-10-12");

        var weekend = (await TimelineAsync("2026-10-10")).Value;

        (weekend.TradingDay, weekend.MarketOpen, weekend.MarketClose).ShouldBe((false, null, null));
        weekend.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_special_session_gives_its_own_hours_and_moves_the_end_of_day_mark()
    {
        _world.Calendar = new TradingCalendar([TradingDay.Special(new DateOnly(2026, 11, 8), 18 * 60 + 15, 19 * 60 + 15, CalendarSource.Manual, CalendarStatus.Confirmed, null, At(1, 0, 0))]);

        var timeline = (await TimelineAsync("2026-11-08")).Value;

        (timeline.TradingDay, timeline.MarketOpen, timeline.MarketClose).ShouldBe((true, "18:15", "19:15"));
        timeline.Entries.Single(e => e.ServiceId == "eod").Marks.Single().Time.ShouldBe("19:15");
    }

    [Fact]
    public async Task A_paused_service_is_not_on_the_timeline_and_a_run_after_service_follows_its_predecessors_mark()
    {
        _world.Data.Store("nightly", Settings(Schedule.AfterService("eod")));
        var followed = (await TimelineAsync("2026-10-12")).Value;
        var after = followed.Entries.Single(e => e.ServiceId == "nightly");
        (after.FollowsServiceId, after.Marks.Single().Time).ShouldBe(("eod", "15:50"));

        await _world.PauseAsync("eod");
        var paused = (await TimelineAsync("2026-10-12")).Value;
        paused.Entries.ShouldBeEmpty(); // eod is paused, and nightly has nothing to follow
    }

    [Fact]
    public async Task Windows_intervals_and_always_on_services_are_bands()
    {
        using var world = new ServicesWorld(
            Make("feed", Schedule.Window(T(9, 0), T(15, 35))),
            Make("alerts", Schedule.Interval(5, T(9, 15), T(15, 30))),
            Make("watch", Schedule.Continuous()));
        world.SetClock(At(12, 11, 0));

        var timeline = (await world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<GetServicesTimelineQuery, Domain.Common.Result<ServicesTimeline>>>().HandleAsync(new GetServicesTimelineQuery("2026-10-12"), Ct))).Value;

        timeline.Entries.Select(e => (e.ServiceId, e.Kind, e.Band!.From, e.Band.To, e.EveryMinutes)).ShouldBe(
        [
            ("feed", "band", "09:00", "15:35", (int?)null),
            ("alerts", "band", "09:15", "15:30", 5),
            ("watch", "band", "00:00", "24:00", null),
        ]);
    }

    [Fact]
    public async Task An_unreadable_date_is_refused()
    {
        var result = await TimelineAsync("12/10/2026");

        Fields(result.Error).ShouldBe(["date"]);
    }

    // ---- notices (17) ----

    private async Task<ServiceNotices> NoticesAsync() =>
        await _world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<GetServiceNoticesQuery, ServiceNotices>>().HandleAsync(new GetServiceNoticesQuery(), Ct));

    [Fact]
    public async Task The_latest_failed_run_of_a_service_with_notify_on_failure_is_a_notice_and_the_next_success_clears_it()
    {
        _eod.AlwaysFail("Dhan refused the token");
        await _world.RunNowAsync("eod");
        await _world.Runner.WhenIdleAsync();

        var notice = (await NoticesAsync()).Items.ShouldHaveSingleItem();

        var run = _world.Data.RunsOf("eod").Single();
        (notice.Id, notice.Kind, notice.Severity, notice.ServiceId).ShouldBe(($"service:failed:{run.Id}", "service_failed", "error", "eod"));
        notice.Text.ShouldBe("Eod failed on 12 Oct: Dhan refused the token");

        _world.SetClock(At(12, 11, 30));
        _eod.Behavior = (_, _) => ValueTask.FromResult(ServiceOutcome.Succeeded());
        await _world.RunNowAsync("eod");
        await _world.Runner.WhenIdleAsync();
        (await NoticesAsync()).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_service_without_notify_on_failure_raises_no_notice()
    {
        using var world = new ServicesWorld(Eod(notify: false));
        world.SetClock(At(12, 11, 0));
        world.Services[0].AlwaysFail("x");
        await world.RunNowAsync("eod");
        await world.Runner.WhenIdleAsync();

        (await world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<GetServiceNoticesQuery, ServiceNotices>>().HandleAsync(new GetServiceNoticesQuery(), Ct))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_attempt_that_will_be_retried_is_not_a_notice_until_the_retries_are_used_up()
    {
        using var world = new ServicesWorld(Eod(retries: 1));
        world.SetClock(At(12, 11, 0));
        world.Services[0].AlwaysFail("x");
        Task<ServiceNotices> Notices() => world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<GetServiceNoticesQuery, ServiceNotices>>().HandleAsync(new GetServiceNoticesQuery(), Ct));

        await world.RunNowAsync("eod");
        await world.Runner.WhenIdleAsync();
        (await Notices()).Items.ShouldBeEmpty();

        world.SetClock(At(12, 11, 1));
        await world.Scheduler.TickAsync(Ct);
        await world.Runner.WhenIdleAsync();
        (await Notices()).Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_interrupted_run_is_a_notice_even_with_retries_left()
    {
        using var world = new ServicesWorld(Eod(retries: 3));
        world.SetClock(At(12, 11, 0));
        var run = ServiceRun.Start("eod", ServiceRunTrigger.Scheduled, 1, At(12, 10, 0));
        run.Fail(At(12, 10, 59), ServiceScheduler.InterruptedMessage);
        world.Data.Runs.Add(run);

        var notices = await world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<GetServiceNoticesQuery, ServiceNotices>>().HandleAsync(new GetServiceNoticesQuery(), Ct));

        notices.Items.ShouldHaveSingleItem().Text.ShouldContain("Interrupted");
    }

    [Fact]
    public async Task A_skipped_run_after_a_failure_clears_the_notice()
    {
        _eod.AlwaysFail("x");
        await _world.RunNowAsync("eod");
        await _world.Runner.WhenIdleAsync();
        (await NoticesAsync()).Items.ShouldHaveSingleItem();

        _world.SetClock(At(12, 11, 5));
        _world.Data.Runs.Add(ServiceRun.Skipped("eod", ServiceRunTrigger.Scheduled, "The Data account is not connected.", At(12, 11, 5)));

        (await NoticesAsync()).Items.ShouldBeEmpty();
    }

    // ---- concurrent edits (pause, resume and restore default never overwrite them) ----

    [Fact]
    public async Task A_pause_keeps_a_save_that_was_made_between_reading_the_service_and_writing()
    {
        var first = (await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 5))))).Value;
        _world.Data.OnFind = () =>
        {
            _world.Data.OnFind = null;
            _world.Data.Store("eod", Settings(Schedule.Daily(T(17, 30)), retries: 3)); // another request saved meanwhile
        };

        var paused = await _world.PauseAsync("eod");

        paused.IsSuccess.ShouldBeTrue();
        var row = _world.Data.Rows["eod"];
        (row.Enabled, row.Schedule, row.Retries).ShouldBe((false, Schedule.Daily(T(17, 30)), 3));
        paused.Value.Revision.ShouldBeGreaterThan(first.Revision);
    }

    [Fact]
    public async Task A_resume_keeps_a_concurrent_save_too()
    {
        await _world.PauseAsync("eod");
        _world.Data.OnFind = () =>
        {
            _world.Data.OnFind = null;
            _world.Data.Store("eod", Settings(Schedule.Daily(T(17, 30)), enabled: false, timeout: 99));
        };

        (await _world.ResumeAsync("eod")).IsSuccess.ShouldBeTrue();

        var row = _world.Data.Rows["eod"];
        (row.Enabled, row.Schedule, row.TimeoutMinutes).ShouldBe((true, Schedule.Daily(T(17, 30)), 99));
    }

    [Fact]
    public async Task Restore_default_after_a_concurrent_save_is_refused_with_revision_changed_and_changes_nothing()
    {
        await _world.SaveAsync("eod", Input(Schedule.Daily(T(16, 5))));
        _world.Data.OnFind = () =>
        {
            _world.Data.OnFind = null;
            _world.Data.Store("eod", Settings(Schedule.Daily(T(17, 30))));
        };

        var reset = await _world.WithScopeAsync(sp => sp.GetRequiredService<ICommandHandler<ResetServiceScheduleCommand, ServiceView>>().HandleAsync(new ResetServiceScheduleCommand("eod", Owner), Ct));

        reset.Error.ShouldBe(ServiceErrors.RevisionChanged);
        _world.Data.Rows["eod"].Schedule.ShouldBe(Schedule.Daily(T(17, 30)));
    }

    // ---- a stored schedule that cannot be read ----

    private ServiceSchedule StoreUnreadable(string id, bool enabled = true)
    {
        var row = _world.Data.Store(id, Settings(Schedule.Unreadable, enabled: enabled));
        row.ScheduleReadable.ShouldBeFalse();
        return row;
    }

    [Fact]
    public async Task A_row_whose_schedule_cannot_be_read_degrades_that_one_service_to_its_defaults_with_an_error()
    {
        StoreUnreadable("eod");

        var list = await _world.ListAsync();

        var eod = list.Items.Single(s => s.Id == "eod");
        (eod.Schedule.Time, eod.Enabled, eod.Status).ShouldBe(("15:50", true, "healthy"));
        var issue = eod.Issues[0];
        (issue.Severity, issue.Code, issue.ServiceId).ShouldBe(("error", "schedule.stored_invalid", "eod"));
        list.Items.Single(s => s.Id == "nightly").Issues.ShouldBeEmpty();
        list.Items.Count.ShouldBe(3); // everything else is read as usual
    }

    [Fact]
    public async Task A_paused_row_with_an_unreadable_schedule_stays_paused()
    {
        StoreUnreadable("eod", enabled: false);

        (await _world.ViewAsync("eod")).Status.ShouldBe("paused");
    }

    [Fact]
    public async Task The_scheduler_runs_on_the_defaults_of_a_service_with_an_unreadable_row_and_keeps_the_others_going()
    {
        StoreUnreadable("eod");
        _world.SetClock(At(12, 10, 0));
        _world.Application.StartedAt = At(12, 10, 0);
        await _world.Scheduler.StartUpAsync(Ct);

        _world.SetClock(At(12, 15, 50));
        await _world.Scheduler.TickAsync(Ct);
        _world.SetClock(At(12, 18, 30));
        await _world.Scheduler.TickAsync(Ct);
        await _world.Runner.WhenIdleAsync();

        _eod.Calls.Count.ShouldBe(1);
        _nightly.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Saving_a_new_schedule_replaces_an_unreadable_one_even_when_it_equals_the_defaults()
    {
        var row = StoreUnreadable("eod");

        var saved = await _world.SaveAsync("eod", Input(Schedule.Daily(T(15, 50)), revision: row.Revision));

        saved.IsSuccess.ShouldBeTrue();
        _world.Data.Rows["eod"].ScheduleReadable.ShouldBeTrue();
        saved.Value.Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task Pausing_a_service_with_an_unreadable_row_writes_a_readable_schedule()
    {
        StoreUnreadable("eod");

        (await _world.PauseAsync("eod")).IsSuccess.ShouldBeTrue();

        var row = _world.Data.Rows["eod"];
        (row.ScheduleReadable, row.Enabled, row.Schedule).ShouldBe((true, false, Schedule.Daily(T(15, 50))));
    }
}
