using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Execution;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Execution;

/// <summary>Spec P12-10 criteria 3, 10, 11 and 18 at the use-case level, on fakes: the notice rules, the blotter's slippage and vocabulary, the filters' 422 and the status poll interval.</summary>
public sealed class ExecutionScreensTests
{
    private static readonly Guid Account = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Monday 2026-10-12 at the given India time.
    private static DateTimeOffset Ist(int h, int m) => new(2026, 10, 12, h, m, 0, TimeSpan.FromMinutes(330));

    private static ExecutionNoticeInput Input(DateTimeOffset now, NoticeRaw? raw = null, IReadOnlyList<ExecutionHaltView>? halts = null, IReadOnlyList<ExecutionAlarmView>? alarms = null,
        IReadOnlyList<ExecutionModeAccountView>? modes = null, IReadOnlyList<OverviewAccount>? accounts = null, IReadOnlyList<ExecutionReadinessAccount>? readiness = null, IReadOnlyList<ExecutionBreakView>? breaks = null) =>
        new(now, true, raw ?? new NoticeRaw([], [], []), alarms ?? [], halts ?? [], breaks ?? [], modes ?? [], accounts ?? [], readiness ?? []);

    private static ExecutionModeAccountView Mode(string mode) => new(Account, "Main", mode, mode, null, null, null, []);

    private static OverviewAccount WithCaps(ExecMetric orders, ExecMetric loss) =>
        new(Account, "Main", "assisted", null, null, null, new ExecutionGateView(false, "x"), new OverviewReadiness("ready", null, null), new OverviewCaps(orders, ExecMetric.Of(0, 100), loss, ExecMetric.Of(0, 3), 1, null), 0, [], [], 0);

    private sealed class TickMarket : ILiveExecutionMarket
    {
        public ValueTask<decimal?> TickAsync(string symbol, CancellationToken cancellationToken) => ValueTask.FromResult<decimal?>(0.05m);

        public ValueTask<decimal?> AdvTurnoverCrAsync(string symbol, DateOnly date, CancellationToken cancellationToken) => ValueTask.FromResult<decimal?>(null);

        public ValueTask<decimal?> MinuteOpenAsync(string symbol, DateTimeOffset at, CancellationToken cancellationToken) => ValueTask.FromResult<decimal?>(null);

        public ValueTask<(decimal? Low, decimal? High)> MinuteExtremesAsync(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) => ValueTask.FromResult<(decimal?, decimal?)>((null, null));

        public ValueTask<decimal?> LastPriceAsync(string symbol, DateOnly date, DateTimeOffset notAfter, CancellationToken cancellationToken) => ValueTask.FromResult<decimal?>(null);
    }

    private sealed class FixedCalendar : ITradingCalendar
    {
        public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new TradingCalendar());

