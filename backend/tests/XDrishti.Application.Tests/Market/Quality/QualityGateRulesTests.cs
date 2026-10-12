using XDrishti.Application.Market.Quality;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Quality;

public sealed class QualityGateRulesTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 12, 10, 20, 0, TimeSpan.FromMinutes(330));

    private static readonly TradingCalendar Calendar = new();

    private static QualityGateView Gate(QualityCounts counts, QualityRunView? run, double max, DateTimeOffset? now = null, TradingCalendar? calendar = null) =>
        QualityGateRules.Evaluate(counts, run, max, calendar ?? Calendar, now ?? At.AddHours(2));

    private static QualityRunView Run(string status = "succeeded", string? error = null) =>
        new(Guid.NewGuid(), "daily", status, At, At.AddSeconds(8), 5, 100, 0, 0, 0, null, error, null, null);

    private static QualityCounts Counts(int checkedCount, int failed, int warned = 0) =>
        new(checkedCount, checkedCount, checkedCount - failed - warned, warned, failed, 0, 90);

    [Fact]
    public void Before_the_first_run_the_gate_is_blocked_fail_closed()
    {
        var gate = Gate(new QualityCounts(0, 0, 0, 0, 0, 0, null), null, 5);

        (gate.Ok, gate.Blocked).ShouldBe((false, true));
        gate.Reasons.ShouldBe(["There is no completed data quality run over every instrument yet."]);
    }

    [Fact]
    public void A_run_without_failures_is_ok_and_not_blocked()
    {
        var gate = Gate(Counts(100, 0, warned: 4), Run(), 5);

        (gate.Ok, gate.Blocked, gate.FailedInstruments, gate.FailedPercent).ShouldBe((true, false, 0, 0));
        gate.Reasons.ShouldBeEmpty();
        gate.LastRunAt.ShouldBe(At.AddSeconds(8));
    }

    [Fact]
    public void A_failed_last_run_blocks_with_its_error()
    {
        var gate = Gate(Counts(100, 0), Run("failed", "The quality run failed unexpectedly."), 5);

        (gate.Ok, gate.Blocked).ShouldBe((false, true));
        gate.Reasons.ShouldBe(["The last data quality run failed: The quality run failed unexpectedly."]);
    }

    [Fact]
    public void More_failures_than_the_limit_block_and_exactly_the_limit_does_not()
    {
        var over = Gate(Counts(100, 6), Run(), 5);
        var exactly = Gate(Counts(100, 5), Run(), 5);

        (over.Blocked, over.Ok, over.FailedPercent).ShouldBe((true, false, 6));
        over.Reasons.ShouldHaveSingleItem().ShouldBe("6 of 100 instruments fail the checks (6 %), more than the 5 % allowed.");
        (exactly.Blocked, exactly.Ok).ShouldBe((false, false));
        exactly.Reasons.ShouldHaveSingleItem().ShouldContain("they are skipped");
    }

    [Fact]
    public void A_limit_of_zero_blocks_on_the_first_failure()
    {
        Gate(Counts(214, 1), Run(), 0).Blocked.ShouldBeTrue();
        Gate(Counts(214, 0), Run(), 0).Blocked.ShouldBeFalse();
    }

    [Fact]
    public void A_run_that_is_still_running_is_no_verdict_so_with_no_finished_run_the_gate_stays_blocked()
    {
        var gate = Gate(Counts(100, 0), Run("running"), 5);

        (gate.Ok, gate.Blocked).ShouldBe((false, true));
        gate.Reasons.ShouldBe(["There is no completed data quality run over every instrument yet."]);
    }

    [Fact]
    public void A_finished_failed_run_keeps_the_gate_blocked_while_the_next_run_is_underway()
    {
        // the reader passes the latest FINISHED run: the running one in front of it is not the verdict
        var gate = Gate(Counts(100, 0), Run("failed", "boom"), 5);

        gate.Blocked.ShouldBeTrue();
        gate.Reasons.ShouldBe(["The last data quality run failed: boom"]);
    }

    [Theory]
    [InlineData(2026, 10, 12, 14, false)] // the same evening
    [InlineData(2026, 10, 15, 16, false)] // Thursday after the close: Tue, Wed, Thu = 3 trading days after Monday
    [InlineData(2026, 10, 16, 16, true)]  // Friday after the close: 4 trading days
    public void A_finished_run_older_than_three_trading_days_is_stale_and_blocks(int year, int month, int day, int hour, bool stale)
    {
        var now = new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.FromMinutes(330));

        var gate = Gate(Counts(100, 0), Run(), 5, now);

        gate.Blocked.ShouldBe(stale);
        gate.Ok.ShouldBe(!stale);
        if (stale)
        {
            gate.Reasons.ShouldHaveSingleItem().ShouldBe("The last completed data quality run is 4 trading days old (more than 3); run the checks again.");
        }
    }

    [Fact]
    public void Holidays_and_weekends_do_not_age_a_run()
    {
        // Monday's run; Tue to Thu are holidays and the weekend passes: on Monday 19th only 1 trading day (Fri 16th) lies between
        var holidays = new TradingCalendar([Holiday(13), Holiday(14), Holiday(15)]);
        var now = new DateTimeOffset(2026, 10, 19, 16, 0, 0, TimeSpan.FromMinutes(330));

        var gate = Gate(Counts(100, 0), Run(), 5, now, holidays);

        gate.Blocked.ShouldBeFalse();
    }

    private static TradingDay Holiday(int day) =>
        TradingDay.Holiday(new DateOnly(2026, 10, day), CalendarSource.Manual, CalendarStatus.Confirmed, "holiday", At);

    [Fact]
    public void Both_reasons_are_given_when_the_run_failed_and_many_instruments_fail()
    {
        var gate = Gate(Counts(10, 5), Run("failed"), 5);

        gate.Blocked.ShouldBeTrue();
        gate.Reasons.Count.ShouldBe(2);
    }
}
