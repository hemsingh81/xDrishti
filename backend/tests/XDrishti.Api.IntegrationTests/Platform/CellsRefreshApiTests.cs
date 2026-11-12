using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Auth;

namespace XDrishti.Api.IntegrationTests.Platform;

/// <summary>LC-01 over HTTP and a real database (ACs 1 and 2, 9, and the ledger kind): the refresh service is paused and says it needs a password to resume; a missing or wrong password is 401 and counts; a fresh password resumes it with an audit row; it then shows as a paper blocker; the ledger takes the new trial kind and counts it as a look.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class CellsRefreshApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private async Task<JsonElement> ServiceAsync(string id) => await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/services/{id}", UriKind.Relative), Ct);

    private async Task<(HttpStatusCode Status, JsonElement Body)> ResumeAsync(string id, object? body)
    {
        using var response = await Client.PostAsJsonAsync(new Uri($"/api/services/{id}/resume", UriKind.Relative), body ?? new { }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task The_refresh_is_paused_at_first_start_and_says_it_needs_a_password_while_other_services_do_not()
    {
        var refresh = await ServiceAsync("cells-refresh");
        var weekly = await ServiceAsync("weekly");

        refresh.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        refresh.GetProperty("resumeNeedsPassword").GetBoolean().ShouldBeTrue();
        weekly.GetProperty("resumeNeedsPassword").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_missing_password_is_403_counting_nothing_and_a_wrong_one_is_401_and_the_service_stays_paused()
    {
        var before = await Factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().CountAsync(e => e.Event == AuditEventType.StepUpFailed, Ct));
        var none = await ResumeAsync("cells-refresh", null);
        var countedAfterNone = await Factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().CountAsync(e => e.Event == AuditEventType.StepUpFailed, Ct));
        var wrong = await ResumeAsync("cells-refresh", new { password = "not-the-password" });

        none.Status.ShouldBe(HttpStatusCode.Forbidden);
        none.Body.GetProperty("code").GetString().ShouldBe("auth.step_up_required");
        countedAfterNone.ShouldBe(before); // nothing counted toward the lockout
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await Factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().CountAsync(e => e.Event == AuditEventType.StepUpFailed, Ct))).ShouldBe(before + 1);
        (await ServiceAsync("cells-refresh")).GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Resuming_with_the_fresh_password_resumes_it_with_an_audit_row_and_it_then_shows_as_a_paper_blocker()
    {
        var resumed = await ResumeAsync("cells-refresh", new { password = Factory.OwnerPassword });
        var config = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/config", UriKind.Relative), Ct);
        var audit = await Factory.WithDbAsync(async db => await db.AccountEvents.AsNoTracking().Where(e => e.AccountId == null).Select(e => e.Event.ToString()).Where(e => e == "ServiceResumed").ToListAsync(Ct));

        resumed.Status.ShouldBe(HttpStatusCode.OK);
        resumed.Body.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        audit.ShouldBe(["ServiceResumed"]);
        config.GetProperty("blockers").EnumerateArray().Select(b => b.GetProperty("serviceId").GetString()).ShouldContain("cells-refresh");
    }

    [Fact]
    public async Task Another_service_still_resumes_with_a_session_only()
    {
        var resumed = await ResumeAsync("weekly", null);

        resumed.Status.ShouldBe(HttpStatusCode.OK);
        resumed.Body.GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task The_ledger_takes_the_cells_refresh_kind_counts_it_as_a_look_and_not_as_a_variant()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection,
            """
            INSERT INTO lab.test_ledger (strategy_id, version, strategy_hash, lab_run_id, run_key, universe_label, window_from, window_to, method, created_at, trial_kind, extra_trials)
            VALUES ('LCX', 1, @hash, gen_random_uuid(), @hash, 'list', '2024-11-01', '2026-04-04', 'cells_refresh', now(), 'cells_refresh', 0)
            """,
            ("hash", new string('a', 64)));

        using var scope = Factory.Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ITestLedger>();
        var counts = await ledger.CountTrialsAsync("LCX", Ct);
        var variants = await ledger.CountVariantsAsync("LCX", Ct);

        counts.CellsRefreshes.ShouldBe(1);
        counts.Total.ShouldBe(1);
        variants.ShouldBe(0);
    }
}
