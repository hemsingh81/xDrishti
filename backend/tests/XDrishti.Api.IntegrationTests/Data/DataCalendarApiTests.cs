using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Calendar;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>The trading calendar endpoints against a real database (ADR 0017 §1, P1-11a): adding, confirming and removing days, NSE's list, and what the overview says.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataCalendarApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private async Task<CalendarView> CalendarAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/data/calendar" + query, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync<CalendarView>(Ct);
    }

    private async Task<HttpResponseMessage> AddAsync(object body) => await Client.PostJsonAsync("/api/data/calendar", body, Ct);

    private async Task InsertAsync(string date, string kind, string source, string status, string? from = null, string? to = null)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO market.trading_days (date, kind, session_from, session_to, source, status, note, updated_at) VALUES (@date::date, @kind, @from::time, @to::time, @source, @status, 'seen', now())",
            ("date", date), ("kind", kind), ("from", (object?)from ?? DBNull.Value), ("to", (object?)to ?? DBNull.Value), ("source", source), ("status", status));
    }

    private Task<string> StatusOfAsync(string date) => ScalarAsync<string>("SELECT status FROM market.trading_days WHERE date = @date::date", ("date", date));

    private Task<long> CountAsync(string date) => ScalarAsync<long>("SELECT count(*) FROM market.trading_days WHERE date = @date::date", ("date", date));

    [Fact]
    public async Task A_fresh_calendar_is_empty_and_the_overview_knows_no_holiday()
    {
        var calendar = await CalendarAsync();
        var overview = await OverviewAsync();

        (calendar.From, calendar.To).ShouldBe((new DateOnly(2026, 10, 12), new DateOnly(2027, 10, 12)));
        calendar.Days.ShouldBeEmpty();
        calendar.Proposals.ShouldBeEmpty();
        calendar.NextHoliday.ShouldBeNull();
        (overview.NextHoliday, overview.CalendarProposals).ShouldBe((null, 0));
    }

    [Fact]
    public async Task The_wire_format_of_the_calendar_and_of_the_new_overview_fields_is_camelCase_with_plain_dates_and_times()
    {
        await InsertAsync("2026-11-08", "special", "observed", "proposed", "18:15", "19:15");
        using var added = await AddAsync(new { date = "2026-10-20", kind = "holiday", note = "Diwali" });
        added.EnsureSuccessStatusCode();

        using var calendar = JsonDocument.Parse(await (await Client.GetAsync("/api/data/calendar", Ct)).Content.ReadAsStringAsync(Ct));
        using var overview = JsonDocument.Parse(await (await Client.GetAsync("/api/data/overview", Ct)).Content.ReadAsStringAsync(Ct));

        calendar.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["from", "to", "days", "proposals", "nextHoliday"]);
        var holiday = calendar.RootElement.GetProperty("days")[0];
        holiday.EnumerateObject().Select(p => p.Name).ShouldBe(["date", "kind", "sessionFrom", "sessionTo", "minutes", "source", "status", "note", "updatedAt", "replaces"]);
        (holiday.GetProperty("date").GetString(), holiday.GetProperty("kind").GetString(), holiday.GetProperty("sessionFrom").ValueKind, holiday.GetProperty("source").GetString(), holiday.GetProperty("status").GetString())
            .ShouldBe(("2026-10-20", "holiday", JsonValueKind.Null, "manual", "confirmed"));
        var proposal = calendar.RootElement.GetProperty("proposals")[0];
        (proposal.GetProperty("date").GetString(), proposal.GetProperty("sessionFrom").GetString(), proposal.GetProperty("sessionTo").GetString(), proposal.GetProperty("minutes").GetInt32())
            .ShouldBe(("2026-11-08", "18:15", "19:15", 60));
        overview.RootElement.EnumerateObject().Select(p => p.Name).ShouldContain("nextHoliday");
        overview.RootElement.EnumerateObject().Select(p => p.Name).ShouldContain("calendarProposals");
        overview.RootElement.GetProperty("nextHoliday").GetProperty("date").GetString().ShouldBe("2026-10-20");
        overview.RootElement.GetProperty("calendarProposals").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Adding_a_holiday_answers_201_replacing_it_answers_200_and_the_overview_shows_the_next_one()
    {
        using var created = await AddAsync(new { date = "2026-10-20", kind = "holiday", note = "Diwali" });
        using var replaced = await AddAsync(new { date = "2026-10-20", kind = "holiday", note = "Diwali Laxmi Pujan" });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.Location!.ToString().ShouldBe("/api/data/calendar?from=2026-10-20&to=2026-10-20");
        var day = (await created.Content.ReadFromJsonAsync<CalendarDayView>(Ct))!;
        (day.Date, day.Kind, day.Source, day.Status, day.Note, day.SessionFrom).ShouldBe((new DateOnly(2026, 10, 20), "holiday", "manual", "confirmed", "Diwali", null));
        replaced.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replaced.ReadAsync<CalendarDayView>(Ct)).Note.ShouldBe("Diwali Laxmi Pujan");
        (await CalendarAsync()).Days.ShouldHaveSingleItem().Note.ShouldBe("Diwali Laxmi Pujan");
        (await OverviewAsync()).NextHoliday!.Date.ShouldBe(new DateOnly(2026, 10, 20));
    }

    [Fact]
    public async Task Adding_a_special_session_stores_its_window_and_a_missing_window_means_the_regular_session()
    {
        using var evening = await AddAsync(new { date = "2026-11-08", kind = "special", sessionFrom = "18:15", sessionTo = "19:15", note = "Muhurat" });
        using var saturday = await AddAsync(new { date = "2026-11-14", kind = "special" });

        var calendar = await CalendarAsync();

        calendar.Days.Select(d => (d.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), d.SessionFrom, d.SessionTo, d.Minutes)).ShouldBe(
            [("2026-11-08", "18:15", "19:15", 60), ("2026-11-14", "09:15", "15:30", 375)]);
        calendar.NextHoliday.ShouldBeNull(); // a special session is not a holiday
        (await ScalarAsync<TimeOnly>("SELECT session_from FROM market.trading_days WHERE date = '2026-11-08'")).ShouldBe(new TimeOnly(18, 15));
    }

    [Theory]
    [InlineData("2026-11-14", "holiday", null, null, "date")]
    [InlineData("nope", "holiday", null, null, "date")]
    [InlineData("2026-10-20", "closed", null, null, "kind")]
    [InlineData("2026-10-20", "holiday", "09:15", "15:30", "sessionFrom")]
    [InlineData("2026-11-14", "special", "12:00", "11:00", "sessionTo")]
    [InlineData("2026-11-14", "special", "09:15", null, "sessionTo")]
    public async Task A_bad_entry_answers_400_data_invalid_with_the_field_and_stores_nothing(string date, string kind, string? from, string? to, string field)
    {
        using var response = await AddAsync(new { date, kind, sessionFrom = from, sessionTo = to });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("data.invalid");
        (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldContain(field);
        (await ScalarAsync<long>("SELECT count(*) FROM market.trading_days")).ShouldBe(0);
    }

    [Fact]
    public async Task A_window_that_cannot_be_read_and_a_missing_body_are_refused_too()
    {
        using var empty = await Client.PostJsonAsync("/api/data/calendar", new { }, Ct);
        using var badWindow = await Client.GetAsync("/api/data/calendar?from=2026-10-10&to=2026-10-01", Ct);

        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await empty.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["date", "kind"], ignoreOrder: true);
        badWindow.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await badWindow.ReadProblemAsync(Ct)).Code.ShouldBe("data.invalid");
    }

    [Fact]
    public async Task Confirming_a_proposal_makes_it_a_holiday_and_asking_again_changes_nothing()
    {
        await InsertAsync("2026-10-20", "holiday", "observed", "proposed");
        (await OverviewAsync()).CalendarProposals.ShouldBe(1);

        using var first = await Client.PutJsonAsync("/api/data/calendar/2026-10-20/confirm", new { }, Ct);
        using var again = await Client.PutJsonAsync("/api/data/calendar/2026-10-20/confirm", new { }, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var day = await first.ReadAsync<CalendarDayView>(Ct);
        (day.Status, day.Source, day.Kind).ShouldBe(("confirmed", "observed", "holiday"));
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        var overview = await OverviewAsync();
        (overview.CalendarProposals, overview.NextHoliday!.Date).ShouldBe((0, new DateOnly(2026, 10, 20)));
    }

    [Theory]
    [InlineData("2026-10-21", HttpStatusCode.NotFound, "data.calendar_day_not_found")]
    [InlineData("2026-10-22", HttpStatusCode.NotFound, "data.calendar_day_not_found")] // dismissed
    [InlineData("20-10-2026", HttpStatusCode.BadRequest, "data.invalid")]
    public async Task Confirming_a_missing_dismissed_or_unreadable_date_is_refused(string date, HttpStatusCode status, string code)
    {
        await InsertAsync("2026-10-22", "holiday", "observed", "dismissed");

        using var response = await Client.PutJsonAsync($"/api/data/calendar/{date}/confirm", new { }, Ct);

        response.StatusCode.ShouldBe(status);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe(code);
    }

    [Fact]
    public async Task Removing_the_owners_own_day_deletes_it()
    {
        (await AddAsync(new { date = "2026-10-20", kind = "holiday" })).EnsureSuccessStatusCode();

        using var response = await Client.DeleteAsync("/api/data/calendar/2026-10-20", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CountAsync("2026-10-20")).ShouldBe(0);
    }

    [Fact]
    public async Task Dismissing_a_proposal_keeps_a_dismissed_row_that_is_neither_listed_nor_counted_and_removing_that_deletes_it()
    {
        await InsertAsync("2026-10-20", "holiday", "observed", "proposed");
        await InsertAsync("2026-10-21", "holiday", "nse", "confirmed");

        using var dismissed = await Client.DeleteAsync("/api/data/calendar/2026-10-20", Ct);
        using var unlisted = await Client.DeleteAsync("/api/data/calendar/2026-10-21", Ct);

        dismissed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        unlisted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StatusOfAsync("2026-10-20"), await StatusOfAsync("2026-10-21")).ShouldBe(("dismissed", "dismissed"));
        var calendar = await CalendarAsync();
        calendar.Days.ShouldBeEmpty();
        calendar.Proposals.ShouldBeEmpty();
        calendar.NextHoliday.ShouldBeNull();
        (await OverviewAsync()).CalendarProposals.ShouldBe(0);

        using var gone = await Client.DeleteAsync("/api/data/calendar/2026-10-20", Ct);

        gone.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CountAsync("2026-10-20")).ShouldBe(0);
    }

    [Theory]
    [InlineData("2026-10-20", HttpStatusCode.NotFound, "data.calendar_day_not_found")]
    [InlineData("tomorrow", HttpStatusCode.BadRequest, "data.invalid")]
    public async Task Removing_a_date_without_a_row_or_one_that_cannot_be_read_is_refused(string date, HttpStatusCode status, string code)
    {
        using var response = await Client.DeleteAsync($"/api/data/calendar/{date}", Ct);

        response.StatusCode.ShouldBe(status);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe(code);
    }

    [Fact]
    public async Task The_calendar_changes_are_audited_without_the_free_text_note()
    {
        (await AddAsync(new { date = "2026-10-20", kind = "holiday", note = "private note" })).EnsureSuccessStatusCode();
        await InsertAsync("2026-10-21", "holiday", "observed", "proposed");
        (await Client.PutJsonAsync("/api/data/calendar/2026-10-21/confirm", new { }, Ct)).EnsureSuccessStatusCode();
        (await Client.DeleteAsync("/api/data/calendar/2026-10-20", Ct)).EnsureSuccessStatusCode();

        await using var connection = await OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT event, detail, actor FROM accounts.account_events WHERE event LIKE 'Calendar%' ORDER BY at, id", connection);
        var events = new List<(string, string?, string)>();
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                events.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
            }
        }

        events.Select(e => (e.Item1, e.Item2)).ShouldBe(
            [("CalendarDayAdded", "Holiday 2026-10-20 (manual)"), ("CalendarDayConfirmed", "Holiday 2026-10-21 (observed)"), ("CalendarDayRemoved", "Holiday 2026-10-20 (manual)")]);
        events.ShouldAllBe(e => e.Item3 == ApiFactory.OwnerEmail);
        events.ShouldAllBe(e => e.Item2 == null || !e.Item2.Contains("private", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_window_of_days_is_chosen_by_from_and_to()
    {
        (await AddAsync(new { date = "2026-10-20", kind = "holiday" })).EnsureSuccessStatusCode();
        (await AddAsync(new { date = "2026-12-25", kind = "holiday" })).EnsureSuccessStatusCode();

        var october = await CalendarAsync("?from=2026-10-01&to=2026-10-31");
        var later = await CalendarAsync("?from=2026-11-01");

        october.Days.ShouldHaveSingleItem().Date.ShouldBe(new DateOnly(2026, 10, 20));
        later.Days.ShouldHaveSingleItem().Date.ShouldBe(new DateOnly(2026, 12, 25));
        october.NextHoliday!.Date.ShouldBe(new DateOnly(2026, 10, 20)); // the next holiday does not depend on the window
    }

    [Fact]
    public async Task Refreshing_from_NSE_adds_the_listed_weekday_holidays_confirmed_and_sends_one_plain_request()
    {
        Factory.MasterData.HolidayJson = FakeMasterData.HolidaysOf(("20-Oct-2026", "Diwali"), ("25-Dec-2026", "Christmas"), ("24-Oct-2026", "a Saturday"));

        using var response = await Client.PostJsonAsync("/api/data/calendar/refresh", new { }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.ReadAsync<CalendarRefreshResult>(Ct);
        (result.Succeeded, result.Added, result.Updated, result.Unchanged, result.Skipped).ShouldBe((true, 2, 0, 0, 1));
        var request = Factory.MasterData.HolidayRequests.ShouldHaveSingleItem();
        (request.Method, request.Uri.ToString()).ShouldBe((HttpMethod.Get, "https://www.nseindia.com/api/holiday-master?type=trading"));
        request.Headers.Keys.ShouldNotContain(k => k == "Cookie" || k == "Authorization" || k == "access-token");
        var calendar = await CalendarAsync();
        calendar.Days.Select(d => (d.Note, d.Source, d.Status)).ShouldBe([("Diwali", "nse", "confirmed"), ("Christmas", "nse", "confirmed")]);
        (await OverviewAsync()).NextHoliday!.Date.ShouldBe(new DateOnly(2026, 10, 20));

        using var again = await Client.PostJsonAsync("/api/data/calendar/refresh", new { }, Ct);

        var second = await again.ReadAsync<CalendarRefreshResult>(Ct);
        (second.Added, second.Unchanged).ShouldBe((0, 2));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "NSE's holiday list answered HTTP 403.")]
    [InlineData(HttpStatusCode.Unauthorized, "NSE's holiday list answered HTTP 401.")]
    public async Task NSE_refusing_the_request_is_an_answer_not_an_error_and_changes_nothing(HttpStatusCode refusal, string message)
    {
        Factory.MasterData.HolidayOverride = _ => new HttpResponseMessage(refusal);

        using var response = await Client.PostJsonAsync("/api/data/calendar/refresh", new { }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.ReadAsync<CalendarRefreshResult>(Ct);
        (result.Succeeded, result.Message, result.Added).ShouldBe((false, message + " Add the holidays by hand instead.", 0));
        (await ScalarAsync<long>("SELECT count(*) FROM market.trading_days")).ShouldBe(0);
    }

    [Fact]
    public async Task A_list_that_is_not_the_expected_json_is_an_answer_too_and_a_manual_add_still_works()
    {
        Factory.MasterData.HolidayJson = "<html>Access Denied</html>";

        using var response = await Client.PostJsonAsync("/api/data/calendar/refresh", new { }, Ct);
        using var added = await AddAsync(new { date = "2026-10-20", kind = "holiday" });

        var result = await response.ReadAsync<CalendarRefreshResult>(Ct);
        (response.StatusCode, result.Succeeded, result.Message).ShouldBe((HttpStatusCode.OK, false, "NSE's holiday list is not valid JSON. Add the holidays by hand instead."));
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task What_the_owner_decided_survives_a_refresh_from_NSE()
    {
        (await AddAsync(new { date = "2026-10-20", kind = "special", sessionFrom = "09:15", sessionTo = "10:15", note = "mine" })).EnsureSuccessStatusCode();
        await InsertAsync("2026-12-25", "holiday", "nse", "dismissed");
        Factory.MasterData.HolidayJson = FakeMasterData.HolidaysOf(("20-Oct-2026", "Diwali"), ("25-Dec-2026", "Christmas"));

        using var response = await Client.PostJsonAsync("/api/data/calendar/refresh", new { }, Ct);

        var result = await response.ReadAsync<CalendarRefreshResult>(Ct);
        (result.Added, result.Skipped).ShouldBe((0, 2));
        (await ScalarAsync<string>("SELECT kind FROM market.trading_days WHERE date = '2026-10-20'")).ShouldBe("special");
        (await StatusOfAsync("2026-12-25")).ShouldBe("dismissed");
    }
}
