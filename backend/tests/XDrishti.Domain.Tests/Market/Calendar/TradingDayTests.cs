using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Tests.Market.Calendar;

public sealed class TradingDayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly Date = new(2026, 10, 2);

    [Fact]
    public void A_holiday_has_no_window()
    {
        var day = TradingDay.Holiday(Date, CalendarSource.Manual, CalendarStatus.Confirmed, "  Gandhi Jayanti  ", Now);

        (day.Id, day.Kind, day.Window, day.SessionFrom, day.SessionTo, day.Source, day.Status, day.Note, day.UpdatedAt)
            .ShouldBe((Date, CalendarDayKind.Holiday, null, null, null, CalendarSource.Manual, CalendarStatus.Confirmed, "Gandhi Jayanti", Now));
    }

    [Fact]
    public void A_special_session_keeps_its_window_as_times_of_day()
    {
        var day = TradingDay.Special(new DateOnly(2024, 11, 1), 18 * 60 + 15, 19 * 60 + 15, CalendarSource.Observed, CalendarStatus.Proposed, null, Now);

        day.Window.ShouldBe((1095, 1155));
        (day.SessionFrom, day.SessionTo).ShouldBe((new TimeOnly(18, 15), new TimeOnly(19, 15)));
        day.IsConfirmed.ShouldBeFalse();
    }

    [Theory]
    [InlineData(600, 600)]
    [InlineData(700, 600)]
    [InlineData(-1, 600)]
    [InlineData(600, 1440)]
    [InlineData(600, 24 * 60 + 5)]
    public void A_session_must_start_before_it_ends_within_one_day(int from, int to)
    {
        TradingDay.IsValidWindow(from, to).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => TradingDay.Special(Date, from, to, CalendarSource.Manual, CalendarStatus.Confirmed, null, Now));
    }

    [Fact]
    public void A_session_may_end_at_the_last_minute_of_the_day() => TradingDay.IsValidWindow(0, 23 * 60 + 59).ShouldBeTrue();

    [Fact]
    public void A_date_before_the_calendar_starts_is_refused() =>
        Should.Throw<ArgumentOutOfRangeException>(() => TradingDay.Holiday(new DateOnly(1999, 12, 31), CalendarSource.Manual, CalendarStatus.Confirmed, null, Now));

    [Fact]
    public void A_long_note_is_cut_and_a_blank_one_is_dropped()
    {
        TradingDay.Holiday(Date, CalendarSource.Manual, CalendarStatus.Confirmed, new string('x', 500), Now).Note!.Length.ShouldBe(TradingDay.MaxNoteLength);
        TradingDay.Holiday(Date, CalendarSource.Manual, CalendarStatus.Confirmed, "   ", Now).Note.ShouldBeNull();
    }

    [Fact]
    public void Confirming_a_proposal_makes_it_count_and_confirming_again_changes_nothing()
    {
        var day = TradingDay.Holiday(Date, CalendarSource.Observed, CalendarStatus.Proposed, null, Now);
        var later = Now.AddHours(1);

        day.Confirm(later).ShouldBeTrue();
        day.Confirm(later.AddHours(1)).ShouldBeFalse();

        (day.Status, day.UpdatedAt, day.IsConfirmed).ShouldBe((CalendarStatus.Confirmed, later, true));
    }

    [Fact]
    public void A_dismissed_day_cannot_be_confirmed_and_dismissing_again_changes_nothing()
    {
        var day = TradingDay.Holiday(Date, CalendarSource.Observed, CalendarStatus.Proposed, null, Now);

        day.Dismiss(Now.AddHours(1)).ShouldBeTrue();
        day.Dismiss(Now.AddHours(2)).ShouldBeFalse();
        day.Confirm(Now.AddHours(3)).ShouldBeFalse();

        (day.Status, day.UpdatedAt).ShouldBe((CalendarStatus.Dismissed, Now.AddHours(1)));
    }

    [Fact]
    public void Replacing_a_holiday_by_a_special_session_and_back_keeps_the_window_consistent()
    {
        var day = TradingDay.Holiday(Date, CalendarSource.Nse, CalendarStatus.Confirmed, "x", Now);

        day.Replace(CalendarDayKind.Special, (600, 660), CalendarSource.Manual, CalendarStatus.Confirmed, null, Now.AddHours(1)).ShouldBeTrue();
        day.Window.ShouldBe((600, 660));
        day.Replace(CalendarDayKind.Holiday, null, CalendarSource.Manual, CalendarStatus.Confirmed, null, Now.AddHours(2)).ShouldBeTrue();

        (day.Window, day.SessionFrom, day.SessionTo, day.UpdatedAt).ShouldBe((null, null, null, Now.AddHours(2)));
    }

    [Fact]
    public void Replacing_with_the_same_values_changes_nothing()
    {
        var day = TradingDay.Special(Date, 600, 660, CalendarSource.Manual, CalendarStatus.Confirmed, "x", Now);

        day.Replace(CalendarDayKind.Special, (600, 660), CalendarSource.Manual, CalendarStatus.Confirmed, " x ", Now.AddHours(1)).ShouldBeFalse();

        day.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void A_special_session_needs_a_valid_window_when_replaced()
    {
        var day = TradingDay.Holiday(Date, CalendarSource.Manual, CalendarStatus.Confirmed, null, Now);

        Should.Throw<ArgumentException>(() => day.Replace(CalendarDayKind.Special, null, CalendarSource.Manual, CalendarStatus.Confirmed, null, Now));
        Should.Throw<ArgumentException>(() => day.Replace(CalendarDayKind.Special, (700, 600), CalendarSource.Manual, CalendarStatus.Confirmed, null, Now));
    }

    [Fact]
    public void The_codes_are_the_contract()
    {
        CalendarDayKind.Holiday.ToCode().ShouldBe("holiday");
        CalendarDayKind.Special.ToCode().ShouldBe("special");
        CalendarSource.Manual.ToCode().ShouldBe("manual");
        CalendarSource.Observed.ToCode().ShouldBe("observed");
        CalendarSource.Nse.ToCode().ShouldBe("nse");
        CalendarStatus.Confirmed.ToCode().ShouldBe("confirmed");
        CalendarStatus.Proposed.ToCode().ShouldBe("proposed");
        CalendarStatus.Dismissed.ToCode().ShouldBe("dismissed");
        CalendarDayKindCodes.TryParse("holiday", out var kind).ShouldBeTrue();
        kind.ShouldBe(CalendarDayKind.Holiday);
        CalendarDayKindCodes.TryParse("Holiday", out _).ShouldBeFalse();
        CalendarDayKindCodes.TryParse(null, out _).ShouldBeFalse();
    }

    private static TradingDay Listed() => TradingDay.Holiday(Date, CalendarSource.Nse, CalendarStatus.Confirmed, "Listed", Now);

    [Fact]
    public void A_proposal_next_to_a_confirmed_day_changes_nothing_about_the_day()
    {
        var day = Listed();

        var proposed = day.Propose(CalendarDayKind.Special, (1095, 1155), "seen", Now.AddHours(1));

        proposed.ShouldBeTrue();
        (day.Kind, day.Status, day.IsConfirmed, day.Window).ShouldBe((CalendarDayKind.Holiday, CalendarStatus.Confirmed, true, null));
        (day.HasPendingProposal, day.ProposedKind, day.ProposedWindow, day.ProposedNote).ShouldBe((true, CalendarDayKind.Special, (1095, 1155), "seen"));
    }

    [Fact]
    public void The_same_proposal_again_changes_nothing_and_a_different_one_replaces_it()
    {
        var day = Listed();
        day.Propose(CalendarDayKind.Special, (1095, 1155), "seen", Now);

        var same = day.Propose(CalendarDayKind.Special, (1095, 1155), "seen", Now.AddHours(1));
        var other = day.Propose(CalendarDayKind.Special, (555, 930), "seen", Now.AddHours(2));

        (same, other, day.ProposedWindow).ShouldBe((false, true, (555, 930)));
    }

    [Fact]
    public void A_dismissed_proposal_is_remembered_and_not_made_again_until_the_owner_decides_the_date()
    {
        var day = Listed();
        day.Propose(CalendarDayKind.Special, (1095, 1155), "seen", Now);

        var dismissed = day.DismissProposal(Now);
        var again = day.Propose(CalendarDayKind.Special, (1095, 1155), "seen", Now);
        var clearedSilently = day.ClearProposal(Now);
        var cleared = day.ClearProposal(Now, includingDismissed: true);

        (dismissed, again, clearedSilently, cleared).ShouldBe((true, false, false, true));
        (day.ProposalStatus, day.ProposedKind, day.ProposedNote).ShouldBe(((CalendarStatus?)null, (CalendarDayKind?)null, (string?)null));
        day.Propose(CalendarDayKind.Special, (1095, 1155), "seen", Now).ShouldBeTrue();
    }

    [Fact]
    public void Confirming_the_proposal_replaces_the_day_as_an_observed_confirmed_one()
    {
        var day = Listed();
        day.Propose(CalendarDayKind.Special, (1095, 1155), "seen", Now);

        var confirmed = day.ConfirmProposal(Now.AddHours(1));

        confirmed.ShouldBeTrue();
        (day.Kind, day.Window, day.Source, day.Status, day.Note, day.ProposalStatus, day.HasPendingProposal)
            .ShouldBe((CalendarDayKind.Special, (1095, 1155), CalendarSource.Observed, CalendarStatus.Confirmed, "seen", (CalendarStatus?)null, false));
        day.ConfirmProposal(Now).ShouldBeFalse();
    }

    [Fact]
    public void A_special_proposal_needs_a_valid_window()
    {
        Should.Throw<ArgumentException>(() => Listed().Propose(CalendarDayKind.Special, null, null, Now));
        Should.Throw<ArgumentException>(() => Listed().Propose(CalendarDayKind.Special, (700, 600), null, Now));
    }
}