        public void Invalidate()
        {
        }
    }

    [Fact]
    public void An_open_ticket_is_a_warning_with_the_cause_in_its_id_and_a_decided_one_is_kept_as_resolved()
    {
        var now = Ist(10, 0);
        var open = new NoticeTicketRaw(Guid.NewGuid(), Account, "BUY 10 SBIN limit 800.00", now.AddSeconds(-10), now.AddSeconds(80), null);
        var done = new NoticeTicketRaw(Guid.NewGuid(), Account, "BUY 5 INFY limit 1500.00", now.AddMinutes(-5), now.AddMinutes(-3), "skipped");

        var items = ExecutionNoticeDeriver.Derive(Input(now, new NoticeRaw([open, done], [], [])));

        items[0].Id.ShouldBe("exec:ticket:" + open.Id.ToString("N"));
        items[0].Resolved.ShouldBeFalse();
        items[0].Severity.ShouldBe("warning");
        items[0].Text.ShouldContain("Expires 10:01:20 IST");
        items[1].Resolved.ShouldBeTrue();
        items.All(i => i.Actions.Count <= 2).ShouldBeTrue();
    }

    [Fact]
    public void An_automatic_action_is_born_resolved_and_info()
    {
        var now = Ist(10, 0);

        var item = ExecutionNoticeDeriver.Derive(Input(now, new NoticeRaw([], [new NoticeActionRaw(7, Account, "auto_entry", "Placed BUY 1", now.AddMinutes(-1))], []))).Single();

        item.Id.ShouldBe("exec:action:7");
        item.Kind.ShouldBe("exec_auto_action");
        item.Resolved.ShouldBeTrue();
        item.Severity.ShouldBe("info");
    }

    [Fact]
    public void A_halt_by_the_owner_is_the_kill_switch_and_an_ended_one_is_resolved_for_24_hours_then_dropped()
    {
        var now = Ist(10, 0);
        var owner = new ExecutionHaltView(1, now.AddHours(-1), "system", null, "entries", "owner_button", "owner", "owner:x", "stop", null);
        var system = new ExecutionHaltView(2, now.AddHours(-30), "system", null, "cancel", "daily_loss", "system", "system:x", "loss", new ExecutionHaltEnd(now.AddHours(-2), "owner:x", "owner", "ok"));
        var old = new ExecutionHaltView(3, now.AddHours(-60), "system", null, "cancel", "daily_loss", "system", "system:x", "loss", new ExecutionHaltEnd(now.AddHours(-30), "owner:x", "owner", "ok"));

        var items = ExecutionNoticeDeriver.Derive(Input(now, halts: [owner, system, old]));

        items.Select(i => (i.Id, i.Kind, i.Resolved)).ShouldBe([("exec:halt:1", "exec_kill_switch", false), ("exec:halt:2", "exec_halted", true)]);
    }

    [Theory]
    [InlineData(9, 0, "warning")]
    [InlineData(9, 5, "error")]
    public void A_not_ready_account_is_a_warning_until_0905_India_time_and_an_error_after(int h, int m, string severity)
    {
        var items = ExecutionNoticeDeriver.Derive(Input(Ist(h, m), modes: [Mode("assisted")]));

        items.Single(i => i.Kind == "exec_not_ready").Severity.ShouldBe(severity);
    }

    [Fact]
    public void A_cap_is_near_at_80_percent_and_not_below_and_the_day_summary_comes_at_1545()
    {
        var near = WithCaps(ExecMetric.Of(5, 6), ExecMetric.Of(1000, 1500));
        var far = WithCaps(ExecMetric.Of(4, 6), ExecMetric.Unavailable("no_day_mark"));

        var atNear = ExecutionNoticeDeriver.Derive(Input(Ist(15, 44), accounts: [near]));
        var atFar = ExecutionNoticeDeriver.Derive(Input(Ist(15, 46), accounts: [far]));

        atNear.Where(i => i.Kind == "exec_cap_near").Count().ShouldBe(1);
        atNear.ShouldNotContain(i => i.Kind == "exec_day_summary");
        atFar.ShouldNotContain(i => i.Kind == "exec_cap_near");
        atFar.Single(i => i.Kind == "exec_day_summary").Id.ShouldBe("exec:day:2026-10-12:summary");
    }

    [Fact]
    public async Task The_blotter_computes_slippage_in_ticks_and_percent_passes_an_unknown_state_and_rejects_a_bad_filter()
    {
        var reader = Substitute.For<IExecutionScreenReader>();
        var market = new TickMarket();
        var at = Ist(10, 0);
        var fills = new[] { new BlotterFillRaw(at, 100, 400.10m) };
        reader.BlotterAsync(Arg.Any<DateOnly>(), Arg.Any<Guid?>(), Arg.Any<IReadOnlyList<string>?>(), 0, 50, Arg.Any<CancellationToken>()).Returns(
        [
            new BlotterRaw(Guid.NewGuid(), Account, "Main", at, "TATA", "buy", 100, 400m, "quantum", 100, "XD1", "B1", null, null, true, null, [], fills),
        ]);
        var handler = new GetExecutionBlotterHandler(reader, market, new FakeTimeProvider(at), NullLogger<GetExecutionBlotterHandler>.Instance);

        var rows = (await handler.HandleAsync(new GetExecutionBlotterQuery(null, null, null, 1), Ct)).Value;
        var bad = await handler.HandleAsync(new GetExecutionBlotterQuery(null, null, "sideways", 1), Ct);

        rows[0].State.ShouldBe("quantum");
        rows[0].StateKnown.ShouldBeFalse();
        rows[0].By.ShouldBe("owner");
        rows[0].SlippageTicks.Value.ShouldBe(2m);
        rows[0].SlippagePercent.Value.ShouldBe(0.025m);
        bad.IsFailure.ShouldBeTrue();
        bad.Error.Code.ShouldBe("execution.invalid");
    }

    [Theory]
    [InlineData(0, 0, true, "value", 0)]
    [InlineData(2, 1, false, "unavailable", 0)]
    public void The_worst_case_is_value_zero_with_no_fills_and_unavailable_when_the_day_mark_is_missing(int filled, int risk, bool withMark, string state, int expected)
    {
        var raw = new DayAccountRaw(Account, 0, 0m, 0, 0, filled, filled, risk, false, withMark || filled == 0 ? 0m : null);

        var metric = GetExecutionOverviewHandler.WorstCase(filled == 0 ? null : raw with { Realised = null }, 1500m, null);

        metric.State.ShouldBe(state);
        (metric.Value ?? 0m).ShouldBe(expected);
    }

    [Theory]
    [InlineData(false, 10, 60)]
    [InlineData(true, 10, 5)]
    [InlineData(true, 17, 60)]
    public async Task The_poll_interval_is_5_seconds_only_while_something_is_automated_and_the_market_is_open(bool automated, int hour, int expected)
    {
        var facts = Substitute.For<IExecutionFactsReader>();
        facts.ReadAsync(Arg.Any<CancellationToken>()).Returns(new ExecutionFacts(null, null, null, null, 0, automated ? [(Account, "auto_live")] : [], false, automated));
        var calendar = new FixedCalendar();
        var handler = new GetExecutionStatusHandler(facts, calendar, new FakeTimeProvider(Ist(hour, 0)));

        var view = await handler.HandleAsync(new GetExecutionStatusQuery(), Ct);

        view.PollSeconds.ShouldBe(expected);
        view.AnyAutomated.ShouldBe(automated);
        view.MarketOpen.ShouldBe(hour == 10);
    }
}
