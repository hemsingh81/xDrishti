using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// Spec P8-15 over HTTP and a real database: the plan notices of the bell are derived from the stored plan, runs and tickets. They need the session (401), exist for GET only, are empty on an empty database, appear for a plan awaiting review and a failed-closed run,
/// go by themselves when the owner decides or a later run publishes, carry a route and no command, and the read writes nothing.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PlanNoticesApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid AccountId = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a20");

    // The API clock is pinned to 2099-01-04 12:00 India; the plan is for the next day with its cutoff at 09:05 India, 1265 minutes away. The year 2099 keeps the cutoff in the future for the database clock, which refuses an approval after it (as in PlanReviewApiTests).
    private static readonly DateOnly Session = new(2099, 1, 5);

    private static readonly DateTimeOffset ReviewCutoff = new(2099, 1, 5, 3, 35, 0, TimeSpan.Zero);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Factory.Clock.Pin(new DateTimeOffset(2099, 1, 4, 6, 30, 0, TimeSpan.Zero));
        await ResignInAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES (@id, 'probe-notices', 'Probe', 'Dhan', '1000000095', current_date, '#1A2B3C', now(), now(), false, false, false, 200)
            """,
            ("id", AccountId));
    }

    private static NewPlanItem Item(int rank, string symbol) =>
        new(
            rank, TicketGroup.Primary, symbol, "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30", [],
            new PlanEvidence("ORB|15m|BUY|intraday|FIXED_RR|*", 41, "about 2.1 years, replayed history, modelled fills, no hold-out", "shrunk posterior win rate of the cell", 0.72, 0.12, ["allocation_entry_is_indicative"], null, 0.7),
            [new NewAllocation(AccountId, "paper", 10)]);

    private async Task<(long Plan, long[] Items)> StoredAsync(params NewPlanItem[] items)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPlanStore>();
        var plan = new NewPlan("notices-2099-01-05", Session, ReviewCutoff, 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, items);
        var published = await store.PublishAsync(plan, Ct);
        return (published.Id, [.. (await store.ItemsAsync(published.Id, Ct)).Select(r => r.Id)]);
    }

    private async Task<JsonElement> NoticesAsync()
    {
        using var response = await Client.GetAsync(new Uri("/api/plans/notices", UriKind.Relative), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items");
    }

    [Fact]
    public async Task Without_a_session_the_notices_are_401_and_only_get_exists()
    {
        var anonymous = Factory.CreateAnonymousClient();

        using var read = await anonymous.GetAsync(new Uri("/api/plans/notices", UriKind.Relative), Ct);
        using var write = await Client.PostAsJsonAsync(new Uri("/api/plans/notices", UriKind.Relative), new { }, Ct);
        using var delete = await Client.DeleteAsync(new Uri("/api/plans/notices", UriKind.Relative), Ct);

        read.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        write.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        delete.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task An_empty_database_has_no_items()
    {
        (await NoticesAsync()).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_plan_awaiting_review_is_listed_with_its_minutes_and_resolves_once_every_ticket_is_decided()
    {
        var (plan, items) = await StoredAsync(Item(1, "NSE:RELIANCE"), Item(2, "NSE:TCS"));
        var before = await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events");

        var first = await NoticesAsync();

        var item = first.EnumerateArray().ShouldHaveSingleItem();
        item.GetProperty("kind").GetString().ShouldBe("plan_awaiting_review");
        item.GetProperty("id").GetString().ShouldBe($"plan:review:{plan}");
        item.GetProperty("route").GetString().ShouldBe($"/plans?plan={plan}");
        item.GetProperty("text").GetString()!.ShouldContain("2 tickets undecided, 1265 minutes to the cutoff at 09:05");
        item.GetProperty("text").GetString()!.ShouldNotContain("RELIANCE");
        item.TryGetProperty("actions", out _).ShouldBeFalse();
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events")).ShouldBe(before); // reading wrote nothing

        using var approved = await Client.PostAsJsonAsync(new Uri($"/api/plans/{plan}/items/{items[0]}/approve", UriKind.Relative), new { reason = "ok" }, Ct);
        approved.EnsureSuccessStatusCode();
        (await NoticesAsync()).GetArrayLength().ShouldBe(1); // one ticket is still undecided
        using var rejected = await Client.PostAsJsonAsync(new Uri($"/api/plans/{plan}/items/{items[1]}/reject", UriKind.Relative), new { reason = "not today" }, Ct);
        rejected.EnsureSuccessStatusCode();

        (await NoticesAsync()).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_run_that_failed_closed_is_an_error_and_a_later_published_run_removes_it()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPlanStore>();
            var at = new DateTimeOffset(2026, 10, 12, 12, 5, 0, TimeSpan.Zero);
            await store.RecordRunAsync(new NewPlanRun(Session, null, "failed_closed", "eod", "the EOD run has not finished", null, false, at, at), Ct);
        }

        var failed = (await NoticesAsync()).EnumerateArray().ShouldHaveSingleItem();

        failed.GetProperty("kind").GetString().ShouldBe("plan_nightly_failed");
        failed.GetProperty("severity").GetString().ShouldBe("error");
        failed.GetProperty("route").GetString().ShouldBe("/plans");
        failed.GetProperty("text").GetString()!.ShouldContain("failed closed at the eod gate: the EOD run has not finished.");

        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPlanStore>();
            var plan = new NewPlan("notices-retry", Session, ReviewCutoff, 1, new string('d', 64), "skeleton: no builder", true, "system:nightly", DateTimeOffset.UnixEpoch, []);
            await store.PublishWithRunAsync(plan, new PublishRun(true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), "Published"), Ct);
        }

        (await NoticesAsync()).GetArrayLength().ShouldBe(0);
    }
}
