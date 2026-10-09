using System.Net;
using System.Net.Http.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Platform.SystemStatus;

namespace XDrishti.Api.IntegrationTests.Platform;

[Collection(ApiTestSuite.Name)]
public sealed class SystemEndpointsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Liveness_and_readiness_are_healthy()
    {
        (await _client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Status_reports_database_with_timescale_and_no_pending_migrations()
    {
        var status = await _client.GetFromJsonAsync<SystemStatusResponse>(new Uri("/api/system/status", UriKind.Relative), TestContext.Current.CancellationToken);

        status.ShouldNotBeNull();
        status.Database.IsConnected.ShouldBeTrue();
        status.Database.TimescaleVersion.ShouldNotBeNullOrEmpty();
        status.Database.PendingMigrations.ShouldBe(0);
        status.Application.Name.ShouldBe("xd-api");
    }

    [Fact]
    public async Task OpenApi_document_is_published()
    {
        var response = await _client.GetAsync(new Uri("/api/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("/api/system/status");
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        var response = await _client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }
}
