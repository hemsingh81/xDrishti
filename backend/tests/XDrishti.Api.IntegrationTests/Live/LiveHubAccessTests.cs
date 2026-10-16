using System.Net;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Live;

/// <summary>Who may open the SignalR hub <c>/hubs/events</c> (ADR 0024 §7, P2-10a criterion 1): the owner's session only, from the same origin, over WebSockets.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LiveHubAccessTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_session_the_negotiation_is_401_and_the_websocket_is_refused()
    {
        using var anonymous = factory.CreateAnonymousClient();

        using var negotiate = await anonymous.PostAsync("/hubs/events/negotiate?negotiateVersion=1", Ct);
        var refused = await Should.ThrowAsync<Exception>(async () => await HubSocket.ConnectAsync(factory, null, null, Ct));

        negotiate.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await negotiate.ReadProblemAsync(Ct)).Code.ShouldNotBeNullOrEmpty();
        refused.Message.ShouldContain("401");
    }

    [Fact]
    public async Task A_cookie_that_is_not_a_session_is_refused_like_none()
    {
        var refused = await Should.ThrowAsync<Exception>(async () => await HubSocket.ConnectAsync(factory, "xd.sid=not-a-session", null, Ct));

        refused.Message.ShouldContain("401");
    }

    [Fact]
    public async Task The_signed_in_owner_connects_and_is_sent_the_feed_status_at_once()
    {
        var owner = await factory.SignInAsync(Ct);

        await using var hub = await HubSocket.ConnectAsync(factory, owner, Ct);
        var status = await hub.NextEventAsync("feedStatus", Ct);

        status.Argument.TryGetProperty("state", out _).ShouldBeTrue();
        status.Argument.TryGetProperty("subscribed", out _).ShouldBeTrue();
        status.Argument.TryGetProperty("lastTickAt", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task A_handshake_from_another_origin_is_refused_with_403_and_the_same_origin_is_accepted()
    {
        var owner = await factory.SignInAsync(Ct);

        var other = await Should.ThrowAsync<Exception>(async () => await HubSocket.ConnectAsync(factory, owner.CookieHeader, "http://evil.example", Ct));
        var otherPort = await Should.ThrowAsync<Exception>(async () => await HubSocket.ConnectAsync(factory, owner.CookieHeader, "http://localhost:5173", Ct));
        var otherScheme = await Should.ThrowAsync<Exception>(async () => await HubSocket.ConnectAsync(factory, owner.CookieHeader, "https://localhost", Ct));
        await using var same = await HubSocket.ConnectAsync(factory, owner.CookieHeader, "http://localhost", Ct);

        other.Message.ShouldContain("403");
        otherPort.Message.ShouldContain("403");
        otherScheme.Message.ShouldContain("403");
        (await same.NextEventAsync("feedStatus", Ct)).Target.ShouldBe("feedStatus");
    }

    [Fact]
    public async Task A_negotiation_from_another_origin_is_403_even_with_a_session()
    {
        var owner = await factory.SignInAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/events/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", "http://evil.example");

        using var response = await owner.Client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("hub.origin_forbidden");
    }

    [Fact]
    public async Task The_negotiation_is_a_post_that_needs_the_csrf_token_and_offers_only_websockets()
    {
        var owner = await factory.SignInAsync(Ct);
        using var withoutToken = factory.CreateAnonymousClient();
        withoutToken.DefaultRequestHeaders.Add("Cookie", owner.CookieHeader);

        using var refused = await withoutToken.PostAsync("/hubs/events/negotiate?negotiateVersion=1", Ct);
        using var accepted = await owner.Client.PostAsync("/hubs/events/negotiate?negotiateVersion=1", Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var offer = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync(Ct));
        offer.RootElement.GetProperty("availableTransports").EnumerateArray().Select(t => t.GetProperty("transport").GetString()).ShouldBe(["WebSockets"]);
    }

    [Fact]
    public async Task The_live_status_endpoint_needs_a_session()
    {
        using var anonymous = factory.CreateAnonymousClient();

        using var response = await anonymous.GetAsync("/api/live/status", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
