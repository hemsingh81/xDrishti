using System.Net;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>AU-17 over HTTP and a real database: Auto is OFF by default; turning it ON needs the fresh password (and is refused, naming each item, while a precondition is not a pass); turning it OFF needs a session only; the readout shows the chain and the preconditions.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AutoSwitchApiTests(ApiFactory factory) : DataTestBase(factory)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM planning.auto_scopes");
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, string uri, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(uri, UriKind.Relative)) { Content = body is null ? null : System.Net.Http.Json.JsonContent.Create(body) };
        using var response = await Client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task Auto_is_off_by_default_shows_the_chain_and_a_scope_that_is_missing_as_a_failing_precondition()
    {
        var read = await SendAsync(HttpMethod.Get, "/api/auto");
        using var anonymous = await Factory.CreateAnonymousClient().GetAsync(new Uri("/api/auto", UriKind.Relative), Ct);

        read.Status.ShouldBe(HttpStatusCode.OK);
        read.Body.GetProperty("on").GetBoolean().ShouldBeFalse();
        read.Body.GetProperty("chain").GetArrayLength().ShouldBe(6);
        read.Body.GetProperty("preconditions").EnumerateArray().Single(p => p.GetProperty("id").GetString() == "scope").GetProperty("status").GetString().ShouldBe("fail");
        read.Body.GetProperty("noOrderNotice").GetString()!.ShouldContain("never approves, arms or places an order");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Turning_on_needs_the_password_and_is_refused_while_a_precondition_is_not_a_pass_and_nothing_changes()
    {
        var noPassword = await SendAsync(HttpMethod.Post, "/api/auto/on", new { });
        var wrong = await SendAsync(HttpMethod.Post, "/api/auto/on", new { password = "not-the-password" });
        var refused = await SendAsync(HttpMethod.Post, "/api/auto/on", new { password = Factory.OwnerPassword });
        var after = await SendAsync(HttpMethod.Get, "/api/auto");
        var essentials = await SendAsync(HttpMethod.Put, "/api/settings/essentials/auto.enabled", new { on = true });

        noPassword.Status.ShouldBe(HttpStatusCode.Forbidden);
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        refused.Status.ShouldBe(HttpStatusCode.Conflict);
        refused.Body.GetProperty("title").GetString()!.ShouldContain("scope");
        after.Body.GetProperty("on").GetBoolean().ShouldBeFalse();
        after.Body.GetProperty("changes").GetArrayLength().ShouldBe(0);
        essentials.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Turning_off_needs_a_session_only_and_changes_nothing_when_it_is_off_already()
    {
        var off = await SendAsync(HttpMethod.Post, "/api/auto/off");
        using var anonymous = await Factory.CreateAnonymousClient().PostAsync(new Uri("/api/auto/off", UriKind.Relative), null, Ct);

        off.Status.ShouldBe(HttpStatusCode.OK);
        off.Body.GetProperty("on").GetBoolean().ShouldBeFalse();
        anonymous.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
