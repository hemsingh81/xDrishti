using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.LiveTrading;
using XDrishti.Application.Tests.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.LiveTrading;

/// <summary>
/// Spec P11-04b at the use-case level, on in-memory ports: the evaluator writes a daily halt once, a hold or a step-down by rule only when the state changes, the automatic step-up only with its switch ON and up to S2, the owner's clear and exit acts are
/// gated, and nothing here calls a broker. Session 2026-11-03 at noon, a live account with 100,000 fixed capital at S1 (1 R = 125.00).
/// </summary>
public sealed class RampEvaluationTests
{
    private const string Owner = "owner:hem@example.invalid";
    private static readonly DateTimeOffset Noon = new(2026, 11, 3, 12, 0, 0, TimeSpan.FromMinutes(330));
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly DateTimeOffset StepSet = new(2026, 10, 1, 20, 0, 0, TimeSpan.FromMinutes(330));

    private readonly AccountsFixture _fx = new();
    private readonly InMemoryRampStore _ramp;
    private readonly InMemoryLiveStore _capture = new();
    private readonly InMemoryExecutionStore _executions = new();
    private readonly InMemoryHaltStore _halts = new();
    private readonly FakeLiveMarket _market = new();
    private readonly Switches _switches = new();
    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();
    private readonly List<DateOnly> _sessions = [];
    private BrokerAccount _account = null!;

