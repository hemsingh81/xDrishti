using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Lab;

namespace XDrishti.Api.IntegrationTests.Lab;

/// <summary>HO-01 over HTTP on a real database (ADR 0283): the owner's release needs a fresh password, answers the refusals of the spec with their codes, stores the release row and block 2 together, and moves the block in force.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldOutReleaseApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string Hash = new('e', 64);
    private static readonly DateOnly One = new(2026, 2, 2); // the suite pins the clock to 2026-10-12: block 1 is sealed, block 2 is still filling
    private static readonly DateOnly OneEnd = new(2026, 9, 30);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CleanAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await CleanAsync();
        await base.DisposeAsync();
    }

    private async Task CleanAsync()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM lab.holdout_releases; DELETE FROM lab.holdout_unlocks; DELETE FROM lab.holdout_blocks");
    }

    private async Task SeedBlockOneAsync(bool looked)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IHoldOutStore>();
        (await store.InsertBlockAsync(new HoldOutBlock(One, new DateOnly(2024, 10, 1), OneEnd, 6, Hash, DateTimeOffset.UtcNow), Ct)).ShouldBeTrue();
        if (looked)
        {
            var result = new HoldOutResultView("ORB", 1, "strategy", "owner", "used", DateTimeOffset.UtcNow, One, OneEnd, false, false, true, "pass", "ok", 0.2, 0.12, 0.6, 45, Hash, Hash, "owner:x", []);
            (await store.InsertUnlockAsync(new StoredUnlock("ORB", 1, "strategy", "owner", "owner:x", DateTimeOffset.UtcNow, Hash, false, One, Guid.NewGuid(), result), Ct)).ShouldBeTrue();
        }
    }

    private async Task<HttpClient> SignInAsync()
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.18.77.9");
        using var response = await client.PostAsJsonAsync(new Uri("/api/auth/login", UriKind.Relative), new { email = ApiFactory.OwnerEmail, password = Factory.OwnerPassword }, Ct);
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        client.DefaultRequestHeaders.Add("Cookie", $"xd.sid={ApiFactory.SessionCookieValue(response)}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", session.GetProperty("csrfToken").GetString());
        return client;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response, CancellationToken ct) => (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString();

    [Fact]
    public async Task A_release_without_a_password_is_403_and_with_a_wrong_one_is_401_and_nothing_is_stored()
    {
        await SeedBlockOneAsync(looked: true);
        using var client = await SignInAsync();

        using var none = await client.PostJsonAsync("/api/lab/holdout/blocks/2026-02-02/release", new { }, Ct);
        using var wrong = await client.PostJsonAsync("/api/lab/holdout/blocks/2026-02-02/release", new { password = "not the password" }, Ct);

        none.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(none, Ct)).ShouldBe("auth.step_up_required");
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ScalarAsync<long>("SELECT count(*) FROM lab.holdout_releases")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM lab.holdout_blocks")).ShouldBe(1);
    }

    [Fact]
    public async Task An_unlooked_block_needs_the_forfeit_and_then_the_release_row_and_block_two_are_stored_together_and_the_view_lists_the_chain()
    {
        await SeedBlockOneAsync(looked: false);
        using var client = await SignInAsync();

        using var refused = await client.PostJsonAsync("/api/lab/holdout/blocks/2026-02-02/release", new { password = Factory.OwnerPassword }, Ct);
        using var done = await client.PostJsonAsync("/api/lab/holdout/blocks/2026-02-02/release", new { password = Factory.OwnerPassword, forfeitLook = true }, Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(refused, Ct)).ShouldBe("lab.holdout_unlooked");
        done.StatusCode.ShouldBe(HttpStatusCode.OK, await done.Content.ReadAsStringAsync(Ct));
        var block = await done.Content.ReadFromJsonAsync<JsonElement>(Ct);
        (block.GetProperty("state").GetString(), block.GetProperty("mode").GetString(), block.GetProperty("looks").GetProperty("strategy").GetProperty("state").GetString()).ShouldBe(("released", "owner", "none"));
        (await ScalarAsync<long>("SELECT count(*) FROM lab.holdout_releases WHERE block_start = '2026-02-02' AND NOT looked AND mode = 'owner' AND next_block_start = '2026-10-01'")).ShouldBe(1);
        (await ScalarAsync<string>("SELECT history_last::text || ' ' || prev_start::text FROM lab.holdout_blocks WHERE start_date = '2026-10-01'")).ShouldBe("2027-03-30 2026-02-02");
        using var view = await client.GetAsync(new Uri("/api/lab/holdout", UriKind.Relative), Ct);
        var json = await view.Content.ReadFromJsonAsync<JsonElement>(Ct);
        json.GetProperty("blocks").EnumerateArray().Select(b => b.GetProperty("state").GetString()).ShouldBe(["released", "filling"]);
        json.GetProperty("block").GetProperty("start").GetString().ShouldBe("2026-10-01");
        json.GetProperty("autoRelease").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_second_release_a_filling_block_a_future_start_and_a_bad_date_answer_their_codes()
    {
        await SeedBlockOneAsync(looked: true);
        using var client = await SignInAsync();
        using var first = await client.PostJsonAsync("/api/lab/holdout/blocks/2026-02-02/release", new { password = Factory.OwnerPassword }, Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(Ct));

        using var again = await client.PostJsonAsync("/api/lab/holdout/blocks/2026-02-02/release", new { password = Factory.OwnerPassword }, Ct);
        using var filling = await client.PostJsonAsync("/api/lab/holdout/blocks/2026-10-01/release", new { password = Factory.OwnerPassword, forfeitLook = true }, Ct);
        using var future = await client.PostJsonAsync("/api/lab/holdout/blocks/2027-04-01/release", new { password = Factory.OwnerPassword, forfeitLook = true }, Ct);
        using var bad = await client.PostJsonAsync("/api/lab/holdout/blocks/next/release", new { password = Factory.OwnerPassword }, Ct);

        (again.StatusCode, await CodeAsync(again, Ct)).ShouldBe((HttpStatusCode.Conflict, "lab.holdout_released"));
        (filling.StatusCode, await CodeAsync(filling, Ct)).ShouldBe((HttpStatusCode.Conflict, "lab.holdout_filling"));
        (future.StatusCode, await CodeAsync(future, Ct)).ShouldBe((HttpStatusCode.Conflict, "lab.holdout_order"));
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ScalarAsync<long>("SELECT count(*) FROM lab.holdout_releases")).ShouldBe(1);
    }

    [Fact]
    public async Task The_block_in_force_is_block_one_until_the_release_then_block_two_and_the_current_cut_follows()
    {
        await SeedBlockOneAsync(looked: true);
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IHoldOutStore>();
        (await store.CurrentAsync(Ct))!.Start.ShouldBe(One);
        using var client = await SignInAsync();

        using var released = await client.PostJsonAsync("/api/lab/holdout/blocks/2026-02-02/release", new { password = Factory.OwnerPassword }, Ct);

        released.StatusCode.ShouldBe(HttpStatusCode.OK);
        var current = (await store.CurrentAsync(Ct))!;
        (current.Start, current.PrevStart).ShouldBe((new DateOnly(2026, 10, 1), One));
        (await store.ListBlocksAsync(Ct)).Select(b => b.Release is not null).ToList().ShouldBe([true, false]);
    }
}
