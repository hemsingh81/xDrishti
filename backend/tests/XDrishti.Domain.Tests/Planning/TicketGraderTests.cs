using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>Spec P7-13 criteria 2, 3 and 6: the simulator's record of one session becomes a grade; only a trade entered on the planned session in the ticket's side counts; pending is not a grade; nothing is guessed.</summary>
public sealed class TicketGraderTests
{
    private static readonly DateOnly F = new(2026, 3, 3);

    private static readonly DateOnly D = new(2026, 3, 2);

    private static readonly CostRecord Sheet = new("test-a", new string('a', 64), "{}", null, 100_000m, SizingProfile.Standard, 1m);

    private static SimTrade Trade(DateOnly date, Side side, double rFilled, double rNet, bool costed, ExitReason reason = ExitReason.Target)
    {
        var entry = new DateTimeOffset(date.Year, date.Month, date.Day, 9, 45, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();
        var costing = costed ? new TradeCosting(CostSegment.IntradayEquity, Sheet.SheetId, 100, 1000m, 10000m, 10000m, LegCharges.Zero, LegCharges.Zero, 100m, 0.1, rNet) : null;
        return new SimTrade(
            SimTrade.CurrentSchemaVersion, "ORB", Guid.Empty, "X", side, date, entry, Timeframe.M15, new Bar(entry - 900, 99, 100.5, 98, 100, 1_000), 15, true,
            entry, 100.5, 98, 105.5, entry + 3600, 105.5, reason, rFilled, 0.4, 2.2, FillBasis.Exact, costed ? CostBasis.Charged : CostBasis.None, null,
            rFilled, 0, 100.5, 105.5, EntryFill.AtLimit, false, 1, 0, costing);
    }

    private static SimulationRun Run(IReadOnlyList<SimTrade> trades, IReadOnlyList<SimTrade>? open = null, bool costed = false, DateOnly? last = null, SkipReason? skip = null, RunFates? fates = null)
    {
        var strategy = new StrategyRecord("ORB", new Dictionary<string, double>(), "{}", new string('1', 64), Timeframe.M15, [Side.Buy]);
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(Guid.Empty, "X")]);
        var header = new RunHeader(strategy, "sim-6", F, F, ReplayOptions.Default, false, "{}", universe, new TradingCalendar([]), costed ? Sheet : null);
        var record = new InstrumentRun(Guid.Empty, "X", skip, null, F.AddDays(-30), last ?? F, F.AddDays(-30), new DateSpan(F.AddDays(-30), F), 0, 0, 0, 0, null, 0.05m, false, [], SessionCounts.Empty, [], false, [], [], WalkCounters.Empty, fates ?? RunFates.Empty, 0, 0);
        return RunAssembler.Assemble(header, [new InstrumentReplay(record, trades, open ?? [])]);
    }

    private static GraderResult G(SimulationRun main, SimulationRun? tail = null, bool daily = false) => TicketGrader.Grade(main, tail, Side.Buy, F, D, daily);

    [Fact]
    public void A_gross_trade_on_the_planned_session_is_graded_at_the_filled_r_and_labelled()
    {
        var run = Run([Trade(F, Side.Buy, 1.8, 0, costed: false)]);

        var result = G(run);

        result.IsPending.ShouldBeFalse();
        result.Grade!.Status.ShouldBe(GradeStatus.Graded);
        result.Grade.R.ShouldBe(1.8);
        result.Grade.CostBasis.ShouldBe("gross, no charges");
        result.Grade.ExitReason.ShouldBe("Target");
        TicketGrade.Basis.ShouldBe("simulated on plan prices");
    }

    [Fact]
    public void A_charged_trade_is_graded_at_the_net_r_and_names_the_sheet()
    {
        var run = Run([Trade(F, Side.Buy, 2.0, 1.9, costed: true)], costed: true);

        var grade = G(run).Grade!;

        grade.R.ShouldBe(1.9);
        grade.CostBasis.ShouldBe("net of the charge sheet test-a");
    }

    [Fact]
    public void A_trade_of_another_date_or_the_other_side_is_never_graded()
    {
        var run = Run([Trade(F.AddDays(-1), Side.Buy, 5, 0, false), Trade(F, Side.Sell, 5, 0, false)]);

        var grade = G(run).Grade!;

        grade.Status.ShouldBe(GradeStatus.NotTriggered);
        grade.R.ShouldBeNull();
    }

    [Fact]
    public void When_two_trades_qualify_the_first_by_entry_instant_is_taken()
    {
        var later = Trade(F, Side.Buy, 9, 0, false) with { EntryEpochSeconds = Trade(F, Side.Buy, 0, 0, false).EntryEpochSeconds + 600 };
        var run = Run([later, Trade(F, Side.Buy, 1, 0, false)]);

        G(run).Grade!.R.ShouldBe(1);
    }

    [Fact]
    public void No_trade_is_not_triggered_with_the_reason_the_replay_gives()
    {
        G(Run([])).Grade!.Detail.ShouldContain("did not hold");
        G(Run([], fates: RunFates.Empty with { LimitNotReached = 1 })).Grade!.Detail.ShouldContain("limit was not reached");
    }

    [Fact]
    public void A_trade_dropped_by_sizing_or_cost_is_not_gradable_not_not_triggered()
    {
        var grade = G(Run([], fates: RunFates.Empty with { SkippedCost = 1 })).Grade!;

        grade.Status.ShouldBe(GradeStatus.NotGradable);
    }

    [Fact]
    public void A_position_still_open_or_bars_that_do_not_reach_the_session_are_pending()
    {
        G(Run([], [Trade(F, Side.Buy, 0, 0, false)])).IsPending.ShouldBeTrue();
        G(Run([], last: F.AddDays(-1))).IsPending.ShouldBeTrue();
        G(Run([], skip: SkipReason.NoBars)).IsPending.ShouldBeTrue();
    }

    [Fact]
    public void An_instrument_the_replay_skipped_is_pending_with_the_code_because_it_may_be_mended()
    {
        var result = G(Run([], skip: SkipReason.TickSizeInvalid));

        result.IsPending.ShouldBeTrue();
        result.PendingReason!.ShouldContain("tick_size_invalid");
    }

    [Fact]
    public void A_flagged_day_or_a_pending_order_is_pending_not_final()
    {
        G(Run([], fates: RunFates.Empty with { EntriesSuppressed = 1 })).IsPending.ShouldBeTrue();
        G(Run([], fates: RunFates.Empty with { OrderPendingAtEnd = 1 })).IsPending.ShouldBeTrue();
    }

    private static long CloseOf(DateOnly date) => new DateTimeOffset(date.Year, date.Month, date.Day, 15, 30, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();

    [Fact]
    public void A_daily_trigger_decided_at_the_close_of_D_and_filled_on_F_is_graded()
    {
        var trade = Trade(F, Side.Buy, 1.7, 0, false) with { DecisionEpochSeconds = CloseOf(D), TriggerTimeframe = Timeframe.D1 };

        var grade = G(Run([trade]), daily: true).Grade!;

        grade.Status.ShouldBe(GradeStatus.Graded);
        grade.R.ShouldBe(1.7);
    }

    [Fact]
    public void An_intraday_trigger_decided_on_D_is_never_graded_even_when_it_fills_on_F()
    {
        var trade = Trade(F, Side.Buy, 1.7, 0, false) with { DecisionEpochSeconds = CloseOf(D) - 3600, TriggerTimeframe = Timeframe.M15 };

        G(Run([trade])).Grade!.Status.ShouldBe(GradeStatus.NotTriggered);
    }

    [Fact]
    public void A_daily_decision_at_the_close_of_F_belongs_to_the_ticket_of_the_next_session_not_to_this_one()
    {
        var next = F.AddDays(1);
        var trade = Trade(next, Side.Buy, 0.9, 0, false) with { DecisionEpochSeconds = CloseOf(F), TriggerTimeframe = Timeframe.D1 };

        var grade = G(Run([trade]), daily: true).Grade!;

        grade.Status.ShouldBe(GradeStatus.NotTriggered);
        grade.Detail.ShouldContain("close of 2026-03-02");
    }

    [Fact]
    public void An_intraday_trade_decided_on_F_does_not_count_for_a_daily_ticket()
    {
        G(Run([Trade(F, Side.Buy, 1.7, 0, false)]), daily: true).Grade!.Status.ShouldBe(GradeStatus.NotTriggered);
    }

    [Fact]
    public void A_dropped_trade_of_another_day_does_not_make_this_ticket_not_gradable()
    {
        // the main run (from D) dropped trades by the cost rule; the tail run (from F) shows the same drops, so they were decided after D
        var main = Run([], fates: RunFates.Empty with { SkippedCost = 2, PositionOpen = 1 });
        var tail = Run([], fates: RunFates.Empty with { SkippedCost = 2, PositionOpen = 1 });

        var grade = G(main, tail, daily: true).Grade!;

        grade.Status.ShouldBe(GradeStatus.NotTriggered);
        grade.Detail.ShouldNotContain("earlier position");
    }

    [Fact]
    public void A_dropped_trade_of_the_decision_day_alone_is_not_gradable()
    {
        var main = Run([], fates: RunFates.Empty with { SkippedCost = 1 });
        var tail = Run([], fates: RunFates.Empty);

        G(main, tail, daily: true).Grade!.Status.ShouldBe(GradeStatus.NotGradable);
    }

    [Fact]
    public void A_flagged_day_or_a_pending_order_after_the_decision_day_does_not_leave_the_ticket_pending()
    {
        var main = Run([], fates: RunFates.Empty with { EntriesSuppressed = 1, OrderPendingAtEnd = 1, ExcludedAcrossDiscontinuity = 1 });
        var tail = Run([], fates: RunFates.Empty with { EntriesSuppressed = 1, OrderPendingAtEnd = 1, ExcludedAcrossDiscontinuity = 1 });

        G(main, tail, daily: true).IsPending.ShouldBeFalse();
    }

    [Fact]
    public void A_not_triggered_reason_of_a_later_day_is_not_this_tickets_reason()
    {
        var main = Run([], fates: RunFates.Empty with { LimitNotReached = 1 });
        var tail = Run([], fates: RunFates.Empty with { LimitNotReached = 1 });

        G(main, tail, daily: true).Grade!.Detail.ShouldNotContain("limit");
        G(main, null, daily: true).Grade!.Detail.ShouldContain("limit");
    }

    [Fact]
    public void The_same_record_gives_the_same_grade()
    {
        var run = Run([Trade(F, Side.Buy, 1.25, 0, false)]);

        G(run).ShouldBe(G(run));
    }
}