    public RampEvaluationTests()
    {
        _fx.Clock.SetUtcNow(Noon);
        _ramp = new InMemoryRampStore(_fx.Clock);
        _calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        for (var d = new DateOnly(2026, 10, 2); d < Today; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                _sessions.Add(d);
            }
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Gate(bool on) : ILiveWritesGate
    {
        public bool Enabled { get; } = on;
    }

    private sealed class Settings : ILiveRampSettings
    {
        public IReadOnlyList<double> StepFactors => RampPolicy.DefaultFactors;

        public RampThresholds Thresholds => RampThresholds.Defaults;
    }

    private sealed class Switches : ILiveSettingsStore
    {
        public LiveSettings Current { get; set; } = LiveSettings.Defaults;

        public ValueTask<LiveSettings> CurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Current);

        public ValueTask<IReadOnlyList<StoredLiveSettings>> HistoryAsync(int limit, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<StoredLiveSettings>>([]);

        public ValueTask<StoredLiveSettings?> AppendAsync(string actor, bool autoApproveTickets, bool autoRampUp, string reason, long? expectedCurrentId, CancellationToken cancellationToken) => ValueTask.FromResult<StoredLiveSettings?>(null);
    }

    private async Task<BrokerAccount> LiveAccountAsync(int step = 1, bool hold = false)
    {
        _account = await _fx.CreatedAsync(Inputs.Account(trading: Inputs.Trading(_fx.Standard.Id, mode: "manual_live", amount: 100_000m), roles: Inputs.Roles(true, false)));
        _ramp.Seed(_account.Id, RampEventKinds.SetStep, null, 1, false, StepSet);
        if (step == 2)
        {
            _ramp.Seed(_account.Id, RampEventKinds.SetStep, 1, 2, false, StepSet);
        }

        if (hold)
        {
            _ramp.Seed(_account.Id, RampEventKinds.Hold, step, step, true, StepSet);
        }

        return _account;
    }

    private RampAssessor Assessor() => new(_ramp, _capture, _executions, _halts, new LiveTicketSource(Substitute.For<IPlanStore>()), _calendar, _fx.Data, new Settings(), _fx.Clock);

    private EvaluateRampHandler Evaluator() =>
        new(_fx.Data, _fx.Data, _ramp, Assessor(), new LiveLossReader(_capture, _market, _fx.Clock), _halts, _switches, new Settings(), _calendar, _fx.Clock, NullLogger<EvaluateRampHandler>.Instance);

    private GetRampHandler Viewer() =>
        new(_fx.Data, _fx.Data, _ramp, new Settings(), new Gate(true), Assessor(), new LiveLossReader(_capture, _market, _fx.Clock), _halts, _switches, _fx.Clock);

    private OwnerRampActs Acts(bool gate = true) =>
        new(_fx.Data, _ramp, Viewer(), Assessor(), new NoExitReport(), new Gate(gate), _fx.Data, _fx.Data, _fx.Clock, NullLogger<OwnerRampActs>.Instance);

    private static NewCaptureRun Poll(Guid account, DateOnly date, int hour, int minute) =>
        new(account, OutcomeCodes.KindPoll, date, At(date, hour, minute), At(date, hour, minute), OutcomeCodes.RunOk, 0, 0, 0, 0);

    private static DateTimeOffset At(DateOnly date, int hour, int minute) => new(date.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromMinutes(330));

    private void HealthyDay(DateOnly date)
    {
        for (var m = 9 * 60 + 15; m <= 15 * 60 + 35; m += 20)
        {
            _capture.Runs.Add(Poll(_account.Id, date, m / 60, m % 60));
        }
    }

    private void FiveStarSession(DateOnly date, int index, double slip, double r, bool overAllocated = false)
    {
        HealthyDay(date);
        var outcome = new TicketOutcome(overAllocated ? OutcomeCodes.OverAllocation : OutcomeCodes.Filled, 10, 10, At(date, 9, 45), [], null, "graded", "present", OutcomeCodes.Protected, null);
        _capture.InsertOutcomesAsync([new NewTicketOutcome(1000 + index, _account.Id, date, 10, outcome)], Ct).AsTask().GetAwaiter().GetResult();
        var stored = _capture.Outcomes.Last();
        var exit = 100m + (decimal)r * 2m; // risk 2.00 per share
        _executions.InsertExecutionsAsync(
            [new NewLiveExecution(stored.Id, _account.Id, stored.ItemId, date, "AAA", "buy", "intraday", "INTRADAY", "L1", 600m, 0.01m, 0.05m, ReferenceKind.TicketLevel, 100m, 2m, 100m, 100m, 10, slip * 2, 0, slip * 2, 0, slip * 100, slip, 0, slip, 30, 100m, 104m, 98m, 0, exit, 10, null)], Ct).AsTask().GetAwaiter().GetResult();
    }

    private void AllGatesPass()
    {
        for (var i = 0; i < _sessions.Count; i++)
        {
            FiveStarSession(_sessions[i], i, i % 2 == 0 ? 0.02 + 0.11696 : 0.02 - 0.11696, 0.5);
        }
    }

    [Fact]
    public async Task A_loss_of_2_16_r_opens_one_daily_halt_and_a_second_run_opens_none()
    {
        await LiveAccountAsync();
        HealthyToday();
        _market.Last["BBB"] = 48.6m;
        AddDayFills();

        var first = await Evaluator().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: true), Ct);
        var second = await Evaluator().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: true), Ct);

        first.Value.HaltsOpened.ShouldBe(1);
        second.Value.HaltsOpened.ShouldBe(0);
        var halt = _halts.Rows.ShouldHaveSingleItem();
        halt.Kind.ShouldBe(HaltCodes.Daily);
        halt.Action.ShouldBe(HaltCodes.StopNew);
        halt.MeasuredR.ShouldNotBeNull().ShouldBe(2.16, 1e-9);
        halt.MeasureState.ShouldBe(MeasureStates.Value);
        _ramp.Rows.Count.ShouldBe(1); // HaltsOnly writes no ramp event
    }

    [Fact]
    public async Task A_loss_of_1_92_r_opens_no_halt()
    {
        await LiveAccountAsync();
        HealthyToday();
        _market.Last["BBB"] = 48.9m;
        AddDayFills();

        (await Evaluator().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: true), Ct)).Value.HaltsOpened.ShouldBe(0);
        var view = (await Viewer().HandleAsync(new GetRampQuery(_account.Id), Ct)).Value;
        view.Loss.ShouldNotBeNull().LossR.ShouldNotBeNull().ShouldBe(1.92, 1e-9);
    }

    [Fact]
    public async Task A_capture_gap_makes_the_loss_unavailable_and_opens_no_halt()
    {
        await LiveAccountAsync();
        _capture.Runs.Add(Poll(_account.Id, Today, 9, 20)); // nothing since: more than 30 minutes of silence by noon
        AddDayFills();

        (await Evaluator().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: true), Ct)).Value.HaltsOpened.ShouldBe(0);
        var loss = (await Viewer().HandleAsync(new GetRampQuery(_account.Id), Ct)).Value.Loss.ShouldNotBeNull();
        loss.State.ShouldBe(MeasureStates.Unavailable);
        loss.NetRupees.ShouldBeNull();
        loss.LossR.ShouldBeNull();
    }

    [Fact]
    public async Task All_gates_passing_with_the_switch_off_shows_eligible_and_writes_nothing()
    {
        await LiveAccountAsync();
        AllGatesPass();

        var result = await Evaluator().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: false), Ct);

        result.Value.AutoStepUps.ShouldBe(0);
        _ramp.Rows.Count.ShouldBe(1);
        var view = (await Viewer().HandleAsync(new GetRampQuery(_account.Id), Ct)).Value;
        view.Eligible.ShouldBeTrue();
        view.Gates.Where(g => g.Code != RampGate.CostDrag).ShouldAllBe(g => g.State == GateStates.Pass);
    }

    [Fact]
    public async Task With_the_switch_on_the_rule_steps_up_once_with_the_settings_snapshot_and_never_past_s2()
    {
        await LiveAccountAsync();
        AllGatesPass();
        _switches.Current = new LiveSettings(9, new string('b', 64), false, true);

        var first = await Evaluator().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: false), Ct);

        first.Value.AutoStepUps.ShouldBe(1);
        var up = _ramp.Rows[^1];
        up.Kind.ShouldBe(RampEventKinds.AutoStepUp);
        up.Actor.ShouldBe(RampEventKinds.RuleActor);
        up.ToStep.ShouldBe(2);
        up.GateHash.ShouldNotBeNull().Length.ShouldBe(64);
        (await Evaluator().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: false), Ct)).Value.AutoStepUps.ShouldBe(0); // S2: the rule stops
        _ramp.Rows.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Two_over_allocated_entries_write_one_hold_by_the_rule_and_the_owner_may_clear_it_after_the_cooldown()
    {
        await LiveAccountAsync();
        AllGatesPass();
        _capture.Outcomes.Clear();
        _executions.Executions.Clear();
        for (var i = 0; i < _sessions.Count; i++)
        {
            FiveStarSession(_sessions[i], i, 0.02, 0.5, overAllocated: i is 15 or 16); // inside the last 10 sessions, outside the last 5
        }

        var first = await Evaluator().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: false), Ct);
        var second = await Evaluator().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: false), Ct);

        first.Value.Holds.ShouldBe(1);
        second.Value.Holds.ShouldBe(0);
        var hold = _ramp.Rows[^1];
        hold.Kind.ShouldBe(RampEventKinds.Hold);
        hold.Actor.ShouldBe(RampEventKinds.RuleActor);
        var cleared = await Acts().HandleAsync(new ClearHoldCommand(Owner, _account.Id, "reviewed"), Ct);
        cleared.IsSuccess.ShouldBeTrue(cleared.IsFailure ? cleared.Error.Message : string.Empty);
        cleared.Value.Hold.ShouldBeFalse();
        _ramp.Rows[^1].Kind.ShouldBe(RampEventKinds.ClearHold);
    }

    [Fact]
    public async Task A_hold_cannot_be_cleared_while_a_fresh_event_is_inside_the_cooldown()
    {
        await LiveAccountAsync();
        for (var i = 0; i < _sessions.Count; i++)
        {
            FiveStarSession(_sessions[i], i, 0.02, 0.5, overAllocated: i >= _sessions.Count - 2);
        }

        await Evaluator().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: false), Ct);
        var refused = await Acts().HandleAsync(new ClearHoldCommand(Owner, _account.Id, "reviewed"), Ct);

        refused.Error.Code.ShouldBe(RampErrors.HoldConditionActiveCode);
        _ramp.Rows[^1].Kind.ShouldBe(RampEventKinds.Hold);
    }

    [Fact]
    public async Task A_drawdown_above_the_weekly_limit_steps_down_by_the_rule_and_at_s1_it_only_holds()
    {
        await LiveAccountAsync(step: 2);
        for (var i = 0; i < _sessions.Count; i++)
        {
            FiveStarSession(_sessions[i], i, 0.02, i < 6 ? -1.0 : 0.1); // six losers in a row: drawdown 6 R
        }

        (await Evaluator().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: false), Ct)).Value.StepDowns.ShouldBe(1);
        var down = _ramp.Rows[^1];
        down.Kind.ShouldBe(RampEventKinds.StepDown);
        down.ToStep.ShouldBe(1);
        down.Hold.ShouldBeFalse();
    }

    [Fact]
    public async Task At_s1_the_step_down_rule_holds_instead()
    {
        await LiveAccountAsync();
        for (var i = 0; i < _sessions.Count; i++)
        {
            FiveStarSession(_sessions[i], i, 0.02, i < 6 ? -1.0 : 0.1);
        }

        var result = await Evaluator().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: false), Ct);

        result.Value.StepDowns.ShouldBe(0);
        result.Value.Holds.ShouldBe(1);
        _ramp.Rows[^1].Kind.ShouldBe(RampEventKinds.Hold);
    }

    [Fact]
    public async Task The_exit_confirmation_is_refused_while_the_report_is_not_built_and_the_acts_need_the_gate()
    {
        await LiveAccountAsync();

        (await Acts().HandleAsync(new ConfirmExitCommand(Owner, _account.Id, "read it"), Ct)).Error.Code.ShouldBe(RampErrors.ExitNotCheckedCode);
        (await Acts(gate: false).HandleAsync(new ConfirmExitCommand(Owner, _account.Id, "read it"), Ct)).Error.Code.ShouldBe(LiveErrors.CapabilityOff);
        (await Acts(gate: false).HandleAsync(new ClearHoldCommand(Owner, _account.Id, "x"), Ct)).Error.Code.ShouldBe(LiveErrors.CapabilityOff);
        (await Acts().HandleAsync(new ClearHoldCommand("rule:ramp", _account.Id, "x"), Ct)).Error.Code.ShouldBe(LiveErrors.OwnerOnly);
        (await Acts().HandleAsync(new ClearHoldCommand(Owner, _account.Id, "x"), Ct)).Error.Code.ShouldBe(RampErrors.NoHoldCode);
        _ramp.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_owner_raise_is_refused_with_the_failing_gates_listed_and_allowed_when_all_pass()
    {
        await LiveAccountAsync();
        var set = new SetRampStepHandler(_fx.Data, _ramp, Viewer(), Assessor(), new Gate(true), _fx.Data, _fx.Data, _fx.Clock, NullLogger<SetRampStepHandler>.Instance);

        var refused = await set.HandleAsync(new SetRampStepCommand(Owner, _account.Id, 2, "up"), Ct);
        refused.Error.Code.ShouldBe(RampErrors.GatesNotMetCode);
        refused.Error.Message.ShouldContain("G2_volume insufficient");

        AllGatesPass();
        var ok = await set.HandleAsync(new SetRampStepCommand(Owner, _account.Id, 2, "evidence read"), Ct);
        ok.IsSuccess.ShouldBeTrue(ok.IsFailure ? ok.Error.Message : string.Empty);
        _ramp.Rows[^1].GateHash.ShouldNotBeNull().Length.ShouldBe(64);
        _ramp.Rows[^1].Actor.ShouldBe(Owner);
    }

    [Fact]
    public async Task The_live_ready_facts_read_the_halt_the_capture_and_the_loss_of_the_last_session()
    {
        await LiveAccountAsync();
        HealthyDay(new DateOnly(2026, 11, 2));
        var source = new LiveReadyFactsSource(_halts, _ramp, new LiveLossReader(_capture, _market, _fx.Clock), _calendar, _fx.Clock);

        var healthy = await source.ForAsync(_account.Id, Today, true, Ct);
        healthy.Facts.ShouldBe(new XDrishti.Domain.Checklist.LiveReadyFacts(true, true, true, true));
        healthy.ScaleFactor.ShouldBe(1.0);

        await _halts.OpenAsync(new NewHalt(_account.Id, HaltCodes.Daily, Today, 2m, 1.5m, 2.2, 0.3, MeasureStates.Value, HaltCodes.StopNew), Ct);
        (await source.ForAsync(_account.Id, Today, true, Ct)).Facts.NoOpenHalt.ShouldBe(false);

        var gap = new LiveReadyFactsSource(_halts, _ramp, new LiveLossReader(new InMemoryLiveStore(), _market, _fx.Clock), _calendar, _fx.Clock);
        var noCapture = await gap.ForAsync(_account.Id, Today, true, Ct);
        noCapture.Facts.CaptureHealthy.ShouldBe(false);
        noCapture.Facts.LossMeasurable.ShouldBe(false); // never "no loss"

        (await source.ForAsync(_account.Id, Today, false, Ct)).Facts.StepSet.ShouldBeFalse();
    }

    [Fact]
    public async Task A_weekly_halve_halves_the_scale_of_the_next_week_and_a_stop_removes_it()
    {
        await LiveAccountAsync();
        HealthyDay(new DateOnly(2026, 11, 2));
        var source = new LiveReadyFactsSource(_halts, _ramp, new LiveLossReader(_capture, _market, _fx.Clock), _calendar, _fx.Clock);
        var monday = new DateOnly(2026, 10, 26);
        await _halts.OpenAsync(new NewHalt(_account.Id, HaltCodes.Weekly, monday, 5m, 0m, 5.6, null, MeasureStates.Value, HaltCodes.Halve), Ct);

        (await source.ForAsync(_account.Id, new DateOnly(2026, 11, 3), true, Ct)).ScaleFactor.ShouldBe(0.5);
        (await source.ForAsync(_account.Id, new DateOnly(2026, 10, 29), true, Ct)).ScaleFactor.ShouldBe(1.0); // the halve is for the next week

        await _halts.OpenAsync(new NewHalt(_account.Id, HaltCodes.Weekly, new DateOnly(2026, 11, 2), 5m, 0m, 5.6, null, MeasureStates.Value, HaltCodes.StopNew), Ct);
        (await source.ForAsync(_account.Id, new DateOnly(2026, 11, 4), true, Ct)).ScaleFactor.ShouldBe(0.0);
    }

    private void HealthyToday()
    {
        for (var m = 9 * 60 + 15; m <= 11 * 60 + 55; m += 20)
        {
            _capture.Runs.Add(Poll(_account.Id, Today, m / 60, m % 60));
        }
    }

    private void AddDayFills()
    {
        LiveFill F(string symbol, Side side, long qty, decimal price, int minute) =>
            new(Guid.NewGuid(), _account.Id, "T" + minute + symbol, null, symbol, side, "INTRADAY", qty, price, Today, new TimeOnly(10, minute));
        _capture.InsertFillsAsync([F("AAA", Side.Buy, 100, 100m, 1), F("AAA", Side.Sell, 100, 98.7m, 20), F("BBB", Side.Buy, 100, 50m, 5)], "trade_book", Ct).AsTask().GetAwaiter().GetResult();
    }
}
