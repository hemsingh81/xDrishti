using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.Platform.Services.ServicesWorld;
using static XDrishti.Application.Tests.Platform.Services.Svc;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>
/// The recovery runner over fakes (ADR 0240, AU-02): one pass in plan order, one step at a time through the normal runner with trigger catch_up, a failure blocks the dependents, attempts are bounded, the three triggers are
/// debounced and cannot stack, a crash is resumed without a second run of what succeeded, market hours wait, cancellation ends the pass. The clock starts on Monday 2026-10-12, 10:00 India time; the three services have stored
/// rows (baseline: the epoch), so every day since is missed.
/// </summary>
public sealed class RecoveryRunnerTests : IDisposable
{
    private readonly TestService _eod = Eod();
    private readonly TestService _learn = Make("learn", Schedule.Daily(T(16, 30)), constraints: new ServiceConstraints { MustFollow = ["eod"] });
    private readonly TestService _other = Make("other", Schedule.Daily(T(16, 0)));
    private readonly ServicesWorld _world;
    private readonly List<string> _order = [];

    public RecoveryRunnerTests()
    {
        _world = new ServicesWorld(_eod, _learn, _other);
        foreach (var service in _world.Services)
        {
            var id = service.Definition.Id;
            _world.Data.Store(id, service.Definition.Defaults);
            service.Behavior = (_, _) =>
            {
                lock (_order)
                {
                    _order.Add(id);
                }

                return ValueTask.FromResult(ServiceOutcome.Succeeded());
            };
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _world.Dispose();

    private async Task StartUpAtAsync(DateTimeOffset instant)
    {
        _world.SetClock(instant);
        _world.Application.StartedAt = instant;
        await _world.Scheduler.StartUpAsync(Ct);
        await _world.SettleAsync();
    }

    private async Task RequestAtAsync(DateTimeOffset instant, RecoveryTrigger trigger = RecoveryTrigger.Wake)
    {
        _world.SetClock(instant);
        _world.Recovery.Request(trigger);
        await _world.SettleAsync();
    }

    [Fact]
    public async Task A_start_up_pass_runs_the_missed_steps_in_plan_order_each_through_the_runner_with_the_catch_up_trigger_linked_by_run_id()
    {
        await StartUpAtAsync(At(12, 17, 0));

        _order.IndexOf("eod").ShouldBeLessThan(_order.IndexOf("learn")); // learn follows eod
        _order.Order().ShouldBe(["eod", "learn", "other"]);
        _world.Data.Runs.ShouldAllBe(r => r.Trigger == ServiceRunTrigger.CatchUp && r.Status == ServiceRunStatus.Succeeded);
        var pass = _world.Ledger.Passes.ShouldHaveSingleItem();
        (pass.Trigger, pass.Status).ShouldBe(("startup", "succeeded"));
        foreach (var item in _world.Ledger.Items.Values)
        {
            item.State.ShouldBe(RecoveryStates.Succeeded);
            item.PassId.ShouldBe(pass.Id);
            item.RunId.ShouldBe(_world.Data.RunsOf(item.ServiceId).Single().Id);
        }
    }

    [Fact]
    public async Task A_failed_step_blocks_what_follows_it_and_only_that_while_independent_branches_continue()
    {
        _eod.AlwaysFail("the data source is down");

        await StartUpAtAsync(At(12, 17, 0));

        _learn.Calls.ShouldBeEmpty();
        _other.Calls.Count.ShouldBe(1);
        var eod = _world.Ledger.Items[("eod", "2026-10-12")];
        (eod.State, eod.Attempts, eod.Reason).ShouldBe((RecoveryStates.Failed, 1, "the data source is down"));
        var learn = _world.Ledger.Items[("learn", "2026-10-12")];
        learn.State.ShouldBe(RecoveryStates.Blocked);
        learn.Reason.ShouldNotBeNull().ShouldContain("eod");
        learn.Attempts.ShouldBe(0); // it never ran: no attempt used
        _world.Ledger.Passes.Single().Status.ShouldBe("incomplete");
    }

    [Fact]
    public async Task A_blocked_step_runs_at_the_next_trigger_once_what_it_follows_has_succeeded()
    {
        _eod.AlwaysFail("down");
        await StartUpAtAsync(At(12, 17, 0));
        _eod.Behavior = (_, _) => ValueTask.FromResult(ServiceOutcome.Succeeded());

        await RequestAtAsync(At(12, 17, 11));

        _world.Ledger.Items[("eod", "2026-10-12")].State.ShouldBe(RecoveryStates.Succeeded);
        _world.Ledger.Items[("learn", "2026-10-12")].State.ShouldBe(RecoveryStates.Succeeded);
        _learn.Calls.Count.ShouldBe(1);
        _other.Calls.Count.ShouldBe(1); // not run twice
    }

    [Fact]
    public async Task A_failed_step_is_tried_at_each_trigger_at_most_max_attempts_times_then_stays_failed_with_one_notice_per_service_and_day()
    {
        _eod.AlwaysFail("down");
        await StartUpAtAsync(At(12, 17, 0));
        await RequestAtAsync(At(12, 17, 11));
        await RequestAtAsync(At(12, 17, 22));
        await RequestAtAsync(At(12, 17, 33)); // the fourth trigger: no attempt is left

        _eod.Calls.Count.ShouldBe(3);
        var item = _world.Ledger.Items[("eod", "2026-10-12")];
        (item.State, item.Attempts).ShouldBe((RecoveryStates.Failed, 3));
        _learn.Calls.ShouldBeEmpty(); // still blocked by the step that stays failed
        var notices = await _world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<GetServiceNoticesQuery, ServiceNotices>>().HandleAsync(new GetServiceNoticesQuery(), Ct));
        notices.Items.Count(n => n.Kind == GetServiceNoticesHandler.RecoveryFailed && n.ServiceId == "eod").ShouldBe(1);
    }

    [Fact]
    public async Task Triggers_inside_the_debounce_or_while_a_pass_runs_start_nothing_and_record_no_second_pass()
    {
        var gate = _eod.Gate();
        _world.SetClock(At(12, 17, 0));
        _world.Application.StartedAt = At(12, 17, 0);
        await _world.Scheduler.StartUpAsync(Ct);
        await _eod.WhenStartedAsync();

        _world.SetClock(At(12, 17, 30)); // past the debounce, but the pass still runs
        _world.Recovery.Request(RecoveryTrigger.BrokerReconnect).ShouldBeFalse();
        _world.Recovery.Request(RecoveryTrigger.Wake).ShouldBeFalse();
        gate.SetResult(ServiceOutcome.Succeeded());
        await _world.SettleAsync();

        _world.Ledger.Passes.Count.ShouldBe(1);
        _eod.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_debounce_is_ten_minutes_by_default_from_the_last_accepted_trigger_and_a_trigger_after_it_is_accepted()
    {
        await StartUpAtAsync(At(12, 17, 0));
        _world.Recovery.Request(RecoveryTrigger.Wake).ShouldBeFalse(); // same instant: refused

        _world.SetClock(At(12, 17, 9));
        _world.Recovery.Request(RecoveryTrigger.BrokerReconnect).ShouldBeFalse();
        _world.SetClock(At(12, 17, 10));
        _world.Recovery.Request(RecoveryTrigger.BrokerReconnect).ShouldBeTrue();
        await _world.SettleAsync();
    }

    [Fact]
    public async Task A_burst_of_triggers_at_once_starts_exactly_one_pass()
    {
        _world.SetClock(At(12, 17, 0));
        var accepted = Enumerable.Range(0, 20).AsParallel().Select(_ => _world.Recovery.Request(RecoveryTrigger.Wake)).ToList();
        await _world.SettleAsync();

        accepted.Count(a => a).ShouldBe(1);
    }

    [Fact]
    public async Task After_a_crash_an_item_left_running_becomes_failed_interrupted_and_runs_again_but_a_step_that_succeeded_does_not()
    {
        var old = await _world.Ledger.BeginPassAsync("startup", At(12, 16, 0), Ct);
        await _world.Ledger.SaveAsync(new RecoveryItemRow(Guid.NewGuid(), old, "eod", "2026-10-12", new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12), RecoveryStates.Running, null, 1, null), Ct);
        await _world.Ledger.SaveAsync(new RecoveryItemRow(Guid.NewGuid(), old, "other", "2026-10-12", new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12), RecoveryStates.Succeeded, null, 1, Guid.NewGuid()), Ct);

        await StartUpAtAsync(At(12, 17, 0));

        _world.Ledger.Passes.First().Status.ShouldBe("interrupted");
        var eod = _world.Ledger.Items[("eod", "2026-10-12")];
        (eod.State, eod.Attempts).ShouldBe((RecoveryStates.Succeeded, 2)); // the interrupted attempt counted
        _other.Calls.ShouldBeEmpty(); // already made up: never started again
        _learn.Calls.Count.ShouldBe(1);
    }

