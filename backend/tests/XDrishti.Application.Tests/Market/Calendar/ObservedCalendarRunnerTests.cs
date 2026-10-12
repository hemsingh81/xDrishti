using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Market.Calendar;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Calendar;

/// <summary>The pass after every data job that turns the stored bars into calendar proposals (ADR 0017 §1): proposals only, never confirmed.</summary>
public sealed class ObservedCalendarRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly First = new(2024, 10, 14);

    private static readonly DateOnly Last = new(2024, 11, 15);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryCalendarData _data = new();
    private readonly FixedCalendar _calendar = new();
    private readonly FakeObservedDays _observed = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ObservedCalendarRunner Runner => new(_observed, _data, _calendar, _data, _clock, NullLogger<ObservedCalendarRunner>.Instance);

    [Fact]
    public async Task Bars_that_are_not_aggregated_yet_conclude_nothing_and_leave_the_proposals_as_they_are()
    {
        _data.Days.Add(TradingDay.Holiday(new DateOnly(2024, 10, 31), CalendarSource.Observed, CalendarStatus.Proposed, "x", Now));
        _observed.Presence = new PresenceBuilder().Weekdays(10, First, Last).Build(pending: true);

        var run = await Runner.RunAsync(Ct);

        run.Skipped.ShouldBeTrue();
        _data.Days.ShouldHaveSingleItem();
        _data.Saves.ShouldBe(0);
        _calendar.Invalidations.ShouldBe(0);
    }

    [Fact]
    public async Task The_muhurat_evening_the_saturday_and_the_holiday_come_out_as_proposals_in_one_pass()
    {
        var muhurat = new DateOnly(2024, 11, 1);
        var saturday = new DateOnly(2024, 10, 19);
        var holiday = new DateOnly(2024, 10, 22);
        _observed.Presence = new PresenceBuilder()
            .Weekdays(214, First, Last, (_, d) => d != holiday, d => d == muhurat ? 60 : 375)
            .On(saturday, 214, 60)
            .Build();
        _observed.Windows[muhurat] = new ObservedWindow(214, 1095, 1155);
        _observed.Windows[saturday] = new ObservedWindow(214, 555, 615);

        var run = await Runner.RunAsync(Ct);

        run.ShouldBe(new ObservedRun(false, 3, 0, 0));
        _data.Days.Select(d => (d.Id, d.Kind, d.Window, d.Source, d.Status)).ShouldBe(
            [
                (saturday, CalendarDayKind.Special, ((int, int)?)(555, 615), CalendarSource.Observed, CalendarStatus.Proposed),
                (holiday, CalendarDayKind.Holiday, null, CalendarSource.Observed, CalendarStatus.Proposed),
                (muhurat, CalendarDayKind.Special, (1095, 1155), CalendarSource.Observed, CalendarStatus.Proposed),
            ],
            ignoreOrder: true);
        _observed.WindowRequests.ShouldHaveSingleItem().OrderBy(d => d).ShouldBe([saturday, muhurat]);
        _calendar.Invalidations.ShouldBe(1);
    }

    [Fact]
    public async Task Proposals_are_never_confirmed_so_the_calendar_still_sees_ordinary_weekdays()
    {
        var holiday = new DateOnly(2024, 10, 22);
        _observed.Presence = new PresenceBuilder().Weekdays(50, First, Last, (_, d) => d != holiday).Build();

        await Runner.RunAsync(Ct);

        new TradingCalendar(_data.Days).IsTradingDay(holiday).ShouldBeTrue();
    }

    [Fact]
    public async Task Running_again_on_the_same_bars_changes_nothing_and_saves_nothing()
    {
        _observed.Presence = new PresenceBuilder().Weekdays(50, First, Last, (_, d) => d != new DateOnly(2024, 10, 22)).Build();
        await Runner.RunAsync(Ct);
        var saves = _data.Saves;

        var again = await Runner.RunAsync(Ct);

        again.ShouldBe(new ObservedRun(false, 0, 0, 0));
        _data.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task A_proposal_the_filled_in_bars_no_longer_support_is_withdrawn_but_a_confirmed_or_dismissed_one_stays()
    {
        _data.Days.Add(TradingDay.Holiday(new DateOnly(2024, 10, 22), CalendarSource.Observed, CalendarStatus.Proposed, "x", Now));
        _data.Days.Add(TradingDay.Holiday(new DateOnly(2024, 10, 23), CalendarSource.Observed, CalendarStatus.Confirmed, "x", Now));
        _data.Days.Add(TradingDay.Holiday(new DateOnly(2024, 10, 24), CalendarSource.Observed, CalendarStatus.Dismissed, "x", Now));
        _observed.Presence = new PresenceBuilder().Weekdays(50, First, Last).Build();

        var run = await Runner.RunAsync(Ct);

        run.Removed.ShouldBe(1);
        _data.Days.Select(d => d.Id.Day).ShouldBe([23, 24], ignoreOrder: true);
    }

    [Fact]
    public async Task A_save_that_collides_drops_the_pass_without_failing()
    {
        _observed.Presence = new PresenceBuilder().Weekdays(50, First, Last, (_, d) => d != new DateOnly(2024, 10, 22)).Build();
        _data.Collisions.Enqueue(() => { });

        var run = await Runner.RunAsync(Ct);

        run.ShouldBe(new ObservedRun(false, 0, 0, 0));
        _data.Days.ShouldBeEmpty();
        _calendar.Invalidations.ShouldBe(0);
    }

    [Fact]
    public async Task No_windows_are_read_when_no_date_can_be_special()
    {
        _observed.Presence = new PresenceBuilder().Weekdays(50, First, Last).Build();

        await Runner.RunAsync(Ct);

        _observed.WindowRequests.ShouldBeEmpty();
    }
}
