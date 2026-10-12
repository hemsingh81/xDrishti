using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Calendar;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// The calendar in the data pipeline (ADR 0017 §1, P1-11a): proposals derived from stored bars of the real data shape, NSE's list once a day after a job, and "behind" by the
/// calendar across a long weekend and a special session.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataCalendarPipelineTests(ApiFactory factory) : DataTestBase(factory)
{
    private const int Instruments = 25;

    private static readonly DateOnly Muhurat = new(2024, 11, 1);       // a Friday: 60 evening bars from 18:15 for every instrument
    private static readonly DateOnly Saturday = new(2024, 10, 26);     // a special Saturday session: 60 bars from 09:15
    private static readonly DateOnly Closed = new(2024, 10, 22);       // a Tuesday on which no instrument has a bar
    private static readonly DateOnly OneOfMany = new(2024, 10, 23);    // 1 of 25 instruments (4 %): suspected
    private static readonly DateOnly TwoOfMany = new(2024, 10, 24);    // 2 of 25 (8 %): open, some files just lack the day
    private static readonly DateOnly ThreeMissing = new(2024, 10, 28); // 3 instruments lack the day: open

    /// <summary>Weekdays from Mon 2024-10-14 to Fri 2024-11-08 with the shapes above, for the instrument with this index (0-based).</summary>
    private static List<Bar> History(int index)
    {
        var bars = new List<Bar>();
        for (var date = new DateOnly(2024, 10, 14); date <= new DateOnly(2024, 11, 8); date = date.AddDays(1))
        {
            if (date == Muhurat)
            {
                bars.AddRange(BarFixtures.EveningSession(date, index + 1));
            }
            else if (date == Saturday)
            {
                bars.AddRange(BarFixtures.Session(date, index + 1, count: 60));
            }
            else if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || date == Closed
                     || (date == OneOfMany && index != 0)
                     || (date == TwoOfMany && index > 1)
                     || (date == ThreeMissing && index < 3))
            {
                continue;
            }
            else
            {
                bars.AddRange(BarFixtures.Session(date, index + 1));
            }
        }

        return bars;
    }

    private async Task LoadHistoryAsync()
    {
        for (var i = 0; i < Instruments; i++)
        {
            var ticker = $"T{i + 1:00}";
            await SeedAsync(ticker);
            WriteCsv(ticker, History(i));
        }
    }

    private async Task<CalendarView> CalendarAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/data/calendar" + query, Ct);
        return await response.ReadAsync<CalendarView>(Ct);
    }

    private static string Show(CalendarDayView d) => $"{d.Date:yyyy-MM-dd} {d.Kind} {d.SessionFrom}-{d.SessionTo} {d.Source}/{d.Status}";

    [Fact]
    public async Task Stored_bars_of_the_real_shape_propose_the_muhurat_evening_the_saturday_and_the_closed_weekdays_and_nothing_else()
    {
        await LoadHistoryAsync();

        // Bars that are not aggregated yet say nothing about the days: a CSV-only job proposes nothing.
        var loaded = await RunAsync(["csv"]);
        loaded.Status.ShouldBe("succeeded", loaded.Error);
        (await CalendarAsync("?from=2024-01-01&to=2024-12-31")).Proposals.ShouldBeEmpty();

        var job = await RunAsync(["aggregate"]);

        job.Status.ShouldBe("succeeded", job.Error);
        job.Warnings.ShouldBeEmpty();
        var calendar = await CalendarAsync("?from=2024-01-01&to=2024-12-31");
        calendar.Proposals.Select(Show).ShouldBe(
            [
                "2024-10-22 holiday - observed/proposed",
                "2024-10-23 holiday - observed/proposed",
                "2024-10-26 special 09:15-10:15 observed/proposed",
                "2024-11-01 special 18:15-19:15 observed/proposed",
            ]);
        calendar.Proposals.Single(p => p.Date == Muhurat).Minutes.ShouldBe(60);
        calendar.Days.ShouldBeEmpty(); // nothing is confirmed by itself
        var overview = await OverviewAsync();
        (overview.CalendarProposals, overview.NextHoliday).ShouldBe((4, null));
    }

    [Fact]
    public async Task Confirming_dismissing_and_running_again_keep_the_decisions_and_do_not_duplicate_proposals()
    {
        await LoadHistoryAsync();
        await RunAsync(["csv", "aggregate"]);
        (await Client.PutJsonAsync("/api/data/calendar/2024-10-22/confirm", new { }, Ct)).EnsureSuccessStatusCode();
        (await Client.DeleteAsync("/api/data/calendar/2024-10-23", Ct)).EnsureSuccessStatusCode();

        var again = await RunAsync(["aggregate"]);

        again.Status.ShouldBe("succeeded", again.Error);
        var calendar = await CalendarAsync("?from=2024-01-01&to=2024-12-31");
        calendar.Days.Select(Show).ShouldBe(["2024-10-22 holiday - observed/confirmed"]);
        calendar.Proposals.Select(p => p.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(["2024-10-26", "2024-11-01"]);
        (await StatusOfAsync("2024-10-23")).ShouldBe("dismissed"); // not proposed again
        (await ScalarAsync<long>("SELECT count(*) FROM market.trading_days")).ShouldBe(4);
        (await OverviewAsync()).CalendarProposals.ShouldBe(2);
    }

    private async Task ListByNseAsync(DateOnly date)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO market.trading_days (date, kind, source, status, note, updated_at) VALUES (@date, 'holiday', 'nse', 'confirmed', 'Listed by NSE', now())",
            ("date", date));
    }

    [Fact]
    public async Task A_muhurat_session_on_a_holiday_NSE_lists_is_proposed_next_to_the_holiday_which_stays_in_force_until_the_owner_confirms()
    {
        await LoadHistoryAsync();
        await ListByNseAsync(Muhurat);

        var job = await RunAsync(["csv", "aggregate"]);

        job.Status.ShouldBe("succeeded", job.Error);
        var calendar = await CalendarAsync("?from=2024-01-01&to=2024-12-31");
        calendar.Days.Select(Show).ShouldBe(["2024-11-01 holiday - nse/confirmed"]); // the listed holiday still decides what a session is
        var proposal = calendar.Proposals.Single(p => p.Date == Muhurat);
        (Show(proposal), proposal.Replaces, proposal.Minutes).ShouldBe(("2024-11-01 special 18:15-19:15 observed/proposed", "holiday", 60));
        (await ScalarAsync<string>("SELECT kind || '/' || source || '/' || status || '/' || proposed_kind FROM market.trading_days WHERE date = '2024-11-01'")).ShouldBe("holiday/nse/confirmed/special");
        (await OverviewAsync()).CalendarProposals.ShouldBe(4);

        var again = await RunAsync(["aggregate"]); // nothing new: no duplicate, no change
        again.Status.ShouldBe("succeeded", again.Error);
        (await ScalarAsync<long>("SELECT count(*) FROM market.trading_days")).ShouldBe(4);

        (await Client.PutJsonAsync("/api/data/calendar/2024-11-01/confirm", new { }, Ct)).EnsureSuccessStatusCode();

        var confirmed = await CalendarAsync("?from=2024-01-01&to=2024-12-31");
        confirmed.Days.Select(Show).ShouldBe(["2024-11-01 special 18:15-19:15 observed/confirmed"]);
        confirmed.Proposals.Select(p => p.Date).ShouldNotContain(Muhurat);
        (await ScalarAsync<bool>("SELECT proposal_status IS NULL AND proposed_kind IS NULL FROM market.trading_days WHERE date = '2024-11-01'")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_regular_session_on_a_moved_holiday_is_proposed_as_a_special_session_next_to_the_listed_holiday_and_dismissing_it_keeps_the_holiday()
    {
        var moved = new DateOnly(2024, 10, 29); // listed as a holiday, but every instrument traded the regular session
        await LoadHistoryAsync();
        await ListByNseAsync(moved);

        await RunAsync(["csv", "aggregate"]);

        var calendar = await CalendarAsync("?from=2024-01-01&to=2024-12-31");
        calendar.Days.Select(Show).ShouldBe(["2024-10-29 holiday - nse/confirmed"]);
        Show(calendar.Proposals.Single(p => p.Date == moved)).ShouldBe("2024-10-29 special 09:15-15:30 observed/proposed");

        (await Client.DeleteAsync("/api/data/calendar/2024-10-29", Ct)).EnsureSuccessStatusCode(); // dismiss the proposal only
        await RunAsync(["aggregate"]);

        var after = await CalendarAsync("?from=2024-01-01&to=2024-12-31");
        after.Days.Select(Show).ShouldBe(["2024-10-29 holiday - nse/confirmed"]);
        after.Proposals.Select(p => p.Date).ShouldNotContain(moved); // not proposed again
        (await ScalarAsync<string>("SELECT proposal_status FROM market.trading_days WHERE date = '2024-10-29'")).ShouldBe("dismissed");

        (await Client.DeleteAsync("/api/data/calendar/2024-10-29", Ct)).EnsureSuccessStatusCode(); // the second removal takes the listed day off
        (await StatusOfAsync("2024-10-29")).ShouldBe("dismissed");
        (await CalendarAsync("?from=2024-01-01&to=2024-12-31")).Days.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_proposal_that_the_bars_no_longer_support_is_withdrawn_once_the_day_is_filled_in()
    {
        await LoadHistoryAsync();
        await RunAsync(["csv", "aggregate"]);
        (await CalendarAsync("?from=2024-01-01&to=2024-12-31")).Proposals.Select(p => p.Date).ShouldContain(Closed);

        // The day arrives later (a late CSV file for every instrument).
        for (var i = 0; i < Instruments; i++)
        {
            WriteCsv($"T{i + 1:00}", [.. History(i), .. BarFixtures.Session(Closed, i + 1)], modified: new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));
        }

        await RunAsync(["csv", "aggregate"]);

        (await CalendarAsync("?from=2024-01-01&to=2024-12-31")).Proposals.Select(p => p.Date).ShouldNotContain(Closed);
        (await ScalarAsync<long>("SELECT count(*) FROM market.trading_days WHERE date = '2024-10-22'")).ShouldBe(0);
    }

    [Fact]
    public async Task Data_to_thursday_is_current_across_a_long_weekend_once_the_holidays_are_on_the_calendar()
    {
        await SeedAsync("ABB");
        WriteCsv("ABB", [.. BarFixtures.Session(new DateOnly(2026, 10, 7), 1), .. BarFixtures.Session(new DateOnly(2026, 10, 8), 1)]);
        await RunAsync(["csv", "aggregate"]);

        var before = await OverviewAsync(); // Monday 12:00: the Friday session is the last that closed
        (await Client.PostJsonAsync("/api/data/calendar", new { date = "2026-10-09", kind = "holiday" }, Ct)).EnsureSuccessStatusCode();
        (await Client.PostJsonAsync("/api/data/calendar", new { date = "2026-10-12", kind = "holiday" }, Ct)).EnsureSuccessStatusCode();
        var holidays = await OverviewAsync();
        var row = (await InstrumentsAsync()).Items.Single();

        (before.Bars.ExpectedSession, before.Bars.DaysBehind, before.Bars.Stale).ShouldBe((new DateOnly(2026, 10, 9), 1, true));
        (holidays.Bars.ExpectedSession, holidays.Bars.DaysBehind, holidays.Bars.Stale, holidays.Bars.InstrumentsBehind).ShouldBe((new DateOnly(2026, 10, 8), 0, false, 0));
        (row.Status, row.DaysBehind).ShouldBe(("current", 0));
        holidays.NextHoliday!.Date.ShouldBe(new DateOnly(2026, 10, 12)); // today (Monday) is itself a holiday; Friday is past
    }

    [Fact]
    public async Task The_trading_days_behind_follow_the_calendar_as_the_week_goes_on()
    {
        await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(new DateOnly(2026, 10, 8), 1));
        await RunAsync(["csv", "aggregate"]);
        (await Client.PostJsonAsync("/api/data/calendar", new { date = "2026-10-09", kind = "holiday" }, Ct)).EnsureSuccessStatusCode();
        (await Client.PostJsonAsync("/api/data/calendar", new { date = "2026-10-12", kind = "holiday" }, Ct)).EnsureSuccessStatusCode();

        // India time, as UTC: 10:00 IST = 04:30 UTC, 16:00 IST = 10:30 UTC.
        var tuesdayMorning = await OverviewAtAsync(new DateTimeOffset(2026, 10, 13, 4, 30, 0, TimeSpan.Zero));
        var tuesdayAfterClose = await OverviewAtAsync(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)); // 15:30 sharp
        var wednesdayAfterClose = await OverviewAtAsync(new DateTimeOffset(2026, 10, 14, 10, 30, 0, TimeSpan.Zero));

        (tuesdayMorning.Bars.ExpectedSession, tuesdayMorning.Bars.DaysBehind).ShouldBe((new DateOnly(2026, 10, 8), 0));
        (tuesdayAfterClose.Bars.ExpectedSession, tuesdayAfterClose.Bars.DaysBehind, tuesdayAfterClose.Bars.Stale).ShouldBe((new DateOnly(2026, 10, 13), 1, true));
        (wednesdayAfterClose.Bars.ExpectedSession, wednesdayAfterClose.Bars.DaysBehind).ShouldBe((new DateOnly(2026, 10, 14), 2));
        (await InstrumentsAsync()).Items.Single().DaysBehind.ShouldBe(2);
    }

    [Fact]
    public async Task A_special_evening_session_closes_at_its_own_end()
    {
        await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(new DateOnly(2026, 10, 9), 1));
        await RunAsync(["csv", "aggregate"]);
        (await Client.PostJsonAsync("/api/data/calendar", new { date = "2026-10-12", kind = "special", sessionFrom = "18:15", sessionTo = "19:15" }, Ct)).EnsureSuccessStatusCode();

        var afternoon = await OverviewAtAsync(new DateTimeOffset(2026, 10, 12, 10, 30, 0, TimeSpan.Zero)); // 16:00 IST: past 15:30, but the evening session has not run
        var lastMinute = await OverviewAtAsync(new DateTimeOffset(2026, 10, 12, 13, 44, 0, TimeSpan.Zero)); // 19:14
        var closed = await OverviewAtAsync(new DateTimeOffset(2026, 10, 12, 13, 45, 0, TimeSpan.Zero)); // 19:15

        (afternoon.Bars.ExpectedSession, afternoon.Bars.DaysBehind).ShouldBe((new DateOnly(2026, 10, 9), 0));
        (lastMinute.Bars.ExpectedSession, lastMinute.Bars.DaysBehind).ShouldBe((new DateOnly(2026, 10, 9), 0));
        (closed.Bars.ExpectedSession, closed.Bars.DaysBehind, closed.Bars.Stale).ShouldBe((new DateOnly(2026, 10, 12), 1, true));
    }

    [Fact]
    public async Task A_saturday_special_session_counts_as_a_missing_session_once_it_has_closed()
    {
        await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(new DateOnly(2026, 10, 9), 1));
        await RunAsync(["csv", "aggregate"]);
        (await Client.PostJsonAsync("/api/data/calendar", new { date = "2026-10-10", kind = "special", sessionFrom = "09:15", sessionTo = "10:15" }, Ct)).EnsureSuccessStatusCode();

        var overview = await OverviewAtAsync(new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero)); // Sunday afternoon

        (overview.Bars.ExpectedSession, overview.Bars.DaysBehind).ShouldBe((new DateOnly(2026, 10, 10), 1));
    }

    private async Task<DataOverview> OverviewAtAsync(DateTimeOffset instant)
    {
        Factory.Clock.Pin(instant);
        await ResignInAsync();
        return await OverviewAsync();
    }

    private Task<string> StatusOfAsync(string date) => ScalarAsync<string>("SELECT status FROM market.trading_days WHERE date = @date::date", ("date", date));

    [Fact]
    public async Task NSEs_list_is_tried_once_a_day_after_a_job_and_its_holidays_are_confirmed()
    {
        Factory.MasterData.HolidayJson = FakeMasterData.HolidaysOf(("25-Dec-2026", "Christmas"));
        Factory.Clock.Pin(new DateTimeOffset(2026, 12, 1, 6, 30, 0, TimeSpan.Zero));
        await ResignInAsync();

        var first = await RunAsync(["aggregate"]);
        var second = await RunAsync(["aggregate"]);
        Factory.Clock.Pin(new DateTimeOffset(2026, 12, 2, 6, 30, 0, TimeSpan.Zero));
        await ResignInAsync();
        var nextDay = await RunAsync(["aggregate"]);

        (first.Status, second.Status, nextDay.Status).ShouldBe(("succeeded", "succeeded", "succeeded"));
        Factory.MasterData.HolidayRequests.Count().ShouldBe(2); // once on the 1st, once on the 2nd
        var holiday = (await CalendarAsync("?from=2026-12-01&to=2026-12-31")).Days.ShouldHaveSingleItem();
        (holiday.Date, holiday.Source, holiday.Status, holiday.Note).ShouldBe((new DateOnly(2026, 12, 25), "nse", "confirmed", "Christmas"));
        Factory.MasterData.HolidayRequests.ShouldAllBe(r => !r.Headers.ContainsKey("Cookie") && !r.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task NSE_refusing_the_daily_attempt_is_only_a_warning_of_the_job()
    {
        Factory.MasterData.HolidayOverride = _ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden);
        Factory.Clock.Pin(new DateTimeOffset(2026, 12, 3, 6, 30, 0, TimeSpan.Zero));
        await ResignInAsync();

        var job = await RunAsync(["aggregate"]);

        job.Status.ShouldBe("succeeded", job.Error);
        job.Warnings.ShouldBe(["Trading calendar: NSE's holiday list answered HTTP 403. Add the holidays by hand instead."]);
        (await CalendarAsync()).Days.ShouldBeEmpty();
        job.Aggregate.Status.ShouldBe("succeeded");
    }
}
