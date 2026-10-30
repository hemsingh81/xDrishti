using XDrishti.Domain.Execution;

namespace XDrishti.Domain.Tests.Execution;

/// <summary>P12-11 criteria 7 and 10 as pure rules over a week with a holiday: the evening and morning reminders, the warning at 08:45 and the error at 09:05, once per day by id, gone at ready, nothing for an account below assisted, and the re-check notice.</summary>
public sealed class ReadinessNoticeTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    // The week of 2026-10-26 (Monday) with Tuesday the 27th a holiday: sessions on Mon, Wed, Thu, Fri; next after Friday is Monday the 2nd.
    private static readonly DateOnly[] Sessions = [new(2026, 10, 26), new(2026, 10, 28), new(2026, 10, 29), new(2026, 10, 30), new(2026, 11, 2)];

    private static DateTimeOffset At(int month, int day, int hour, int minute) => new(2026, month, day, hour, minute, 0, Ist);

    private static IReadOnlyList<ReadinessNotice> Derive(DateTimeOffset now, bool ready = false, bool covers = false, bool assisted = true)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        var next = Sessions.First(s => s > today);
        return ReadinessNotices.Derive(now, new(today, Sessions.Contains(today), next, assisted, ready, covers));
    }

    [Fact]
    public void The_evening_before_a_trading_day_reminds_at_19_00_when_the_token_will_not_cover_it()
    {
        Derive(At(10, 26, 18, 59)).ShouldBeEmpty();

        var notice = Derive(At(10, 26, 19, 0)).Single();

        (notice.Kind, notice.Severity, notice.Date, notice.Id).ShouldBe(("exec_login_reminder", "info", new DateOnly(2026, 10, 28), "exec_login_reminder:2026-10-28:eve"));
        Derive(At(10, 26, 19, 0), covers: true).ShouldBeEmpty();
    }

    [Fact]
    public void A_friday_evening_reminds_for_monday_and_a_holiday_evening_for_the_next_session()
    {
        Derive(At(10, 30, 19, 0)).Single().Date.ShouldBe(new DateOnly(2026, 11, 2));
        Derive(At(10, 27, 20, 0)).Single().Date.ShouldBe(new DateOnly(2026, 10, 28)); // the holiday itself: tomorrow is the next session
        Derive(At(10, 31, 19, 30)).Single().Date.ShouldBe(new DateOnly(2026, 11, 2)); // Saturday evening
    }

    [Fact]
    public void On_a_trading_morning_not_ready_the_login_reminder_comes_at_08_15_the_warning_at_08_45_and_the_error_at_09_05()
    {
        Derive(At(10, 28, 8, 14)).ShouldBeEmpty();
        Derive(At(10, 28, 8, 15)).Select(n => (n.Kind, n.Severity)).ShouldBe([("exec_login_reminder", "info")]);
        Derive(At(10, 28, 8, 45)).Select(n => (n.Kind, n.Severity)).ShouldBe([("exec_login_reminder", "info"), ("exec_not_ready", "warning")]);
        Derive(At(10, 28, 9, 5)).Select(n => (n.Kind, n.Severity)).ShouldBe([("exec_login_reminder", "info"), ("exec_not_ready", "error")]);
        Derive(At(10, 28, 9, 5)).Single(n => n.Kind == "exec_not_ready").Id.ShouldBe("exec_not_ready:2026-10-28");
    }

    [Fact]
    public void The_same_id_all_day_means_one_notice_and_ready_or_the_close_or_a_closed_day_or_a_lower_mode_means_none()
    {
        Derive(At(10, 28, 8, 46)).Single(n => n.Kind == "exec_not_ready").Id.ShouldBe(Derive(At(10, 28, 11, 0)).Single(n => n.Kind == "exec_not_ready").Id);
        Derive(At(10, 28, 9, 30), ready: true).ShouldBeEmpty();
        Derive(At(10, 28, 16, 30)).ShouldBeEmpty();
        Derive(At(10, 27, 9, 30)).ShouldBeEmpty(); // the holiday
        Derive(At(10, 28, 9, 30), assisted: false).ShouldBeEmpty();
    }

    [Fact]
    public void The_recheck_is_due_with_no_record_or_on_and_after_the_review_date_only_for_an_account_at_assisted()
    {
        var today = new DateOnly(2026, 10, 30);

        ReadinessNotices.Recheck(today, null, true)!.Kind.ShouldBe("exec_recheck_due");
        ReadinessNotices.Recheck(today, today, true).ShouldNotBeNull();
        ReadinessNotices.Recheck(today, today.AddDays(-1), true).ShouldNotBeNull();
        ReadinessNotices.Recheck(today, today.AddDays(1), true).ShouldBeNull();
        ReadinessNotices.Recheck(today, null, false).ShouldBeNull();
        ReadinessNotices.Recheck(today, null, true)!.Id.ShouldBe("exec_recheck_due:2026-10-30");
    }
}
