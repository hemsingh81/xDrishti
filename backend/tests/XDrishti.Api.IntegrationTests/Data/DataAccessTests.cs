using System.Net;
using System.Text;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>Every data endpoint needs a session; starting a job also needs the CSRF token but no step-up (it only reads the mounted folder and a token that is already stored).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataAccessTests(ApiFactory factory)
{
    private static readonly string Id = Guid.NewGuid().ToString();

    private static readonly string[] AggregateOnly = ["aggregate"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> AllEndpoints => new()
    {
        { "GET", "/api/data/overview" },
        { "GET", "/api/data/instruments" },
        { "GET", "/api/data/instruments?q=abb&status=behind&sort=-bars&page=2&pageSize=10" },
        { "GET", $"/api/data/instruments/{Id}/bars" },
        { "GET", $"/api/data/instruments/{Id}/bars?timeframe=1m&take=10" },
        { "POST", "/api/data/refresh" },
        { "GET", "/api/data/jobs" },
        { "GET", $"/api/data/jobs/{Id}" },
        { "GET", "/api/data/calendar" },
        { "GET", "/api/data/calendar?from=2026-10-01&to=2026-12-31" },
        { "POST", "/api/data/calendar" },
        { "POST", "/api/data/calendar/refresh" },
        { "PUT", "/api/data/calendar/2026-10-02/confirm" },
        { "DELETE", "/api/data/calendar/2026-10-02" },
        { "GET", "/api/data/gaps" },
        { "GET", "/api/data/notices" },
        { "GET", "/api/data/context" },
        { "GET", "/api/data/quality/overview" },
        { "GET", "/api/data/quality/instruments" },
        { "GET", "/api/data/quality/instruments?status=fail&q=abb&sort=-score&page=2&pageSize=10" },
        { "GET", $"/api/data/quality/instruments/{Id}" },
        { "GET", $"/api/data/quality/instruments/{Id}?differencesOnly=true&timeframe=15m" },
        { "GET", "/api/data/quality/runs" },
        { "GET", "/api/data/quality/gate" },
        { "GET", "/api/data/corporate-actions" },
        { "GET", $"/api/data/corporate-actions?status=suggested&instrumentId={Id}&page=2&pageSize=10" },
        { "POST", "/api/data/corporate-actions" },
        { "PUT", $"/api/data/corporate-actions/{Id}" },
        { "POST", $"/api/data/corporate-actions/{Id}/ignore" },
        { "POST", "/api/data/corporate-actions/detect" },
        { "POST", "/api/data/corporate-actions/import" },
        { "POST", "/api/data/corporate-actions/import?preview=true" },
        { "GET", $"/api/data/instruments/{Id}/bars?adjusted=true" },
        { "POST", "/api/data/quality/run" },
        { "POST", $"/api/data/quality/instruments/{Id}/refetch" },
        { "POST", $"/api/data/quality/instruments/{Id}/refresh-aggregates" },
        { "GET", "/api/data/csv/files" },
        { "GET", "/api/data/csv/files?q=abb&state=pending&page=2&pageSize=10" },
        { "POST", "/api/data/csv/preview" },
        { "POST", "/api/data/csv/files/ABB_1m.csv.gz/undo" },
    };

    public static TheoryData<string, string> CsvReviewChanges => new()
    {
        { "POST", "/api/data/csv/preview" },
        { "POST", "/api/data/csv/files/ABB_1m.csv.gz/undo" },
    };

    public static TheoryData<string, string> QualityStarts => new()
    {
        { "POST", "/api/data/quality/run" },
        { "POST", $"/api/data/quality/instruments/{Id}/refetch" },
        { "POST", $"/api/data/quality/instruments/{Id}/refresh-aggregates" },
    };

    public static TheoryData<string, string> CorporateActionChanges => new()
    {
        { "POST", "/api/data/corporate-actions" },
        { "PUT", $"/api/data/corporate-actions/{Id}" },
        { "POST", $"/api/data/corporate-actions/{Id}/ignore" },
        { "POST", "/api/data/corporate-actions/detect" },
        { "POST", "/api/data/corporate-actions/import" },
    };

    public static TheoryData<string, string> CalendarChanges => new()
    {
        { "POST", "/api/data/calendar" },
        { "POST", "/api/data/calendar/refresh" },
        { "PUT", "/api/data/calendar/2026-10-02/confirm" },
        { "DELETE", "/api/data/calendar/2026-10-02" },
    };

    private static HttpRequestMessage Request(string method, string path) => new(new HttpMethod(method), new Uri(path, UriKind.Relative))
    {
        Content = method is "POST" or "PUT" ? new StringContent("{}", Encoding.UTF8, "application/json") : null,
    };

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task Without_a_session_every_endpoint_answers_401(string method, string path)
    {
        using var response = await factory.CreateAnonymousClient().SendAsync(Request(method, path), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.unauthenticated");
        factory.DataJobs.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Starting_a_job_without_the_CSRF_token_is_refused_even_with_a_session()
    {
        var signedIn = await factory.SignInAsync(Ct);
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        using var response = await client.PostAsync("/api/data/refresh", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        factory.DataJobs.PendingCount.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(CsvReviewChanges))]
    public async Task A_preview_or_an_undo_without_the_CSRF_token_is_refused_and_with_it_needs_no_step_up(string method, string path)
    {
        var signedIn = await factory.SignInAsync(Ct);
        var withoutToken = factory.CreateAnonymousClient();
        withoutToken.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        using var refused = await withoutToken.SendAsync(Request(method, path), Ct);
        using var allowed = await signedIn.Client.SendAsync(Request(method, path), Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        allowed.StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound); // no files named / never loaded: not 403 (step-up) and not 401
        factory.DataJobs.PendingCount.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(CalendarChanges))]
    public async Task A_calendar_change_without_the_CSRF_token_is_refused_and_with_it_needs_no_step_up(string method, string path)
    {
        var signedIn = await factory.SignInAsync(Ct);
        var withoutToken = factory.CreateAnonymousClient();
        withoutToken.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        using var refused = await withoutToken.SendAsync(Request(method, path), Ct);
        using var allowed = await signedIn.Client.SendAsync(Request(method, path), Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        allowed.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden, "a step-up would answer 403 auth.step_up_required");
        allowed.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(CorporateActionChanges))]
    public async Task A_corporate_action_change_without_the_CSRF_token_is_refused_and_with_it_needs_no_step_up(string method, string path)
    {
        var signedIn = await factory.SignInAsync(Ct);
        var withoutToken = factory.CreateAnonymousClient();
        withoutToken.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        using var refused = await withoutToken.SendAsync(Request(method, path), Ct);
        using var allowed = await signedIn.Client.SendAsync(Request(method, path), Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        allowed.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden, "a step-up would answer 403 auth.step_up_required");
        allowed.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(QualityStarts))]
    public async Task Starting_a_quality_run_or_a_fix_action_without_the_CSRF_token_is_refused_and_with_it_needs_no_step_up(string method, string path)
    {
        var signedIn = await factory.SignInAsync(Ct);
        var withoutToken = factory.CreateAnonymousClient();
        withoutToken.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);
        using var unknown = new StringContent($$"""{"instrumentId":"{{Id}}"}""", Encoding.UTF8, "application/json");

        using var refused = await withoutToken.SendAsync(Request(method, path), Ct);
        using var allowed = await signedIn.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = unknown }, Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        allowed.StatusCode.ShouldBe(HttpStatusCode.NotFound, "an unknown instrument: not 403 (step-up) and not 401");
        factory.DataJobs.PendingCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("/api/data/overview")]
    [InlineData("/api/data/instruments")]
    [InlineData("/api/data/jobs")]
    [InlineData("/api/data/calendar")]
    [InlineData("/api/data/gaps")]
    [InlineData("/api/data/notices")]
    [InlineData("/api/data/context")]
    [InlineData("/api/data/quality/overview")]
    [InlineData("/api/data/quality/instruments")]
    [InlineData("/api/data/quality/runs")]
    [InlineData("/api/data/quality/gate")]
    [InlineData("/api/data/corporate-actions")]
    [InlineData("/api/data/csv/files")]
    public async Task With_a_session_the_reads_work_without_step_up(string path)
    {
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await signedIn.Client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task With_a_session_and_the_CSRF_token_a_job_starts_without_step_up()
    {
        await factory.ResetInstrumentsAsync();
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await signedIn.Client.PostJsonAsync("/api/data/refresh", new { steps = AggregateOnly }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        factory.DataJobs.Clear();
        await factory.ResetInstrumentsAsync();
    }
}
