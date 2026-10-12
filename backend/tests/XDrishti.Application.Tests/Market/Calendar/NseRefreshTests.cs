using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Market.Calendar;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Calendar;

/// <summary>NSE's public holiday list is best effort: every failure is a message, nothing throws, nothing changes (ADR 0017 §1).</summary>
public sealed class NseRefreshTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryCalendarData _data = new();
    private readonly FixedCalendar _calendar = new();
    private readonly FakeHolidaySource _source = new();
    private readonly HolidayFetchGate _gate = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private NseCalendarRefresher Refresher => new(_source, _data, _calendar, _data, _data, _clock, NullLogger<NseCalendarRefresher>.Instance);

    private RefreshCalendarHandler Refresh => new(Refresher, _gate, _clock);

    [Fact]
    public async Task The_owner_asking_adds_the_holidays_confirms_them_and_audits_the_change()
    {
        _source.Json = FakeHolidaySource.Sample(("26-Jan-2027", "Republic Day"), ("03-Nov-2026", "Diwali"));

        var result = (await Refresh.HandleAsync(new RefreshCalendarCommand("owner@example.invalid"), Ct)).Value;

        (result.Succeeded, result.Added, result.Updated, result.Unchanged, result.Skipped, result.At).ShouldBe((true, 2, 0, 0, 0, Now));
        result.Message.ShouldBe("NSE's list has 2 weekday holidays: 2 added, 0 updated, 0 already known.");
        _data.Days.ShouldAllBe(d => d.Source == CalendarSource.Nse && d.Status == CalendarStatus.Confirmed && d.Kind == CalendarDayKind.Holiday);
        var audit = _data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Detail, audit.Actor).ShouldBe((AccountEventType.CalendarRefreshedFromNse, "NSE's holiday list: 2 added, 0 updated", "owner@example.invalid"));
        _calendar.Invalidations.ShouldBe(1);
    }

    [Fact]
    public async Task Asking_again_changes_nothing_and_audits_nothing()
    {
        _source.Json = FakeHolidaySource.Sample(("03-Nov-2026", "Diwali"));
        await Refresh.HandleAsync(new RefreshCalendarCommand("owner"), Ct);
        _data.Events.Clear();

        var again = (await Refresh.HandleAsync(new RefreshCalendarCommand("owner"), Ct)).Value;

        (again.Succeeded, again.Added, again.Unchanged).ShouldBe((true, 0, 1));
        _data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_automatic_attempt_is_not_audited()
    {
        _source.Json = FakeHolidaySource.Sample(("03-Nov-2026", "Diwali"));

        var result = await Refresher.RefreshAsync(null, Ct);

        result.Added.ShouldBe(1);
        _data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Weekends_dates_out_of_range_and_unreadable_entries_are_left_out()
    {
        _source.Json = FakeHolidaySource.Sample(
            ("14-Nov-2026", "a Saturday"),
            ("15-Nov-2026", "a Sunday"),
            ("05-Nov-2040", "far away"),
            ("05-Nov-1999", "ancient"),
            ("nonsense", "unreadable"),
            ("03-Nov-2026", "Diwali"));

        var result = await Refresher.RefreshAsync(null, Ct);

        (result.Added, result.Skipped).ShouldBe((1, 5));
        result.Message.ShouldBe("NSE's list has 1 weekday holidays: 1 added, 0 updated, 0 already known, 5 left out.");
        _data.Days.ShouldHaveSingleItem().Id.ShouldBe(new DateOnly(2026, 11, 3));
    }

    [Fact]
    public async Task What_the_owner_decided_is_left_alone_and_counted_as_left_out()
    {
        _data.Days.Add(TradingDay.Holiday(new DateOnly(2026, 11, 3), CalendarSource.Manual, CalendarStatus.Confirmed, "mine", Now));
        _data.Days.Add(TradingDay.Holiday(new DateOnly(2026, 12, 25), CalendarSource.Nse, CalendarStatus.Dismissed, "no", Now));
        _source.Json = FakeHolidaySource.Sample(("03-Nov-2026", "Diwali"), ("25-Dec-2026", "Christmas"));

        var result = await Refresher.RefreshAsync("owner", Ct);

        (result.Added, result.Skipped).ShouldBe((0, 2));
        _data.Days.Select(d => d.Note).ShouldBe(["mine", "no"], ignoreOrder: true);
        _data.Events.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("NSE's holiday list answered HTTP 403.")]
    [InlineData("NSE's holiday list could not be reached.")]
    [InlineData("NSE's holiday list did not answer in time.")]
    public async Task A_failed_download_is_an_answer_with_the_reason_and_advice_and_changes_nothing(string failure)
    {
        _source.Failure = failure;

        var result = (await Refresh.HandleAsync(new RefreshCalendarCommand("owner"), Ct)).Value;

        (result.Succeeded, result.Added, result.Updated, result.Unchanged, result.Skipped).ShouldBe((false, 0, 0, 0, 0));
        result.Message.ShouldBe(failure + " Add the holidays by hand instead.");
        _data.Days.ShouldBeEmpty();
        _data.Events.ShouldBeEmpty();
        _calendar.Invalidations.ShouldBe(0);
    }

    [Theory]
    [InlineData("<html>Access Denied</html>", "not valid JSON")]
    [InlineData("{\"FO\":[]}", "no cash-market")]
    public async Task A_list_that_cannot_be_read_is_an_answer_too(string body, string expected)
    {
        _source.Json = body;

        var result = await Refresher.RefreshAsync(null, Ct);

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldContain(expected);
        _data.Days.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_save_that_collides_is_reported_and_drops_the_changes()
    {
        _source.Json = FakeHolidaySource.Sample(("03-Nov-2026", "Diwali"));
        _data.Collisions.Enqueue(() => { });

        var result = await Refresher.RefreshAsync(null, Ct);

        result.Succeeded.ShouldBeFalse();
        _data.Days.ShouldBeEmpty();
        _calendar.Invalidations.ShouldBe(0);
    }

    [Fact]
    public async Task The_owners_request_counts_as_the_days_attempt()
    {
        _source.Json = FakeHolidaySource.Sample();

        await Refresh.HandleAsync(new RefreshCalendarCommand("owner"), Ct);

        _gate.TryBegin(new DateOnly(2026, 10, 12)).ShouldBeFalse();
        _gate.TryBegin(new DateOnly(2026, 10, 13)).ShouldBeTrue();
    }

    [Fact]
    public void The_gate_lets_one_attempt_through_per_day()
    {
        var gate = new HolidayFetchGate();

        gate.TryBegin(new DateOnly(2026, 10, 12)).ShouldBeTrue();
        gate.TryBegin(new DateOnly(2026, 10, 12)).ShouldBeFalse();
        gate.TryBegin(new DateOnly(2026, 10, 13)).ShouldBeTrue();
    }
}
