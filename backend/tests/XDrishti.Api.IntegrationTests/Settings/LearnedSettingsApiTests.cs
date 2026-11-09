using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.LearnedSettings;

namespace XDrishti.Api.IntegrationTests.Settings;

/// <summary>
/// AU-10 over HTTP and a real database: the owner approves, dismisses and undoes learned changes. A loosening, and the undo of a tightening, need a fresh password at any Auto state; a tightening needs only the session; the actor is the session owner, never the body;
/// a candidate that breaks the whole configuration is refused; the Essential settings show who and when and reset to default with a password.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LearnedSettingsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Bps = "Confidence:CostStress:TierABps"; // default 3, range 3..5, higher is safer
    private const string TierB = "Confidence:CostStress:TierBCutCrore"; // default 20, must stay below tier A (100)

    private static readonly string Hash = new('a', 64);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM config.learned_setting_changes WHERE undo_of IS NOT NULL");
        await ExecuteAsync(connection, "DELETE FROM config.learned_setting_changes");
        await ExecuteAsync(connection, "DELETE FROM platform.app_settings WHERE key IN ('auto.enabled', 'notices.failure')");
    }

    private async Task<long> ProposeAsync(string key, string oldValue, string newValue, string direction, string? autoApply = null)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ILearnedSettingStore>();
        var id = (await store.InsertProposedAsync(new NewLearnedChange(key, oldValue, newValue, direction, "learning", "{}", Hash, "paper fills", Guid.NewGuid().ToString("N"), null), Ct))!.Value;
        if (autoApply is not null)
        {
            await using var connection = await OpenAsync();
            await ExecuteAsync(connection, "INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('auto.enabled', 'true', 'owner:t@x.test', now()) ON CONFLICT (key) DO UPDATE SET value = 'true'");
            (await store.DecideAsync(id, "applied", autoApply, Ct)).ShouldBeTrue();
            await ExecuteAsync(connection, "DELETE FROM platform.app_settings WHERE key = 'auto.enabled'");
        }

        return id;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, string uri, object? body = null, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(uri, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        using var response = await (client ?? Client).SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<string> StateAsync(long id) => ScalarAsync<string>("SELECT state FROM config.learned_setting_changes WHERE id = @i", ("i", id));

    private static string Code(JsonElement body) => body.GetProperty("code").GetString()!;

    [Fact]
    public async Task Reading_needs_a_session_and_shows_the_guardrails_and_the_allow_list_with_auto_off()
    {
        (await SendAsync(HttpMethod.Get, "/api/settings/learned", client: Factory.CreateAnonymousClient())).Status.ShouldBe(HttpStatusCode.Unauthorized);
        await ProposeAsync(Bps, "3", "3.3", "tighten");

        var view = await SendAsync(HttpMethod.Get, "/api/settings/learned");

        view.Status.ShouldBe(HttpStatusCode.OK);
        view.Body.GetProperty("autoOn").GetBoolean().ShouldBeFalse();
        view.Body.GetProperty("weeklyCap").GetInt32().ShouldBe(3);
        view.Body.GetProperty("allowlist").GetArrayLength().ShouldBe(9);
        view.Body.GetProperty("allowlist").EnumerateArray().Select(a => a.GetProperty("key").GetString()).ShouldNotContain("Confidence:Loop:Level");
        var item = view.Body.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("direction").GetString().ShouldBe("tighten");
        item.GetProperty("approveNeedsPassword").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_tightening_proposal_is_approved_with_the_session_as_actor_and_a_loosening_needs_the_password()
    {
        var tighten = await ProposeAsync(Bps, "3", "3.3", "tighten");

        var approved = await SendAsync(HttpMethod.Post, $"/api/settings/learned/{tighten}/approve", new { actor = "owner:evil@example.test" });

        approved.Status.ShouldBe(HttpStatusCode.OK);
        approved.Body.GetProperty("state").GetString().ShouldBe("applied");
        approved.Body.GetProperty("decidedBy").GetString().ShouldStartWith("owner:");
        approved.Body.GetProperty("decidedBy").GetString()!.ShouldNotContain("evil");

        var loosen = await ProposeAsync(Bps, "3.3", "3.1", "loosen");
        var missing = await SendAsync(HttpMethod.Post, $"/api/settings/learned/{loosen}/approve", new { });
        var wrong = await SendAsync(HttpMethod.Post, $"/api/settings/learned/{loosen}/approve", new { password = "not-the-password" });

        missing.Status.ShouldBe(HttpStatusCode.Forbidden);
        Code(missing.Body).ShouldBe("auth.step_up_required");
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await StateAsync(loosen)).ShouldBe("proposed");

        var done = await SendAsync(HttpMethod.Post, $"/api/settings/learned/{loosen}/approve", new { password = Factory.OwnerPassword });

        done.Status.ShouldBe(HttpStatusCode.OK);
        done.Body.GetProperty("decidedBy").GetString().ShouldStartWith("owner:");
        (await StateAsync(loosen)).ShouldBe("applied");
    }

    [Fact]
    public async Task Undoing_an_automatic_tightening_needs_the_password_and_records_an_owner_loosening()
    {
        var id = await ProposeAsync(Bps, "3", "3.3", "tighten", autoApply: "auto");

        var without = await SendAsync(HttpMethod.Post, $"/api/settings/learned/{id}/undo", new { });
        var done = await SendAsync(HttpMethod.Post, $"/api/settings/learned/{id}/undo", new { password = Factory.OwnerPassword });

        without.Status.ShouldBe(HttpStatusCode.Forbidden);
        Code(without.Body).ShouldBe("auth.step_up_required");
        done.Status.ShouldBe(HttpStatusCode.OK);
        done.Body.GetProperty("direction").GetString().ShouldBe("loosen");
        done.Body.GetProperty("decidedBy").GetString().ShouldStartWith("owner:");
        done.Body.GetProperty("undoOf").GetInt64().ShouldBe(id);
        (await StateAsync(id)).ShouldBe("undone");
    }

    [Fact]
    public async Task A_candidate_that_breaks_the_whole_configuration_is_refused_and_stays_a_proposal()
    {
        var id = await ProposeAsync(TierB, "20", "100", "tighten"); // higher is safer, but B must stay below A (100)

        var result = await SendAsync(HttpMethod.Post, $"/api/settings/learned/{id}/approve", new { });

        Code(result.Body).ShouldBe("learned.config_invalid");
        result.Status.ShouldNotBe(HttpStatusCode.OK);
        (await StateAsync(id)).ShouldBe("proposed");
    }

    [Fact]
    public async Task Dismissing_needs_no_password_and_a_decided_change_cannot_be_decided_again()
    {
        var id = await ProposeAsync(Bps, "3", "3.3", "tighten");

        var first = await SendAsync(HttpMethod.Post, $"/api/settings/learned/{id}/dismiss");
        var again = await SendAsync(HttpMethod.Post, $"/api/settings/learned/{id}/dismiss");
        var approve = await SendAsync(HttpMethod.Post, $"/api/settings/learned/{id}/approve", new { });
        var missing = await SendAsync(HttpMethod.Post, "/api/settings/learned/999999/approve", new { });

        first.Status.ShouldBe(HttpStatusCode.OK);
        again.Status.ShouldBe(HttpStatusCode.Conflict);
        approve.Status.ShouldBe(HttpStatusCode.Conflict);
        missing.Status.ShouldBe(HttpStatusCode.NotFound);
        (await StateAsync(id)).ShouldBe("dismissed");
    }

    [Fact]
    public async Task The_essentials_show_who_and_when_and_reset_needs_the_password()
    {
        var before = await SendAsync(HttpMethod.Get, "/api/settings/essentials");
        before.Body.GetProperty("items").GetArrayLength().ShouldBe(5);
        before.Body.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "notices.failure").GetProperty("isDefault").GetBoolean().ShouldBeTrue();

        var noPassword = await SendAsync(HttpMethod.Put, "/api/settings/essentials/notices.failure", new { on = false });
        var off = await SendAsync(HttpMethod.Put, "/api/settings/essentials/notices.failure", new { on = false, password = Factory.OwnerPassword });

        noPassword.Status.ShouldBe(HttpStatusCode.Forbidden);
        off.Status.ShouldBe(HttpStatusCode.OK);
        var after = await SendAsync(HttpMethod.Get, "/api/settings/essentials");
        var row = after.Body.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "notices.failure");
        row.GetProperty("value").GetString().ShouldBe("false");
        row.GetProperty("isDefault").GetBoolean().ShouldBeFalse();
        row.GetProperty("changedBy").GetString().ShouldStartWith("owner:");
        row.GetProperty("changedAt").ValueKind.ShouldBe(JsonValueKind.String);

        (await SendAsync(HttpMethod.Post, "/api/settings/essentials/notices.failure/reset", new { })).Status.ShouldBe(HttpStatusCode.Forbidden);
        var reset = await SendAsync(HttpMethod.Post, "/api/settings/essentials/notices.failure/reset", new { password = Factory.OwnerPassword });
        reset.Status.ShouldBe(HttpStatusCode.OK);
        reset.Body.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        (await SendAsync(HttpMethod.Post, "/api/settings/essentials/risk.profile/reset", new { password = Factory.OwnerPassword })).Status.ShouldBe(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Put, "/api/settings/essentials/auto.enabled", new { on = true })).Status.ShouldBe(HttpStatusCode.Conflict);
    }
}
