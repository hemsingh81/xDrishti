using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Filings;
using static XDrishti.Api.IntegrationTests.Filings.CalendarTestKit;

namespace XDrishti.Api.IntegrationTests.Filings;

/// <summary>P15-04 criterion 9 end to end: GET /api/calendar/results needs a session, answers each state, carries no file content and no estimate, and refuses a bad query with calendar.invalid_query.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class CalendarApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string[] AllowedNames =
    [
        "from", "to", "asOf", "meetings", "truncated", "instrumentsWithoutEntry", "symbol", "instrumentId", "periodEnd", "meetingDate", "eventSession", "days", "intimatedAt", "revision", "count", "symbols",
    ];

    private async Task StoreAsync(NewMeeting meeting)
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICalendarStore>().InsertAsync(meeting, Ct);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone());
    }

    private static IEnumerable<string> Names(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var inner in Names(property.Value))
                {
                    yield return inner;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray().SelectMany(Names))
            {
                yield return item;
            }
        }
    }

    [Fact]
    public async Task Without_a_session_the_route_is_401()
    {
        using var response = await Factory.CreateAnonymousClient().GetAsync(new Uri("/api/calendar/results", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Nothing_stored_is_an_empty_list_and_the_tracked_instruments_are_listed_without_an_entry()
    {
        await SeedAsync("RELIANCE");
        await SeedAsync("TCS");
        await SeedAsync("OLDCO", retired: true);

        var (status, body) = await GetAsync("/api/calendar/results");

        status.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("meetings").GetArrayLength().ShouldBe(0);
        body.GetProperty("truncated").GetBoolean().ShouldBeFalse();
        body.GetProperty("instrumentsWithoutEntry").GetProperty("count").GetInt32().ShouldBe(2);
        body.GetProperty("instrumentsWithoutEntry").GetProperty("symbols").EnumerateArray().Select(s => s.GetString()).ShouldBe(["RELIANCE", "TCS"]);
        (body.GetProperty("from").GetString(), body.GetProperty("to").GetString()).ShouldBe(("2026-10-12", "2026-12-10"));
        Names(body).Distinct().ShouldBeSubsetOf(AllowedNames);
    }

    [Fact]
    public async Task The_visible_upcoming_meetings_are_listed_with_days_revision_and_the_time_they_were_published()
    {
        var reliance = await SeedAsync("RELIANCE");
        var tcs = await SeedAsync("TCS");
        var infy = await SeedAsync("INFY");
        await StoreAsync(Meeting(reliance, "a", new DateOnly(2026, 10, 14), At(9, 28, 11)));
        await StoreAsync(Meeting(reliance, "b", new DateOnly(2026, 10, 21), At(10, 2, 9), "rescheduled", period: Q1));
        await StoreAsync(Meeting(tcs, "c", new DateOnly(2026, 10, 10), At(9, 25, 10)));
        await StoreAsync(Meeting(tcs, "d", new DateOnly(2026, 10, 13), At(10, 20, 10), "rescheduled"));
        await StoreAsync(Meeting(infy, "e", new DateOnly(2026, 10, 15), At(10, 1, 10), "cancelled"));

        var (status, body) = await GetAsync("/api/calendar/results?from=2026-10-12&to=2026-10-31&asOf=2026-10-12T10:00:00%2B05:30");

        status.ShouldBe(HttpStatusCode.OK);
        var meetings = body.GetProperty("meetings").EnumerateArray().ToList();
        meetings.Select(m => (m.GetProperty("symbol").GetString(), m.GetProperty("meetingDate").GetString(), m.GetProperty("eventSession").GetString(), m.GetProperty("days").GetInt32(), m.GetProperty("revision").GetInt32()))
            .ShouldBe([("TCS", "2026-10-10", "2026-10-12", 0, 1), ("RELIANCE", "2026-10-21", "2026-10-21", 7, 2)]);
        meetings[1].GetProperty("intimatedAt").GetDateTimeOffset().ShouldBe(At(10, 2, 9));
        meetings[0].GetProperty("periodEnd").GetString().ShouldBe("2026-06-30");
        body.GetProperty("instrumentsWithoutEntry").GetProperty("symbols").EnumerateArray().Select(s => s.GetString()).ShouldBe(["INFY"]);
        Names(body).Distinct().ShouldBeSubsetOf(AllowedNames);
    }

    [Fact]
    public async Task A_meeting_published_after_asOf_is_not_shown_and_the_range_filters_by_event_session()
    {
        var reliance = await SeedAsync("RELIANCE");
        await StoreAsync(Meeting(reliance, "a", new DateOnly(2026, 10, 14), At(10, 5, 11)));

        var early = (await GetAsync("/api/calendar/results?asOf=2026-10-05T11:04:59%2B05:30")).Body;
        var inside = (await GetAsync("/api/calendar/results?asOf=2026-10-05T11:05:00%2B05:30&from=2026-10-14&to=2026-10-14")).Body;
        var outside = (await GetAsync("/api/calendar/results?asOf=2026-10-05T11:05:00%2B05:30&from=2026-10-15&to=2026-10-30")).Body;

        early.GetProperty("meetings").GetArrayLength().ShouldBe(0);
        early.GetProperty("instrumentsWithoutEntry").GetProperty("count").GetInt32().ShouldBe(1);
        inside.GetProperty("meetings").EnumerateArray().Single().GetProperty("days").GetInt32().ShouldBe(7);
        outside.GetProperty("meetings").GetArrayLength().ShouldBe(0);
        outside.GetProperty("instrumentsWithoutEntry").GetProperty("count").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData("/api/calendar/results?from=2026-01-01&to=2026-12-31")]
    [InlineData("/api/calendar/results?from=2026-10-10&to=2026-10-01")]
    [InlineData("/api/calendar/results?from=yesterday")]
    [InlineData("/api/calendar/results?asOf=2026-10-12T10:00:00")]
    [InlineData("/api/calendar/results?asOf=2026-10-12")]
    [InlineData("/api/calendar/results?asOf=1999-01-01T00:00:00Z")]
    [InlineData("/api/calendar/results?symbol=RELIANCE")]
    [InlineData("/api/calendar/results?from=2026-10-12&limit=5")]
    public async Task A_bad_query_is_400_calendar_invalid_query(string path)
    {
        var (status, body) = await GetAsync(path);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().ShouldBe("calendar.invalid_query");
    }
}
