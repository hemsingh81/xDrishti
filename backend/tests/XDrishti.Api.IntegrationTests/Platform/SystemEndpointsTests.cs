using System.Net;
using System.Net.Http.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Platform.SystemStatus;

namespace XDrishti.Api.IntegrationTests.Platform;

[Collection(ApiTestSuite.Name)]
public sealed class SystemEndpointsTests(ApiFactory factory)
{
    [Fact]
    public async Task Liveness_and_readiness_are_healthy_without_a_session()
    {
        var client = factory.CreateAnonymousClient();

        (await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Status_reports_database_with_timescale_and_no_pending_migrations()
    {
        var signedIn = await factory.SignInAsync(TestContext.Current.CancellationToken);

        var status = await signedIn.Client.GetFromJsonAsync<SystemStatusResponse>(new Uri("/api/system/status", UriKind.Relative), TestContext.Current.CancellationToken);

        status.ShouldNotBeNull();
        status.Database.IsConnected.ShouldBeTrue();
        status.Database.TimescaleVersion.ShouldNotBeNullOrEmpty();
        status.Database.PendingMigrations.ShouldBe(0);
        status.Application.Name.ShouldBe("xd-api");
    }

    [Fact]
    public async Task OpenApi_document_is_published_to_a_signed_in_owner()
    {
        var signedIn = await factory.SignInAsync(TestContext.Current.CancellationToken);

        var response = await signedIn.Client.GetAsync(new Uri("/api/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var document = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        document.ShouldContain("/api/system/status");
        document.ShouldContain("/api/auth/login");
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details_to_a_signed_in_owner()
    {
        var signedIn = await factory.SignInAsync(TestContext.Current.CancellationToken);

        var response = await signedIn.Client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Unknown_route_does_not_reveal_itself_to_an_anonymous_caller()
    {
        var response = await factory.CreateAnonymousClient().GetAsync(new Uri("/api/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