    private sealed class StubProof(bool all) : IRecoveryProof
    {
        public string ServiceId => "eod";

        public List<IReadOnlyList<DateOnly>> Asked { get; } = [];

        public ValueTask<RecoveryProofResult> ProveAsync(IReadOnlyList<DateOnly> sessions, DateTimeOffset runStartedAt, CancellationToken cancellationToken)
        {
            Asked.Add(sessions);
            return ValueTask.FromResult(all ? new RecoveryProofResult(true, "{\"ok\":true}", null) : new RecoveryProofResult(false, "{\"ok\":false}", "1 of 2 sessions recovered; not recovered: 2026-10-09 still_missing"));
        }
    }

    private static ServicesWorld ProvedWorld(StubProof proof, TestService eod, TestService learn)
    {
        var world = new ServicesWorld((_, _) => [eod, learn], p => p.Singleton<IRecoveryProof>(proof));
        foreach (var service in world.Services)
        {
            world.Data.Store(service.Definition.Id, service.Definition.Defaults);
        }

        return world;
    }

    [Fact]
    public async Task A_run_whose_proof_finds_a_session_not_recovered_leaves_a_partial_item_with_its_detail_and_the_dependents_still_run()
    {
        var eod = Eod();
        var learn = Make("learn", Schedule.Daily(T(16, 30)), constraints: new ServiceConstraints { MustFollow = ["eod"] });
        var proof = new StubProof(false);
        using var world = ProvedWorld(proof, eod, learn);
        world.SetClock(At(12, 17, 0));
        world.Application.StartedAt = At(12, 17, 0);

        await world.Scheduler.StartUpAsync(Ct);
        await world.SettleAsync();

        var item = world.Ledger.Items[("eod", "2026-10-12")];
        (item.State, item.Detail).ShouldBe((RecoveryStates.Partial, "{\"ok\":false}"));
        item.Reason.ShouldNotBeNull().ShouldContain("2026-10-09 still_missing");
        learn.Calls.Count.ShouldBe(1); // its own fail-closed gates decide, the runner does not block it
        world.Ledger.Items[("learn", "2026-10-12")].State.ShouldBe(RecoveryStates.Succeeded);
        world.Ledger.Passes.Single().Status.ShouldBe("incomplete");
        proof.Asked.ShouldHaveSingleItem().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_proof_that_finds_everything_recovered_keeps_the_item_succeeded_and_stores_the_detail()
    {
        var eod = Eod();
        var learn = Make("learn", Schedule.Daily(T(16, 30)), constraints: new ServiceConstraints { MustFollow = ["eod"] });
        using var world = ProvedWorld(new StubProof(true), eod, learn);
        world.SetClock(At(12, 17, 0));
        world.Application.StartedAt = At(12, 17, 0);

        await world.Scheduler.StartUpAsync(Ct);
        await world.SettleAsync();

        var item = world.Ledger.Items[("eod", "2026-10-12")];
        (item.State, item.Detail, item.Reason).ShouldBe((RecoveryStates.Succeeded, "{\"ok\":true}", null));
        world.Ledger.Passes.Single().Status.ShouldBe("succeeded");
    }

    [Fact]
    public async Task A_service_in_the_market_hours_of_a_special_session_resumes_after_its_real_close_and_not_before_its_own_earliest_start()
    {
        var backup = Make("backup", Schedule.Daily(T(23, 30), false, Weekdays.EveryDay), constraints: new ServiceConstraints { MustNotOverlap = TimeRange.MarketHours, StartNotBefore = T(16, 0) });
        using var world = new ServicesWorld(backup);
        world.Data.Store("backup", backup.Definition.Defaults);
        world.Calendar = new TradingCalendar([TradingDay.Special(new DateOnly(2026, 10, 12), 9 * 60 + 15, 13 * 60, CalendarSource.Manual, CalendarStatus.Confirmed, null, At(1, 0, 0))]);
        world.SetClock(At(12, 12, 0));
        world.Application.StartedAt = At(12, 12, 0);

        await world.Scheduler.StartUpAsync(Ct);
        await world.SettleAsync();

        world.Ledger.Items.Values.ShouldHaveSingleItem().State.ShouldBe(RecoveryStates.Waiting);
        world.Recovery.ResumeAt.ShouldBe(At(12, 16, 0)); // the later of the close (13:01) and its own earliest start

        world.SetClock(At(12, 13, 30)); // after the special session's close: not inside any market hours, runs now
        world.Recovery.Request(RecoveryTrigger.Wake);
        await world.SettleAsync();

        backup.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_run_the_owner_started_is_not_taken_for_a_scheduled_failure_and_the_step_runs_again()
    {
        var manual = ServiceRun.Start("eod", ServiceRunTrigger.Manual, 1, At(9, 16, 30));
        manual.Fail(At(9, 16, 31), "boom");
        _world.Data.Runs.Add(manual);
        var pass = await _world.Ledger.BeginPassAsync("startup", At(9, 16, 0), Ct);
        var friday = new DateOnly(2026, 10, 9);
        await _world.Ledger.SaveAsync(new RecoveryItemRow(Guid.NewGuid(), pass, "eod", "2026-10-09", friday, friday, RecoveryStates.Failed, "boom", 1, null), Ct);

        await StartUpAtAsync(At(9, 17, 0));

        _eod.Calls.Count.ShouldBe(1);
        _world.Ledger.Items[("eod", "2026-10-09")].State.ShouldBe(RecoveryStates.Succeeded);
    }

    [Fact]
    public async Task A_service_that_must_not_run_in_market_hours_waits_and_a_later_look_resumes_it_after_the_close()
    {
        using var world = new ServicesWorld(Backup());
        world.Data.Store("backup", Backup().Definition.Defaults);
        world.SetClock(At(12, 11, 0));
        world.Application.StartedAt = At(12, 11, 0);
        await world.Scheduler.StartUpAsync(Ct);
        await world.SettleAsync();

        world.Services[0].Calls.ShouldBeEmpty();
        var waiting = world.Ledger.Items.Values.ShouldHaveSingleItem();
        (waiting.State, waiting.Reason).ShouldBe((RecoveryStates.Waiting, RecoveryRunner.WaitingMarketHours));
        world.Recovery.ResumeAt.ShouldBe(At(12, 15, 31));

        world.SetClock(At(12, 15, 30));
        await world.Scheduler.TickAsync(Ct);
        await world.SettleAsync();
        world.Services[0].Calls.ShouldBeEmpty(); // the market hours include 15:30

        world.SetClock(At(12, 15, 32));
        await world.Scheduler.TickAsync(Ct);
        await world.SettleAsync();

        world.Services[0].Calls.Count.ShouldBe(1);
        world.Ledger.Items.Values.Single().State.ShouldBe(RecoveryStates.Succeeded);
        world.Ledger.Passes.Select(p => p.Trigger).ShouldBe(["startup", "resume"]);
    }

    [Fact]
    public async Task Stopping_the_application_ends_the_pass_as_interrupted_and_it_starts_nothing_more()
    {
        var gate = _eod.Gate();
        _world.SetClock(At(12, 17, 0));
        _world.Application.StartedAt = At(12, 17, 0);
        await _world.Scheduler.StartUpAsync(Ct);
        await _eod.WhenStartedAsync();

        await _world.Recovery.StopAsync(TimeSpan.FromSeconds(10));
        gate.TrySetResult(ServiceOutcome.Succeeded());
        await _world.Runner.WhenIdleAsync();

        _world.Ledger.Passes.Single().Status.ShouldBe("interrupted");
        _world.Ledger.Items[("eod", "2026-10-12")].State.ShouldBe(RecoveryStates.Running); // closed as failed (interrupted) by the next pass
        _learn.Calls.ShouldBeEmpty();
        _world.Recovery.Request(RecoveryTrigger.Wake).ShouldBeFalse();
    }

    [Fact]
    public async Task The_scheduler_and_the_runner_together_run_each_occurrence_exactly_once()
    {
        await StartUpAtAsync(At(12, 17, 0));
        _world.SetClock(At(12, 17, 1));
        await _world.Scheduler.TickAsync(Ct);
        await _world.SettleAsync();
        _world.SetClock(At(12, 18, 0));
        await _world.Scheduler.TickAsync(Ct);
        await _world.SettleAsync();

        _eod.Calls.Count.ShouldBe(1);
        _learn.Calls.Count.ShouldBe(1);
        _other.Calls.Count.ShouldBe(1);

        _world.SetClock(At(13, 15, 50)); // the next day's time comes: one scheduled run
        await _world.Scheduler.TickAsync(Ct);
        await _world.SettleAsync();
        _eod.Calls.Count.ShouldBe(2);
        _world.Data.RunsOf("eod").Select(r => r.Trigger.ToCode()).ShouldBe(["catch_up", "scheduled"]);
    }

    [Fact]
    public void A_service_without_a_row_or_a_run_counts_from_the_start_of_the_process_and_a_resumed_or_seeded_one_from_its_row()
    {
        var processStart = At(12, 16, 0);
        var service = Make("x", Schedule.Daily(T(9, 0)));
        RecoveryRunner.Baseline(new EffectiveService(service.Definition, service.Definition.Defaults, null), null, processStart).ShouldBe(processStart);
        var row = ServiceSchedule.Create("x", service.Definition.Defaults, "owner", At(10, 9, 0));
        RecoveryRunner.Baseline(new EffectiveService(service.Definition, service.Definition.Defaults, row), null, processStart).ShouldBe(At(10, 9, 0));
        var run = ServiceRun.Skipped("x", ServiceRunTrigger.Scheduled, "x", At(11, 9, 0));
        RecoveryRunner.Baseline(new EffectiveService(service.Definition, service.Definition.Defaults, row), run, processStart).ShouldBe(At(11, 9, 0));
        RecoveryRunner.Baseline(new EffectiveService(service.Definition, service.Definition.Defaults, null), run, processStart).ShouldBe(At(11, 9, 0));
        var seed = ServiceSchedule.Create("x", service.Definition.Defaults, ServiceScheduler.SeedRequester, At(12, 16, 0));
        RecoveryRunner.Baseline(new EffectiveService(service.Definition, service.Definition.Defaults, seed), null, processStart).ShouldBe(At(12, 16, 0)); // a first start: no surprise run
        RecoveryRunner.Baseline(new EffectiveService(service.Definition, service.Definition.Defaults, seed), run, processStart).ShouldBe(At(11, 9, 0)); // a run exists: the first-start row hides nothing
    }

    [Fact]
    public async Task A_service_that_runs_after_another_is_made_up_by_its_own_plan_step_never_started_by_the_run_after_chain_of_a_recovery_run()
    {
        using var world = new ServicesWorld(Eod(), Make("chain", Schedule.AfterService("eod")));
        world.Data.Store("eod", world.Services[0].Definition.Defaults);
        world.Data.Store("chain", world.Services[1].Definition.Defaults);
        world.SetClock(At(12, 17, 0));
        world.Application.StartedAt = At(12, 17, 0);

        await world.Scheduler.StartUpAsync(Ct);
        await world.SettleAsync();

        world.Data.RunsOf("chain").Select(r => r.Trigger).ShouldBe([ServiceRunTrigger.CatchUp]); // one run, from its own step, never an After run
        world.Services[1].Calls.Count.ShouldBe(1);
        world.Services[1].Calls[0].RequestedBy.ShouldBe(RecoveryRunner.Requester);
    }

    [Fact]
    public async Task A_trigger_refused_while_a_pass_runs_is_not_lost_one_trailing_pass_follows_at_the_next_look()
    {
        var gate = _eod.Gate();
        _world.SetClock(At(12, 17, 0));
        _world.Application.StartedAt = At(12, 17, 0);
        await _world.Scheduler.StartUpAsync(Ct);
        await _eod.WhenStartedAsync();
        _world.SetClock(At(12, 17, 5));
        _world.Recovery.Request(RecoveryTrigger.BrokerReconnect).ShouldBeFalse(); // refused: kept as pending
        gate.SetResult(ServiceOutcome.Failed("down"));
        await _world.SettleAsync();
        _eod.Behavior = (_, _) => ValueTask.FromResult(ServiceOutcome.Succeeded());

        _world.SetClock(At(12, 17, 6));
        await _world.Scheduler.TickAsync(Ct);
        await _world.SettleAsync();
        _eod.Calls.Count.ShouldBe(1); // inside the debounce: still waiting

        _world.SetClock(At(12, 17, 11));
        await _world.Scheduler.TickAsync(Ct);
        await _world.SettleAsync();

        _eod.Calls.Count.ShouldBe(2);
        _world.Ledger.Items[("eod", "2026-10-12")].State.ShouldBe(RecoveryStates.Succeeded);
        _learn.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_skipped_run_costs_no_attempt_makes_its_dependents_wait_and_is_tried_again_at_the_next_trigger()
    {
        _eod.Behavior = (_, _) => ValueTask.FromResult(ServiceOutcome.Skipped("The Data account is not connected."));
        await StartUpAtAsync(At(12, 17, 0));

        var eod = _world.Ledger.Items[("eod", "2026-10-12")];
        (eod.State, eod.Attempts).ShouldBe((RecoveryStates.Waiting, 0));
        eod.Reason.ShouldNotBeNull().ShouldContain("not connected");
        _world.Ledger.Items[("learn", "2026-10-12")].State.ShouldBe(RecoveryStates.Waiting);
        _learn.Calls.ShouldBeEmpty();
        _other.Calls.Count.ShouldBe(1);

        _eod.Behavior = (_, _) => ValueTask.FromResult(ServiceOutcome.Succeeded());
        await RequestAtAsync(At(12, 17, 11), RecoveryTrigger.BrokerReconnect);

        _world.Ledger.Items[("eod", "2026-10-12")].State.ShouldBe(RecoveryStates.Succeeded);
        _world.Ledger.Items[("learn", "2026-10-12")].State.ShouldBe(RecoveryStates.Succeeded);
    }

    [Fact]
    public async Task A_service_paused_while_the_pass_runs_is_not_started_and_nothing_that_follows_it_runs()
    {
        var gate = _eod.Gate();
        _world.SetClock(At(12, 17, 0));
        _world.Application.StartedAt = At(12, 17, 0);
        await _world.Scheduler.StartUpAsync(Ct);
        await _eod.WhenStartedAsync();

        (await _world.PauseAsync("learn")).IsSuccess.ShouldBeTrue();
        gate.SetResult(ServiceOutcome.Succeeded());
        await _world.SettleAsync();

        _learn.Calls.ShouldBeEmpty();
        _world.Ledger.Items.ContainsKey(("learn", "2026-10-12")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_later_step_closes_the_older_open_item_of_the_service_as_superseded()
    {
        var old = await _world.Ledger.BeginPassAsync("startup", At(9, 16, 0), Ct);
        await _world.Ledger.SaveAsync(new RecoveryItemRow(Guid.NewGuid(), old, "eod", "2026-10-09", new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 9), RecoveryStates.Failed, "down", 1, null), Ct);

        await StartUpAtAsync(At(12, 17, 0));

        _world.Ledger.Items[("eod", "2026-10-09")].State.ShouldBe(RecoveryStates.Superseded);
        _world.Ledger.Items[("eod", "2026-10-12")].State.ShouldBe(RecoveryStates.Succeeded);
    }

    [Fact]
    public async Task An_item_that_used_up_its_attempts_stays_failed_when_its_blocker_fails_again_and_a_blocked_item_runs_once_the_blocker_recovers()
    {
        var old = await _world.Ledger.BeginPassAsync("startup", At(12, 16, 0), Ct);
        var day = new DateOnly(2026, 10, 12);
        await _world.Ledger.SaveAsync(new RecoveryItemRow(Guid.NewGuid(), old, "learn", "2026-10-12", day, day, RecoveryStates.Failed, "boom", 3, null), Ct);
        _eod.AlwaysFail("down");

        await StartUpAtAsync(At(12, 17, 0));

        _world.Ledger.Items[("learn", "2026-10-12")].State.ShouldBe(RecoveryStates.Failed); // not overwritten as blocked: its notice stays
        _learn.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_older_failed_item_that_used_up_its_attempts_is_kept_as_evidence_when_a_later_step_runs()
    {
        var old = await _world.Ledger.BeginPassAsync("startup", At(9, 16, 0), Ct);
        await _world.Ledger.SaveAsync(new RecoveryItemRow(Guid.NewGuid(), old, "eod", "2026-10-09", new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 9), RecoveryStates.Failed, "down", 3, null), Ct);

        await StartUpAtAsync(At(12, 17, 0));

        _world.Ledger.Items[("eod", "2026-10-09")].State.ShouldBe(RecoveryStates.Failed);
    }

    [Fact]
    public async Task A_step_that_has_waited_for_a_session_a_day_old_raises_one_notice_per_service_and_day()
    {
        var pass = await _world.Ledger.BeginPassAsync("startup", At(12, 9, 0), Ct);
        var day = new DateOnly(2026, 10, 9);
        await _world.Ledger.SaveAsync(new RecoveryItemRow(Guid.NewGuid(), pass, "eod", "2026-10-09", day, day, RecoveryStates.Waiting, "waiting: The Data account is not connected.", 0, null), Ct);
        await _world.Ledger.SaveAsync(new RecoveryItemRow(Guid.NewGuid(), pass, "eod", "2026-10-08", day, day, RecoveryStates.Waiting, "waiting: again", 0, null), Ct);

        var notices = await _world.WithScopeAsync(sp => sp.GetRequiredService<IQueryHandler<GetServiceNoticesQuery, ServiceNotices>>().HandleAsync(new GetServiceNoticesQuery(), Ct));

        notices.Items.Count(n => n.Kind == GetServiceNoticesHandler.RecoveryWaiting && n.ServiceId == "eod").ShouldBe(1);
    }
}
