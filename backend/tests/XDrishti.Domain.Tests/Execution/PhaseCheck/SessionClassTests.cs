using XDrishti.Domain.Execution.PhaseCheck;
using static XDrishti.Domain.Tests.Execution.PhaseCheck.PhaseBuilders;

namespace XDrishti.Domain.Tests.Execution.PhaseCheck;

/// <summary>Spec P12-12 criterion 1: one case per clause, plus the precedence.</summary>
public sealed class SessionClassTests
{
    private static SessionClass Class(SessionFacts f) => SessionClassifier.Classify(f).Class;

    private static readonly DateOnly D = Start;

    [Fact]
    public void A_clean_confirmed_day_with_both_runs_is_counted() => Class(Counted(D)).ShouldBe(SessionClass.Counted);

    [Fact]
    public void A_quiet_day_with_no_entry_and_no_carried_position_is_idle() => Class(Idle(D)).ShouldBe(SessionClass.Idle);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void Not_in_mode_at_open_a_change_in_the_session_or_a_canary_day_is_out_of_mode_and_never_broken(bool inMode, bool changed, bool canary)
    {
        var f = Counted(D) with { InModeAtOpen = inMode, ModeChangedInSession = changed, CanaryPending = canary, BreakKinds = ["trade_extra"] };

        Class(f).ShouldBe(SessionClass.OutOfMode);
    }

    [Fact]
    public void A_resolved_break_still_breaks_the_day() => Class(Broken(D)).ShouldBe(SessionClass.Broken);

    [Theory]
    [InlineData("eod_missing")]
    [InlineData("final_missing")]
    [InlineData("eod_unreadable")]
    [InlineData("final_unreadable")]
    public void A_missing_or_unreadable_run_on_a_day_with_an_entry_is_broken(string reason)
    {
        var f = reason.StartsWith("eod", StringComparison.Ordinal)
            ? Counted(D) with { Eod = reason.EndsWith("missing", StringComparison.Ordinal) ? null : new RunFact(false) }
            : Counted(D) with { Final = reason.EndsWith("missing", StringComparison.Ordinal) ? null : new RunFact(false) };

        SessionClassifier.Classify(f).Reasons.ShouldContain(reason);
        Class(f).ShouldBe(SessionClass.Broken);
    }

    [Fact]
    public void A_carried_position_with_an_unreadable_final_run_is_broken_even_on_a_not_ready_day() =>
        Class(Idle(D) with { PositionCarriedIn = true, DayState = "not_ready", Final = new RunFact(false), Eod = new RunFact(true) }).ShouldBe(SessionClass.Broken);

    [Theory]
    [InlineData("protection_breach")]
    [InlineData("flatten_started")]
    [InlineData("oversold")]
    public void A_breaking_alarm_breaks_the_day(string alarm) => Class(Counted(D) with { Alarms = [alarm] }).ShouldBe(SessionClass.Broken);

    [Theory]
    [InlineData("reconciliation_break")]
    [InlineData("protection_breach")]
    [InlineData("oversold")]
    public void A_breaking_halt_breaks_the_day(string halt) => Class(Counted(D) with { Halts = [halt] }).ShouldBe(SessionClass.Broken);

    [Fact]
    public void A_harmless_alarm_and_halt_do_not_break_the_day() => Class(Counted(D) with { Alarms = ["stop_replaced"], Halts = ["owner_button"] }).ShouldBe(SessionClass.Counted);

    [Fact]
    public void A_missing_run_on_a_day_with_nothing_happening_does_not_break_it() => Class(Idle(D)).ShouldBe(SessionClass.Idle);

    [Fact]
    public void An_unavailable_day_state_does_not_stop_a_clean_confirmed_day_from_counting() => Class(Counted(D) with { DayState = null }).ShouldBe(SessionClass.Counted);

    [Fact]
    public void An_entry_without_a_confirmed_ticket_is_broken_with_its_reason()
    {
        var v = SessionClassifier.Classify(Counted(D) with { UnconfirmedEntries = 1 });

        v.Class.ShouldBe(SessionClass.Broken);
        v.Reasons.ShouldContain("unconfirmed_entry");
    }

    [Fact]
    public void Broken_wins_over_counted_and_out_of_mode_wins_over_broken()
    {
        Class(Counted(D) with { BreakKinds = ["trade_missing"], Alarms = ["oversold"] }).ShouldBe(SessionClass.Broken);
        Class(Counted(D) with { BreakKinds = ["trade_missing"], InModeAtOpen = false }).ShouldBe(SessionClass.OutOfMode);
    }

    [Fact]
    public void Sandbox_activity_alone_neither_counts_nor_breaks()
    {
        var f = Idle(D) with { SandboxIntents = 3 };

        Class(f).ShouldBe(SessionClass.Idle);
    }
}
