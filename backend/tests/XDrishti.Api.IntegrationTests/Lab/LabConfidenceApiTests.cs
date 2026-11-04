using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Lab;

/// <summary>CF-06 over HTTP on a real database: a registration is written once (409 the second time), the unlock needs a fresh password (nothing is read or stored without it) and a second look by the same version is 409 with the stored result unchanged and no second audit row.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabConfidenceApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string Hash = new('e', 64);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanAsync();
        await StrategySeeding.SeedAsync(Factory.Services, Ct);
    }

    public override async ValueTask DisposeAsync()
    {
        await CleanAsync();
        await base.DisposeAsync();
    }

    private async Task CleanAsync()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM lab.holdout_unlocks; DELETE FROM lab.holdout_blocks; DELETE FROM lab.plan_freezes; DELETE FROM lab.registrations; DELETE FROM lab.lab_results; DELETE FROM lab.lab_runs; DELETE FROM accounts.account_events WHERE event = 'LabHoldOutUnlocked'");
    }

    private static object Body => new { counterparty = "retail chasing breakouts", parameterSigns = "rr up, pull_atr up", applicability = "liquid names, first hour", expectedEdge = "0.15 R gross against 0.05 R of cost", falsifier = "net expectancy at or below zero out of sample" };

    [Fact]
    public async Task A_registration_is_written_once_a_second_put_is_409_and_the_get_reads_it_back()
    {
        using var first = await Client.PutJsonAsync("/api/lab/strategies/ORB/versions/1/registration", Body, Ct);
        using var second = await Client.PutJsonAsync("/api/lab/strategies/ORB/versions/1/registration", Body, Ct);
        using var read = await Client.GetAsync(new Uri("/api/lab/strategies/ORB/versions/1/registration", UriKind.Relative), Ct);
        using var missing = await Client.GetAsync(new Uri("/api/lab/strategies/ORB/versions/2/registration", UriKind.Relative), Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(Ct));
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("lab.registration_frozen");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await read.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("textHash").GetString()!.Length.ShouldBe(64);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_unlock_without_or_with_a_wrong_password_is_refused_before_anything_is_looked_up_or_stored()
    {
        var run = Guid.NewGuid();

        using var none = await Client.PostJsonAsync($"/api/lab/runs/{run}/holdout-unlock", new { }, Ct);
        using var wrong = await Client.PostJsonAsync($"/api/lab/runs/{run}/holdout-unlock", new { password = "not the password" }, Ct);

        // the shared step-up check answers a missing or wrong password with 401 auth.invalid_credentials (and counts it toward the lockout); the spec's 403 is the step-up window of other acts
        none.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await none.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("auth.invalid_credentials");
        (await ScalarAsync<long>("SELECT count(*) FROM lab.holdout_unlocks")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM lab.holdout_blocks")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM lab.lab_runs WHERE method = 'holdout'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_second_look_by_the_same_version_is_409_lab_holdout_used_the_stored_result_stays_and_no_audit_row_is_written()
    {
        using var scope = Factory.Services.CreateScope();
        var version = await scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Research.IStrategyStore>().GetVersionAsync("ORB", 1, Ct);
        var created = await scope.ServiceProvider.GetRequiredService<ILabRunStore>().InsertOrGetAsync(
            new NewLabRun(Guid.NewGuid(), new string('9', 64), "ORB", 1, version!.Hash, new LabUniverse("symbols", null, "symbols as listed", 1, [new LabInstrument(Guid.NewGuid(), "AAA")], false, null), new DateOnly(2025, 1, 1), new DateOnly(2026, 4, 30), "full", new string('c', 64), "sim-5", [], DateTimeOffset.UtcNow), Ct);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE lab.lab_runs SET state = 'succeeded', finished_at = now() WHERE id = @id", ("id", created.Run.Id));
        }

        var verdict = new LabVerdictView("PASS", 1, 1, new EvidenceView(100, 0.2, 1.5, 6, 5, 3, 0, "walk_forward"), [], [], [], [], []);
        (await scope.ServiceProvider.GetRequiredService<ILabResultStore>().AppendAsync(created.Run.Id, "ORB", version.Hash, verdict, DateTimeOffset.UtcNow, Ct)).ShouldBeTrue();
        var result = new HoldOutResultView("ORB", 1, "strategy", "owner", "used", DateTimeOffset.UtcNow, new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30), false, false, true, "pass", "ok", 0.2, 0.12, 0.6, 45, Hash, Hash, "owner:x", []);
        (await scope.ServiceProvider.GetRequiredService<IHoldOutStore>().InsertUnlockAsync(new StoredUnlock("ORB", 1, "strategy", "owner", "owner:x", DateTimeOffset.UtcNow, Hash, false, new DateOnly(2026, 5, 1), created.Run.Id, result), Ct)).ShouldBeTrue();

        using var response = await Client.PostJsonAsync($"/api/lab/runs/{created.Run.Id}/holdout-unlock", new { password = Factory.OwnerPassword }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync(Ct));
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("lab.holdout_used");
        (await ScalarAsync<long>("SELECT count(*) FROM lab.holdout_unlocks")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'LabHoldOutUnlocked'")).ShouldBe(0);
        using var view = await Client.GetAsync(new Uri("/api/lab/holdout", UriKind.Relative), Ct);
        (await view.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("unlocks").GetArrayLength().ShouldBe(1);
    }

    // enforcement and the one successful look, over HTTP

    private async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.18.77.7");
        using var response = await client.PostAsJsonAsync(new Uri("/api/auth/login", UriKind.Relative), new { email = ApiFactory.OwnerEmail, password = Factory.OwnerPassword }, Ct);
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct));
        client.DefaultRequestHeaders.Add("Cookie", $"xd.sid={ApiFactory.SessionCookieValue(response)}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", session.GetProperty("csrfToken").GetString());
        return client;
    }

    private async Task<string> SeedWithBarsAsync(string ticker)
    {
        var id = await SeedAsync(ticker);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.instruments SET tick_size = 5 WHERE id = @id", ("id", id));
        }

        using var scope = Factory.Services.CreateScope();
        await using var session = await scope.ServiceProvider.GetRequiredService<IBarStore>().BeginMergeAsync(id, BarSource.Dhan, Ct);
        await session.AddAsync(ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.F1(ReplayDays.D2), ReplayDays.Flat(ReplayDays.D3, 105.00)), Ct);
        await session.CompleteAsync(null, Ct);
        return ticker;
    }

    [Fact]
    public async Task With_enforcement_on_a_full_run_of_an_unregistered_version_and_a_window_that_reaches_the_hold_out_are_each_422()
    {
        var symbol = await SeedWithBarsAsync("HOLD001");
        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IHoldOutStore>().InsertBlockAsync(new HoldOutBlock(ReplayDays.D2, ReplayDays.D1, ReplayDays.D3, 6, Hash, DateTimeOffset.UtcNow), Ct)).ShouldBeTrue();
        using var host = Factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Registration:Enforce", "true").UseSetting("Confidence:HoldOut:Enforce", "true"));
        using var client = await SignInAsync(host);
        var window = new { from = ReplayDays.D1.ToString("yyyy-MM-dd"), to = ReplayDays.D3.ToString("yyyy-MM-dd") };

        using var unregistered = await client.PostJsonAsync("/api/lab/runs", new { strategyId = "ORB", version = 1, universe = new { symbols = new[] { symbol } }, method = "full", window }, Ct);
        using var touched = await client.PostJsonAsync("/api/lab/runs", new { strategyId = "ORB", version = 1, universe = new { symbols = new[] { symbol } }, method = "quick", window }, Ct);

        unregistered.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await unregistered.Content.ReadAsStringAsync(Ct));
        (await unregistered.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("lab.not_registered");
        touched.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await touched.Content.ReadAsStringAsync(Ct));
        (await touched.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("lab.holdout_touched");
        (await ScalarAsync<long>("SELECT count(*) FROM lab.lab_runs")).ShouldBe(0);
    }

    [Fact]
    public async Task A_first_look_over_http_is_queued_as_a_holdout_job_the_dispatcher_stores_one_owner_unlock_and_a_second_is_409()
    {
        var created = await SeedUnlockableRunAsync("HOLD002");
        using var host = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services => services.AddScoped<IHoldOutPass, StubPass>()));
        using var client = await SignInAsync(host);

        using var first = await client.PostJsonAsync($"/api/lab/runs/{created}/holdout-unlock", new { password = Factory.OwnerPassword }, Ct);

        // 202: nothing is looked at in the request; the job is queued and carries no password
        first.StatusCode.ShouldBe(HttpStatusCode.Accepted, await first.Content.ReadAsStringAsync(Ct));
        var job = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);
        (job.GetProperty("method").GetString(), job.GetProperty("state").GetString()).ShouldBe(("holdout", "queued"));
        first.Headers.Location!.ToString().ShouldEndWith(job.GetProperty("id").GetString()!);
        (await ScalarAsync<long>("SELECT count(*) FROM lab.holdout_unlocks")).ShouldBe(0);
        (await ScalarAsync<string>($"SELECT labels::text FROM lab.lab_runs WHERE id = '{job.GetProperty("id").GetString()}'")).ShouldNotContain(Factory.OwnerPassword);

        await host.Services.GetRequiredService<LabRunDispatcher>().DrainAsync(Ct);

        using var done = await client.GetAsync(new Uri($"/api/lab/runs/{job.GetProperty("id").GetString()}", UriKind.Relative), Ct);
        (await done.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("state").GetString().ShouldBe("succeeded");
        using var second = await client.PostJsonAsync($"/api/lab/runs/{created}/holdout-unlock", new { password = Factory.OwnerPassword }, Ct);
        using var look = await client.GetAsync(new Uri("/api/lab/holdout", UriKind.Relative), Ct);
        var unlocks = (await look.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("unlocks");
        unlocks.GetArrayLength().ShouldBe(1);
        (unlocks[0].GetProperty("mode").GetString(), unlocks[0].GetProperty("state").GetString(), unlocks[0].GetProperty("passed").GetBoolean(), unlocks[0].GetProperty("status").GetString()).ShouldBe(("owner", "used", true, "pass"));
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("lab.holdout_used");
        (await ScalarAsync<long>("SELECT count(*) FROM lab.holdout_unlocks WHERE mode = 'owner'")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'LabHoldOutUnlocked'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_second_post_while_the_first_job_is_queued_returns_the_same_job_and_one_row_and_asking_again_after_a_failed_job_creates_a_new_one()
    {
        var run = await SeedUnlockableRunAsync("HOLD003");
        using var host = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services => services.AddScoped<IHoldOutPass, RefusingPass>()));
        using var client = await SignInAsync(host);

        using var first = await client.PostJsonAsync($"/api/lab/runs/{run}/holdout-unlock", new { password = Factory.OwnerPassword }, Ct);
        using var again = await client.PostJsonAsync($"/api/lab/runs/{run}/holdout-unlock", new { password = Factory.OwnerPassword }, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString();
        (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString().ShouldBe(firstId);
        (await ScalarAsync<long>("SELECT count(*) FROM lab.lab_runs WHERE method = 'holdout'")).ShouldBe(1);

        await host.Services.GetRequiredService<LabRunDispatcher>().DrainAsync(Ct);

        (await ScalarAsync<string>($"SELECT state FROM lab.lab_runs WHERE id = '{firstId}'")).ShouldBe("failed");
        (await ScalarAsync<long>("SELECT count(*) FROM lab.holdout_unlocks")).ShouldBe(0);
        using var retry = await client.PostJsonAsync($"/api/lab/runs/{run}/holdout-unlock", new { password = Factory.OwnerPassword }, Ct);
        retry.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await retry.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString().ShouldNotBe(firstId);
        (await ScalarAsync<long>("SELECT count(*) FROM lab.lab_runs WHERE method = 'holdout'")).ShouldBe(2);
    }

    private async Task<Guid> SeedUnlockableRunAsync(string symbol)
    {
        await SeedWithBarsAsync(symbol);
        using var scope = Factory.Services.CreateScope();
        var version = await scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Research.IStrategyStore>().GetVersionAsync("ORB", 1, Ct);
        var created = await scope.ServiceProvider.GetRequiredService<ILabRunStore>().InsertOrGetAsync(
            new NewLabRun(Guid.NewGuid(), new string('8', 64), "ORB", 1, version!.Hash, new LabUniverse("symbols", null, "symbols as listed", 1, [new LabInstrument(Guid.NewGuid(), "AAA")], false, null), new DateOnly(2025, 1, 1), new DateOnly(2026, 4, 30), "full", new string('c', 64), "sim-5", [], DateTimeOffset.UtcNow), Ct);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE lab.lab_runs SET state = 'succeeded', finished_at = now() WHERE id = @id", ("id", created.Run.Id));
        }

        var verdict = new LabVerdictView("PASS", 1, 1, new EvidenceView(100, 0.2, 1.5, 6, 5, 3, 0, "walk_forward"), [], [], [], [], []);
        (await scope.ServiceProvider.GetRequiredService<ILabResultStore>().AppendAsync(created.Run.Id, "ORB", version.Hash, verdict, DateTimeOffset.UtcNow, Ct)).ShouldBeTrue();
        (await scope.ServiceProvider.GetRequiredService<IHoldOutStore>().InsertBlockAsync(new HoldOutBlock(new DateOnly(2026, 5, 1), new DateOnly(2024, 10, 1), new DateOnly(2026, 10, 30), 6, Hash, DateTimeOffset.UtcNow), Ct)).ShouldBeTrue();
        await scope.ServiceProvider.GetRequiredService<IPlanFreezeStore>().FreezeAsync("ORB", 1, new string('f', 64), created.Run.Id, DateTimeOffset.UtcNow, Ct);
        return created.Run.Id;
    }

    private sealed class StubPass : IHoldOutPass
    {
        public ValueTask<Result<HoldOutFigures>> RunAsync(LabRun run, LabVerdictView verdict, HoldOutBlock block, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result.Success(new HoldOutFigures(45, 0.12, block.Start, block.HistoryLast)));
    }

    private sealed class RefusingPass : IHoldOutPass
    {
        public ValueTask<Result<HoldOutFigures>> RunAsync(LabRun run, LabVerdictView verdict, HoldOutBlock block, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result.Failure<HoldOutFigures>(Error.Conflict("lab.holdout_unavailable", "refused")));
    }
}
