using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Market.Calendar;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Calendar;

/// <summary>Reading and editing the trading calendar: the owner's add, confirm and remove, with validation and audit (ADR 0017 §1).</summary>
public sealed class CalendarHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero); // Monday 12:00 India

    private const string Actor = "owner@example.invalid";

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryCalendarData _data = new();
    private readonly FixedCalendar _calendar = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AddCalendarDayHandler Add => new(_data, _calendar, _data, _data, _clock);

    private ConfirmCalendarDayHandler Confirm => new(_data, _calendar, _data, _data, _clock);

    private RemoveCalendarDayHandler Remove => new(_data, _calendar, _data, _data, _clock);

    private GetCalendarHandler Get => new(_data, _calendar, _clock);

    private static TradingDay Row(string date, CalendarDayKind kind, CalendarSource source, CalendarStatus status) =>
        kind == CalendarDayKind.Holiday
            ? TradingDay.Holiday(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), source, status, "n", Now.AddDays(-1))
            : TradingDay.Special(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), 1095, 1155, source, status, "n", Now.AddDays(-1));

    private ValueTask<XDrishti.Domain.Common.Result<AddedCalendarDay>> AddAsync(string? date, string? kind, string? from = null, string? to = null, string? note = null) =>
        Add.HandleAsync(new AddCalendarDayCommand(new AddCalendarDayRequest(date, kind, from, to, note), Actor), Ct);

    [Fact]
    public async Task Adding_a_weekday_holiday_stores_it_confirmed_from_the_owner_and_audits_it()
    {
        var result = await AddAsync("2026-11-09", "holiday", note: "  Diwali  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Created.ShouldBeTrue();
        result.Value.Day.ShouldBe(new CalendarDayView(new DateOnly(2026, 11, 9), "holiday", null, null, null, "manual", "confirmed", "Diwali", Now));
        var stored = _data.Days.ShouldHaveSingleItem();
        (stored.Source, stored.Status).ShouldBe((CalendarSource.Manual, CalendarStatus.Confirmed));
        var audit = _data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Detail, audit.Actor, audit.AccountId).ShouldBe((AccountEventType.CalendarDayAdded, "Holiday 2026-11-09 (manual)", Actor, null));
        _calendar.Invalidations.ShouldBe(1);
    }

    [Fact]
    public async Task Adding_a_special_session_keeps_its_window_and_a_missing_window_means_the_regular_session()
    {
        var evening = await AddAsync("2026-11-08", "special", "18:15", "19:15");
        var saturday = await AddAsync("2026-11-14", "special");

        evening.Value.Day.ShouldBe(new CalendarDayView(new DateOnly(2026, 11, 8), "special", "18:15", "19:15", 60, "manual", "confirmed", null, Now));
        (saturday.Value.Day.SessionFrom, saturday.Value.Day.SessionTo, saturday.Value.Day.Minutes).ShouldBe(("09:15", "15:30", 375));
        _data.Events.Select(e => e.Detail).ShouldBe(["Special session 2026-11-08 18:15-19:15 (manual)", "Special session 2026-11-14 09:15-15:30 (manual)"]);
    }

    [Fact]
    public async Task Adding_replaces_what_the_date_had_and_says_it_was_not_new()
    {
        _data.Days.Add(Row("2026-11-09", CalendarDayKind.Holiday, CalendarSource.Observed, CalendarStatus.Proposed));

        var result = await AddAsync("2026-11-09", "special", "10:00", "11:00");

        result.Value.Created.ShouldBeFalse();
        var stored = _data.Days.ShouldHaveSingleItem();
        (stored.Kind, stored.Window, stored.Source, stored.Status).ShouldBe((CalendarDayKind.Special, (600, 660), CalendarSource.Manual, CalendarStatus.Confirmed));
    }

    [Fact]
    public async Task Adding_over_a_dismissed_day_makes_it_count()
    {
        _data.Days.Add(Row("2026-11-09", CalendarDayKind.Holiday, CalendarSource.Observed, CalendarStatus.Dismissed));

        await AddAsync("2026-11-09", "holiday");

        _data.Days.ShouldHaveSingleItem().Status.ShouldBe(CalendarStatus.Confirmed);
    }

    [Theory]
    [InlineData(null, "holiday", "date")]
    [InlineData("", "holiday", "date")]
    [InlineData("12/11/2026", "holiday", "date")]
    [InlineData("2026-13-01", "holiday", "date")]
    [InlineData("1999-12-31", "holiday", "date")]
    [InlineData("2031-10-13", "holiday", "date")] // more than five years ahead
    [InlineData("2026-11-14", "holiday", "date")] // a Saturday
    [InlineData("2026-11-15", "holiday", "date")] // a Sunday
    [InlineData("2026-11-09", null, "kind")]
    [InlineData("2026-11-09", "Holiday", "kind")]
    [InlineData("2026-11-09", "closed", "kind")]
    public async Task A_bad_date_or_kind_is_refused_with_the_field(string? date, string? kind, string field)
    {
        var result = await AddAsync(date, kind);

        result.Error.Code.ShouldBe("data.invalid");
        result.Error.Fields!.Select(f => f.Field).ShouldContain(field);
        _data.Days.ShouldBeEmpty();
        _data.Events.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("09:15", null, "sessionTo")]
    [InlineData(null, "15:30", "sessionFrom")]
    [InlineData("9.15", "15:30", "sessionFrom")]
    [InlineData("09:15", "25:00", "sessionTo")]
    [InlineData("10:00", "10:00", "sessionTo")]
    [InlineData("12:00", "11:00", "sessionTo")]
    [InlineData("09:15", "24:00", "sessionTo")]
    public async Task A_special_session_needs_a_readable_window_that_starts_before_it_ends(string? from, string? to, string field)
    {
        var result = await AddAsync("2026-11-14", "special", from, to);

        result.Error.Fields!.Select(f => f.Field).ShouldContain(field);
    }

    [Fact]
    public async Task A_holiday_has_no_window()
    {
        var result = await AddAsync("2026-11-09", "holiday", "09:15", "15:30");

        result.Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe("sessionFrom");
    }

    [Fact]
    public async Task The_note_is_limited_and_every_problem_is_reported_at_once()
    {
        var result = await AddAsync("nope", "nope", note: new string('x', 201));

        result.Error.Fields!.Select(f => f.Field).ShouldBe(["date", "kind", "note"]);
        (await AddAsync("2026-11-09", "holiday", note: "a\u0001b")).Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe("note");
        (await AddAsync("2026-11-09", "holiday", note: new string('x', 200))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_collision_with_a_concurrent_add_is_retried_against_what_was_stored()
    {
        _data.Collisions.Enqueue(() => _data.Days.Add(Row("2026-11-09", CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed)));

        var result = await AddAsync("2026-11-09", "holiday", note: "mine");

        result.Value.Created.ShouldBeFalse();
        _data.Days.ShouldHaveSingleItem().Source.ShouldBe(CalendarSource.Manual);
        _data.Events.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_save_that_keeps_colliding_gives_up_with_a_message()
    {
        for (var i = 0; i < CalendarChecks.Attempts; i++)
        {
            _data.Collisions.Enqueue(() => { });
        }

        var result = await AddAsync("2026-11-09", "holiday");

        result.Error.Code.ShouldBe("data.invalid");
        _data.Days.ShouldBeEmpty();
    }

    [Fact]
    public async Task Confirming_a_proposal_makes_it_count_and_is_audited_once()
    {
        _data.Days.Add(Row("2026-11-09", CalendarDayKind.Holiday, CalendarSource.Observed, CalendarStatus.Proposed));

        var first = await Confirm.HandleAsync(new ConfirmCalendarDayCommand("2026-11-09", Actor), Ct);
        var again = await Confirm.HandleAsync(new ConfirmCalendarDayCommand("2026-11-09", Actor), Ct);

        (first.Value.Status, first.Value.Source, again.Value.Status).ShouldBe(("confirmed", "observed", "confirmed"));
        _data.Events.ShouldHaveSingleItem().Event.ShouldBe(AccountEventType.CalendarDayConfirmed);
        _calendar.Invalidations.ShouldBe(1);
    }

    [Theory]
    [InlineData("2026-11-10")]   // no row
    [InlineData("2026-11-09")]   // dismissed
    public async Task Confirming_a_missing_or_dismissed_day_is_not_found(string date)
    {
        _data.Days.Add(Row("2026-11-09", CalendarDayKind.Holiday, CalendarSource.Observed, CalendarStatus.Dismissed));

        var result = await Confirm.HandleAsync(new ConfirmCalendarDayCommand(date, Actor), Ct);

        result.Error.Code.ShouldBe("data.calendar_day_not_found");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tomorrow")]
    [InlineData("2026-1-9")]
    public async Task A_date_that_cannot_be_read_is_refused_by_confirm_and_remove(string? date)
    {
        var confirm = await Confirm.HandleAsync(new ConfirmCalendarDayCommand(date, Actor), Ct);
        var remove = await Remove.HandleAsync(new RemoveCalendarDayCommand(date, Actor), Ct);

        (confirm.Error.Code, remove.Error.Code).ShouldBe(("data.invalid", "data.invalid"));
        confirm.Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe("date");
    }

    [Fact]
    public async Task Removing_the_owners_own_day_deletes_it()
    {
        _data.Days.Add(Row("2026-11-09", CalendarDayKind.Holiday, CalendarSource.Manual, CalendarStatus.Confirmed));

        (await Remove.HandleAsync(new RemoveCalendarDayCommand("2026-11-09", Actor), Ct)).IsSuccess.ShouldBeTrue();

        _data.Days.ShouldBeEmpty();
        _data.Events.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Event.ShouldBe(AccountEventType.CalendarDayRemoved),
            e => e.Detail.ShouldBe("Holiday 2026-11-09 (manual)"));
    }

    [Theory]
    [InlineData(CalendarSource.Observed, CalendarStatus.Proposed)]  // Dismiss
    [InlineData(CalendarSource.Observed, CalendarStatus.Confirmed)]
    [InlineData(CalendarSource.Nse, CalendarStatus.Confirmed)]
    public async Task Removing_a_proposal_or_a_listed_day_dismisses_it_so_it_is_not_proposed_again(CalendarSource source, CalendarStatus status)
    {
        _data.Days.Add(Row("2026-11-09", CalendarDayKind.Holiday, source, status));

        (await Remove.HandleAsync(new RemoveCalendarDayCommand("2026-11-09", Actor), Ct)).IsSuccess.ShouldBeTrue();

        _data.Days.ShouldHaveSingleItem().Status.ShouldBe(CalendarStatus.Dismissed);
        _calendar.Invalidations.ShouldBe(1);
    }

    private TradingDay ListedHolidayWithMuhurat()
    {
        var day = Row("2026-11-09", CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed);
        day.Propose(CalendarDayKind.Special, (1095, 1155), "Seen in the stored bars", Now);
        _data.Days.Add(day);
        return day;
    }

    [Fact]
    public async Task A_proposal_next_to_a_listed_holiday_is_shown_under_the_proposals_while_the_holiday_stays_a_confirmed_day()
    {
        var day = ListedHolidayWithMuhurat();
        _calendar.Calendar = new TradingCalendar(_data.Days);

        var calendar = (await Get.HandleAsync(new GetCalendarQuery("2026-11-01", "2026-11-30"), Ct)).Value;

        calendar.Days.ShouldHaveSingleItem().ShouldBe(CalendarDayView.From(day));
        calendar.Days[0].Kind.ShouldBe("holiday");
        var proposal = calendar.Proposals.ShouldHaveSingleItem();
        (proposal.Date, proposal.Kind, proposal.SessionFrom, proposal.SessionTo, proposal.Minutes, proposal.Source, proposal.Status, proposal.Replaces)
            .ShouldBe((new DateOnly(2026, 11, 9), "special", "18:15", "19:15", 60, "observed", "proposed", "holiday"));
        (await _data.CountProposedAsync(Ct)).ShouldBe(1);
        _calendar.Calendar.IsTradingDay(new DateOnly(2026, 11, 9)).ShouldBeFalse(); // still a holiday for gaps and quality
    }

    [Fact]
    public async Task Confirming_the_proposal_next_to_a_listed_holiday_turns_the_day_into_the_special_session()
    {
        var day = ListedHolidayWithMuhurat();

        var result = await Confirm.HandleAsync(new ConfirmCalendarDayCommand("2026-11-09", Actor), Ct);

        (result.Value.Kind, result.Value.SessionFrom, result.Value.Source, result.Value.Status).ShouldBe(("special", "18:15", "observed", "confirmed"));
        day.HasPendingProposal.ShouldBeFalse();
        _data.Events.ShouldHaveSingleItem().Event.ShouldBe(AccountEventType.CalendarDayConfirmed);
    }

    [Fact]
    public async Task Removing_a_date_with_a_proposal_next_to_a_confirmed_day_dismisses_only_the_proposal_and_removing_again_takes_the_day_off()
    {
        var day = ListedHolidayWithMuhurat();

        (await Remove.HandleAsync(new RemoveCalendarDayCommand("2026-11-09", Actor), Ct)).IsSuccess.ShouldBeTrue();

        (day.Status, day.Kind, day.HasPendingProposal, day.ProposalStatus).ShouldBe((CalendarStatus.Confirmed, CalendarDayKind.Holiday, false, CalendarStatus.Dismissed));
        (await _data.CountProposedAsync(Ct)).ShouldBe(0);

        (await Remove.HandleAsync(new RemoveCalendarDayCommand("2026-11-09", Actor), Ct)).IsSuccess.ShouldBeTrue();

        day.Status.ShouldBe(CalendarStatus.Dismissed);
    }

    [Fact]
    public async Task Adding_the_day_yourself_settles_the_date_and_drops_the_proposal_next_to_it()
    {
        var day = ListedHolidayWithMuhurat();

        await AddAsync("2026-11-09", "special", "18:15", "19:15");

        (day.Source, day.Kind, day.ProposalStatus).ShouldBe((CalendarSource.Manual, CalendarDayKind.Special, (CalendarStatus?)null));
    }

    [Fact]
    public async Task Removing_a_dismissed_day_deletes_it()
    {
        _data.Days.Add(Row("2026-11-09", CalendarDayKind.Holiday, CalendarSource.Observed, CalendarStatus.Dismissed));

        await Remove.HandleAsync(new RemoveCalendarDayCommand("2026-11-09", Actor), Ct);

        _data.Days.ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_a_date_without_a_row_is_not_found()
    {
        var result = await Remove.HandleAsync(new RemoveCalendarDayCommand("2026-11-09", Actor), Ct);

        result.Error.Code.ShouldBe("data.calendar_day_not_found");
    }

    [Fact]
    public async Task The_calendar_lists_confirmed_days_in_the_window_every_proposal_and_the_next_holiday()
    {
        _data.Days.AddRange(
            Row("2026-10-02", CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed),      // before today
            Row("2026-10-20", CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed),
            Row("2026-11-08", CalendarDayKind.Special, CalendarSource.Manual, CalendarStatus.Confirmed),
            Row("2027-11-01", CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed),      // beyond a year
            Row("2024-11-01", CalendarDayKind.Special, CalendarSource.Observed, CalendarStatus.Proposed),  // history, still waiting
            Row("2026-10-30", CalendarDayKind.Holiday, CalendarSource.Observed, CalendarStatus.Dismissed));
        _calendar.Calendar = new TradingCalendar(_data.Days);

        var calendar = (await Get.HandleAsync(new GetCalendarQuery(null, null), Ct)).Value;

        (calendar.From, calendar.To).ShouldBe((new DateOnly(2026, 10, 12), new DateOnly(2027, 10, 12)));
        calendar.Days.Select(d => d.Date).ShouldBe([new DateOnly(2026, 10, 20), new DateOnly(2026, 11, 8)]);
        calendar.Proposals.ShouldHaveSingleItem().Date.ShouldBe(new DateOnly(2024, 11, 1));
        calendar.NextHoliday!.Date.ShouldBe(new DateOnly(2026, 10, 20));
    }

    [Fact]
    public async Task A_window_can_be_chosen_and_an_open_end_is_a_year_after_the_start()
    {
        _data.Days.AddRange(
            Row("2026-10-02", CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed),
            Row("2026-10-20", CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed));

        var both = (await Get.HandleAsync(new GetCalendarQuery("2026-10-01", "2026-10-02"), Ct)).Value;
        var fromOnly = (await Get.HandleAsync(new GetCalendarQuery("2026-10-03", null), Ct)).Value;

        both.Days.ShouldHaveSingleItem().Date.ShouldBe(new DateOnly(2026, 10, 2));
        (fromOnly.To, fromOnly.Days.Single().Date).ShouldBe((new DateOnly(2027, 10, 3), new DateOnly(2026, 10, 20)));
    }

    [Theory]
    [InlineData("x", null, "from")]
    [InlineData(null, "2026/10/10", "to")]
    [InlineData("2026-10-10", "2026-10-09", "to")]
    [InlineData("2010-01-01", "2026-10-09", "to")] // more than 3 660 days
    public async Task A_window_that_cannot_be_read_or_is_wrong_is_refused_with_the_field(string? from, string? to, string field)
    {
        var result = await Get.HandleAsync(new GetCalendarQuery(from, to), Ct);

        result.Error.Code.ShouldBe("data.invalid");
        result.Error.Fields!.Select(f => f.Field).ShouldContain(field);
    }
}
