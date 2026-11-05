using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Shadow;

namespace XDrishti.Api.IntegrationTests.Shadow;

/// <summary>
/// CF-08 part B, criterion 11 and the summary on a real database: the owner's switch is a fresh-password act on every change (ON and OFF), the actor is the session, a reason is needed, the rows are insert-only and audited without the reason, with the switch OFF (the default) the
/// writer and the grader idle, and the summary needs a session and says so when there are no rows. The two services are listed paused.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PaperShadowSwitchApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string SecretReason = "a-reason-that-must-never-reach-the-audit-feed";

    private static readonly Uri Settings = new("/api/paper/shadow/settings", UriKind.Relative);

    private static readonly Uri Summary = new("/api/paper/shadow/summary", UriKind.Relative);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM accounts.account_events WHERE event = 'ShadowRecordSwitched'");
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(string? password, bool enabled = true, string? reason = SecretReason, HttpClient? client = null)
    {
        using var response = await (client ?? Client).PutAsJsonAsync(Settings, new { enabled, reason, password, actor = "owner:evil@example.test" }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<long> RowsAsync(string where = "true") => ScalarAsync<long>($"SELECT count(*) FROM paper.shadow_settings WHERE {where}");

    [Fact]
    public async Task Without_a_session_both_routes_are_401()
    {
        using var read = await Factory.CreateAnonymousClient().GetAsync(Summary, Ct);
        using var write = await Factory.CreateAnonymousClient().PutAsJsonAsync(Settings, new { enabled = true, reason = "x", password = Factory.OwnerPassword }, Ct);

        (read.StatusCode, write.StatusCode).ShouldBe((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized));
        (await RowsAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-password")]
    public async Task A_missing_or_wrong_password_is_401_and_nothing_is_written(string? password)
    {
        var result = await PutAsync(password);

        result.Status.ShouldBe(HttpStatusCode.Unauthorized);
        result.Body.GetProperty("code").GetString().ShouldBe("auth.invalid_credentials");
        (await RowsAsync()).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'ShadowRecordSwitched'")).ShouldBe(0);
    }

    [Fact]
    public async Task The_right_password_and_a_reason_append_an_audited_row_for_the_owner_of_the_session_stamped_by_the_database()
    {
        var result = await PutAsync(Factory.OwnerPassword);

        result.Status.ShouldBe(HttpStatusCode.OK);
        result.Body.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        var id = result.Body.GetProperty("id").GetInt64();
        (await RowsAsync()).ShouldBe(1);
        (await RowsAsync("actor LIKE '%evil%'")).ShouldBe(0, "a body cannot name the actor");
        (await ScalarAsync<string>("SELECT actor FROM paper.shadow_settings")).ShouldStartWith("owner:");
        (await ScalarAsync<string>("SELECT reason FROM paper.shadow_settings")).ShouldBe(SecretReason);
        var detail = await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'ShadowRecordSwitched'");
        detail.ShouldBe($"switch={id} shadow_record=on");
        detail.ShouldNotContain(SecretReason);
    }

    [Fact]
    public async Task Every_change_asks_for_the_password_again_ON_and_OFF_and_the_newest_row_is_the_value_in_force()
    {
        var on = await PutAsync(Factory.OwnerPassword, enabled: true);
        var offWithout = await PutAsync(null, enabled: false);
        var off = await PutAsync(Factory.OwnerPassword, enabled: false);
        var onWithout = await PutAsync(null, enabled: true);

        on.Status.ShouldBe(HttpStatusCode.OK);
        offWithout.Status.ShouldBe(HttpStatusCode.Unauthorized);
        off.Status.ShouldBe(HttpStatusCode.OK);
        onWithout.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await ScalarAsync<string>("SELECT string_agg(enabled::text, ',' ORDER BY id) FROM paper.shadow_settings")).ShouldBe("true,false");
        (await Client.GetFromJsonAsync<JsonElement>(Summary, Ct)).GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_missing_reason_with_the_right_password_is_400_and_writes_nothing(string? reason)
    {
        var result = await PutAsync(Factory.OwnerPassword, reason: reason);

        result.Status.ShouldBe(HttpStatusCode.BadRequest);
        result.Body.GetProperty("code").GetString().ShouldBe("paper.shadow_reason_invalid");
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_change_without_the_CSRF_token_is_refused_even_with_a_session_and_the_right_password()
    {
        var client = Factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", Owner.CookieHeader);

        using var response = await client.PutAsJsonAsync(Settings, new { enabled = true, reason = "x", password = Factory.OwnerPassword }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.csrf_invalid");
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_post_is_not_a_route()
    {
        using var response = await Client.PostAsJsonAsync(Settings, new { enabled = true, reason = "x", password = Factory.OwnerPassword }, Ct);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
        (await RowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task With_the_switch_off_by_default_the_writer_and_the_grader_idle_and_the_summary_says_there_are_no_rows()
    {
        using var scope = Factory.Services.CreateScope();
        var write = await scope.ServiceProvider.GetRequiredService<ICommandHandler<WriteShadowCommand, ShadowWriteOutcome>>().HandleAsync(new WriteShadowCommand(null), Ct);
        var grade = await scope.ServiceProvider.GetRequiredService<ICommandHandler<GradeShadowCommand, ShadowGradeOutcome>>().HandleAsync(new GradeShadowCommand(), Ct);
        var view = await Client.GetFromJsonAsync<JsonElement>(Summary, Ct);

        write.Value.Enabled.ShouldBeFalse();
        grade.Value.Enabled.ShouldBeFalse();
        view.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        view.GetProperty("hasRows").GetBoolean().ShouldBeFalse();
        view.GetProperty("note").GetString().ShouldNotBeNull().ShouldContain("OFF");
        view.GetProperty("statement").GetString().ShouldBe("modelled fills; no queue, no latency");
        view.GetProperty("counts").GetProperty("edgeRaw").GetInt32().ShouldBe(0);
        view.GetProperty("edge").GetProperty("rhoIsPlaceholder").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task The_two_services_are_listed_paused_by_default()
    {
        foreach (var id in new[] { "shadow-write", "shadow-grade" })
        {
            var service = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/services/" + id, UriKind.Relative), Ct);

            service.GetProperty("id").GetString().ShouldBe(id);
            service.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        }
    }
}
