using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Replay;

/// <summary>
/// SR-03 over a real TimescaleDB (ADR 0284): the registration (frozen hash, supersede, refusals with their reason), the fresh-password run and its refusals (403, 401, drift before the claim with no look written, nothing to run in a block that is not stored), the two seeded switches and
/// their fresh-password change. The suite clock is pinned to 2026-10-12: block 2 (2026-10-01..2027-03-30) is in force, block 3 starts 2027-03-31.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldOutRegistrationApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string Hash = new('e', 64);
    private static readonly DateOnly One = new(2026, 2, 2);
    private static readonly DateOnly OneEnd = new(2026, 9, 30);
    private static readonly DateOnly Two = new(2026, 10, 1);
    private static readonly DateOnly TwoEnd = new(2027, 3, 30);
    private static readonly DateOnly Three = new(2027, 3, 31);
    private const string SheetKey = "research.cost_sheet.dhan-2026-04";

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanAsync();
        Factory.ReplayStart.Facts = new StubReplayFacts();
        await SeedChainAsync();
        await StoreSheetAsync(0.03m, checkedNote: true);
    }

    public override async ValueTask DisposeAsync()
    {
        Factory.ReplayStart.Facts = null;
        await CleanAsync();
        await base.DisposeAsync();
    }

    private async Task CleanAsync()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.replay_holdout_results; DELETE FROM research.replay_holdout_registrations; DELETE FROM lab.holdout_releases; DELETE FROM lab.holdout_unlocks; DELETE FROM lab.holdout_blocks; DELETE FROM platform.app_settings WHERE key LIKE 'research.cost_sheet.%'; DELETE FROM research.replay_holdout_settings; INSERT INTO research.replay_holdout_settings (name, enabled, actor, reason, at) VALUES ('replay.holdout.auto_register', true, 'system:default', 'ADR 0282', '2000-01-01'), ('replay.holdout.auto_run', true, 'system:default', 'ADR 0282', '2000-01-01')");
    }

    private async Task SeedChainAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IHoldOutStore>();
        (await store.InsertBlockAsync(new HoldOutBlock(One, new DateOnly(2024, 10, 1), OneEnd, 6, Hash, DateTimeOffset.UtcNow), Ct)).ShouldBeTrue();
        (await store.ReleaseAsync(new StoredRelease(One, DateTimeOffset.UtcNow, "owner", "owner:x", Hash, false, Two), new HoldOutBlock(Two, new DateOnly(2024, 10, 1), TwoEnd, 6, Hash, DateTimeOffset.UtcNow, One), Ct)).ShouldBe(ReleaseOutcome.Released);
    }

    private async Task StoreSheetAsync(decimal brokerage, bool checkedNote)
    {
        var sheet = CostSheet.Create("dhan-2026-04", "Dhan", "contract note", checkedNote ? "checked against contract note" : null, TestCostSheet.Of("dhan-2026-04", TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.Rates(brokerage, 20m, 0m, 0m, 0.04m, 0.005m, 50m, 0.02m, 0m, 0m), TestCostSheet.DeliveryA)).Schedules).Value;
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM platform.app_settings WHERE key = @key", ("key", SheetKey));
        await ExecuteAsync(connection, "INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES (@key, @value, 'test', now())", ("key", SheetKey), ("value", sheet.ToCanonicalJson()));
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response, CancellationToken ct) => await response.Content.ReadFromJsonAsync<JsonElement>(ct);

    private static object Body(string? sheet = "dhan-2026-04") => new { instrumentIds = new[] { new Guid(1, 0, 0, new byte[8]) }, costSheetId = sheet };

    [Fact]
    public async Task A_registration_stores_the_hash_and_the_same_inputs_give_the_same_hash_and_the_older_one_reads_superseded()
    {
        using var first = await Client.PostJsonAsync("/api/replay/holdout-registrations", Body(), Ct);
        Factory.Clock.Pin(Now.AddMinutes(10));
        using var second = await Client.PostJsonAsync("/api/replay/holdout-registrations", Body(), Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(Ct));
        var (a, b) = (await JsonOf(first, Ct), await JsonOf(second, Ct));
        (a.GetProperty("blockStart").GetString(), b.GetProperty("configHash").GetString()).ShouldBe(("2027-03-31", a.GetProperty("configHash").GetString()));
        a.GetProperty("id").GetGuid().ShouldNotBe(b.GetProperty("id").GetGuid());
        using var view = await Client.GetAsync(new Uri($"/api/replay/holdout-registrations/{a.GetProperty("id").GetGuid()}", UriKind.Relative), Ct);
        var older = await JsonOf(view, Ct);
        (older.GetProperty("state").GetString(), older.GetProperty("rule").GetProperty("maxDrawdownPct").GetDouble(), older.GetProperty("rule").GetProperty("minTrades").GetInt32()).ShouldBe(("superseded", 15d, 30));
        older.GetProperty("hindsight").GetString()!.ShouldContain("Unseen by the system");
        using var list = await Client.GetAsync(new Uri("/api/replay/holdout-registrations", UriKind.Relative), Ct);
        (await JsonOf(list, Ct)).EnumerateArray().Select(r => r.GetProperty("state").GetString()).ShouldBe(["waiting", "superseded"]);
        (await ScalarAsync<long>("SELECT count(*) FROM research.replay_holdout_registrations WHERE config_hash ~ '^[0-9a-f]{64}$' AND mode = 'owner'")).ShouldBe(2);
    }

    [Theory]
    [InlineData(null, "gross_choice")]
    [InlineData("gross", "gross_choice")]
    [InlineData("missing-sheet", "sheet_missing")]
    public async Task A_missing_or_gross_sheet_is_422_with_its_reason(string? sheet, string reason)
    {
        using var response = await Client.PostJsonAsync("/api/replay/holdout-registrations", Body(sheet), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var json = await JsonOf(response, Ct);
        (json.GetProperty("code").GetString(), json.GetProperty("title").GetString()!.StartsWith(reason + ":", StringComparison.Ordinal)).ShouldBe(("replay.registration_invalid", true));
        (await ScalarAsync<long>("SELECT count(*) FROM research.replay_holdout_registrations")).ShouldBe(0);
    }

    [Fact]
    public async Task An_unchecked_sheet_a_scope_over_the_memory_cap_and_a_block_that_has_begun_are_422()
    {
        await StoreSheetAsync(0.03m, checkedNote: false);
        using var unchecked_ = await Client.PostJsonAsync("/api/replay/holdout-registrations", Body(), Ct);
        await StoreSheetAsync(0.03m, checkedNote: true);
        Factory.ReplayStart.Facts = new StubReplayFacts { Members = 200 };
        using var capped = await Client.PostJsonAsync("/api/replay/holdout-registrations", Body(), Ct);
        Factory.ReplayStart.Facts = new StubReplayFacts();
        Factory.Clock.Pin(new DateTimeOffset(2027, 3, 31, 6, 30, 0, TimeSpan.Zero));
        await ResignInAsync();
        using var begun = await Client.PostJsonAsync("/api/replay/holdout-registrations", Body(), Ct);

        (await JsonOf(unchecked_, Ct)).GetProperty("title").GetString().ShouldStartWith("sheet_unchecked:");
        (await JsonOf(capped, Ct)).GetProperty("title").GetString().ShouldStartWith("scope_over_memory_cap:");
        begun.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonOf(begun, Ct)).GetProperty("title").GetString().ShouldStartWith("no_future_block:");
    }

    [Fact]
    public async Task A_run_without_a_password_is_403_with_a_wrong_one_401_and_a_block_not_stored_yet_is_409_and_claims_nothing()
    {
        using var made = await Client.PostJsonAsync("/api/replay/holdout-registrations", Body(), Ct);
        var id = (await JsonOf(made, Ct)).GetProperty("id").GetGuid();
        var url = $"/api/replay/holdout-registrations/{id}/run";

        using var none = await Client.PostJsonAsync(url, new { }, Ct);
        using var wrong = await Client.PostJsonAsync(url, new { password = "not the password" }, Ct);
        using var early = await Client.PostJsonAsync(url, new { password = Factory.OwnerPassword }, Ct);

        none.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonOf(early, Ct)).GetProperty("code").GetString().ShouldBe("lab.holdout_unavailable");
        (await ScalarAsync<long>("SELECT count(*) FROM lab.holdout_unlocks WHERE kind = 'system'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_replaced_sheet_is_409_drift_on_the_run_and_no_look_is_written_and_an_unknown_registration_is_404()
    {
        using var made = await Client.PostJsonAsync("/api/replay/holdout-registrations", Body(), Ct);
        var id = (await JsonOf(made, Ct)).GetProperty("id").GetGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            // block 2 is released, block 3 is stored and sealed once the clock passes its end (2027-09-30)
            var store = scope.ServiceProvider.GetRequiredService<IHoldOutStore>();
            (await store.ReleaseAsync(new StoredRelease(Two, DateTimeOffset.UtcNow, "owner", "owner:x", Hash, false, Three), new HoldOutBlock(Three, new DateOnly(2024, 10, 1), new DateOnly(2027, 9, 30), 6, Hash, DateTimeOffset.UtcNow, Two), Ct)).ShouldBe(ReleaseOutcome.Released);
        }

        await StoreSheetAsync(0.05m, checkedNote: true);
        Factory.Clock.Pin(new DateTimeOffset(2027, 10, 5, 6, 30, 0, TimeSpan.Zero));
        await ResignInAsync();
        using var run = await Client.PostJsonAsync($"/api/replay/holdout-registrations/{id}/run", new { password = Factory.OwnerPassword }, Ct);
        using var missing = await Client.PostJsonAsync($"/api/replay/holdout-registrations/{Guid.NewGuid()}/run", new { password = Factory.OwnerPassword }, Ct);

        run.StatusCode.ShouldBe(HttpStatusCode.Conflict, await run.Content.ReadAsStringAsync(Ct));
        var json = await JsonOf(run, Ct);
        (json.GetProperty("code").GetString(), json.GetProperty("title").GetString()).ShouldBe(("replay.registration_drift", json.GetProperty("title").GetString()));
        json.GetProperty("title").GetString()!.ShouldContain("new content hash");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ScalarAsync<long>("SELECT count(*) FROM lab.holdout_unlocks")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM research.replay_holdout_results")).ShouldBe(0);
        using var view = await Client.GetAsync(new Uri($"/api/replay/holdout-registrations/{id}", UriKind.Relative), Ct);
        (await JsonOf(view, Ct)).GetProperty("state").GetString().ShouldBe("drift");
    }

    [Fact]
    public async Task Both_switches_are_seeded_on_and_changing_them_needs_a_fresh_password_and_appends_a_row_by_the_owner()
    {
        using var before = await Client.GetAsync(new Uri("/api/replay/settings", UriKind.Relative), Ct);
        using var none = await Client.PutJsonAsync("/api/replay/settings", new { autoRun = false }, Ct);
        using var wrong = await Client.PutJsonAsync("/api/replay/settings", new { password = "nope", autoRun = false }, Ct);
        using var done = await Client.PutJsonAsync("/api/replay/settings", new { password = Factory.OwnerPassword, autoRun = false }, Ct);

        var seeded = await JsonOf(before, Ct);
        (seeded.GetProperty("autoRegister").GetBoolean(), seeded.GetProperty("autoRun").GetBoolean()).ShouldBe((true, true));
        (none.StatusCode, wrong.StatusCode).ShouldBe((HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized));
        var after = await JsonOf(done, Ct);
        (after.GetProperty("autoRegister").GetBoolean(), after.GetProperty("autoRun").GetBoolean()).ShouldBe((true, false));
        (await ScalarAsync<long>("SELECT count(*) FROM research.replay_holdout_settings WHERE name = 'replay.holdout.auto_run' AND NOT enabled AND actor <> 'system:default'")).ShouldBe(1);
    }
}
