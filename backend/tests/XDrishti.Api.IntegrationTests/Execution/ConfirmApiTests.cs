using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>
/// Spec P12-08 criteria 3, 6, 8 and the Contract over HTTP on the real database: every route needs a session and every write the CSRF token; the list is empty and validated; a tap or a skip of an unknown ticket is 404 and an
/// Idempotency-Key that is not the ticket id is 400 before anything is looked up; the day unlock needs a fresh password and is refused for an account that is not in <c>semi_auto</c>. No ticket can be written here: an intent references
/// its account, the account reset of this suite deletes accounts and the execution record is insert-only, so the tap's database path is proven as the real roles in <c>ExecutionTicketsDatabaseTests</c>.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ConfirmApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Tickets = new("/api/execution/confirm-tickets", UriKind.Relative);

    private static readonly Uri Unlocks = new("/api/execution/day-unlocks", UriKind.Relative);

    private static Uri Confirm(Guid id) => new($"/api/execution/confirm-tickets/{id}/confirm", UriKind.Relative);

    private static Uri Skip(Guid id) => new($"/api/execution/confirm-tickets/{id}/skip", UriKind.Relative);

    private async Task<Guid> AccountAsync()
    {
        var id = Guid.NewGuid();
        var client = Random.Shared.NextInt64(2_000_000_000, 9_000_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget) VALUES (@id, @slug, 'Confirm probe', 'Dhan', @client, current_date, '#112233', now(), now(), false, false, false, 0)",
            ("id", id), ("slug", "c-" + id.ToString("N")), ("client", client));
        await ExecuteAsync(
            connection,
            "INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments) VALUES (@id, true, 'ManualLive', 'Fixed', 400000, true, false, 1, 0, 0)",
            ("id", id));
        return id;
    }

    private async Task<(HttpStatusCode Status, string? Code)> PostAsync(Uri uri, object body, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        using var response = await Client.SendAsync(request, Ct);
        return (response.StatusCode, response.IsSuccessStatusCode ? null : (await response.ReadProblemAsync(Ct)).Code);
    }

    [Fact]
    public async Task Without_a_session_every_confirm_route_is_401()
    {
        var anonymous = Factory.CreateAnonymousClient();

        using var list = await anonymous.GetAsync(Tickets, Ct);
        using var confirm = await anonymous.PostAsJsonAsync(Confirm(Guid.NewGuid()), new { textHash = new string('a', 64) }, Ct);
        using var skip = await anonymous.PostAsJsonAsync(Skip(Guid.NewGuid()), new { }, Ct);
        using var unlocks = await anonymous.GetAsync(Unlocks, Ct);
        using var unlock = await anonymous.PostAsJsonAsync(Unlocks, new { accountId = Guid.NewGuid(), password = Factory.OwnerPassword }, Ct);

        new[] { list.StatusCode, confirm.StatusCode, skip.StatusCode, unlocks.StatusCode, unlock.StatusCode }.ShouldAllBe(s => s == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_write_without_the_csrf_token_is_403_even_with_a_session()
    {
        var client = Factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", Owner.CookieHeader);

        using var confirm = await client.PostAsJsonAsync(Confirm(Guid.NewGuid()), new { textHash = new string('a', 64) }, Ct);
        using var skip = await client.PostAsJsonAsync(Skip(Guid.NewGuid()), new { }, Ct);
        using var unlock = await client.PostAsJsonAsync(Unlocks, new { accountId = Guid.NewGuid(), password = Factory.OwnerPassword }, Ct);

        foreach (var response in new[] { confirm, skip, unlock })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.csrf_invalid");
        }
    }

    [Fact]
    public async Task The_lists_are_empty_without_tickets_or_unlocks_and_an_unknown_state_is_400()
    {
        var open = await Client.GetFromJsonAsync<JsonElement>(Tickets, Ct);
        var all = await Client.GetFromJsonAsync<JsonElement>(new Uri(Tickets + "?state=all&date=2026-10-27", UriKind.Relative), Ct);
        var unlocks = await Client.GetFromJsonAsync<JsonElement>(new Uri(Unlocks + "?date=2026-10-27", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri(Tickets + "?state=closed", UriKind.Relative), Ct);

        (open.GetArrayLength(), all.GetArrayLength(), unlocks.GetArrayLength()).ShouldBe((0, 0, 0));
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await bad.ReadProblemAsync(Ct)).Code.ShouldBe("execution.values_invalid");
    }

    [Fact]
    public async Task A_tap_or_a_skip_of_an_unknown_ticket_is_404_and_an_idempotency_key_that_is_not_the_ticket_id_is_400()
    {
        var id = Guid.NewGuid();

        (await PostAsync(Confirm(id), new { textHash = new string('a', 64) })).ShouldBe((HttpStatusCode.NotFound, "execution.not_found"));
        (await PostAsync(Confirm(id), new { textHash = new string('a', 64) }, id.ToString())).ShouldBe((HttpStatusCode.NotFound, "execution.not_found"));
        (await PostAsync(Confirm(id), new { textHash = new string('a', 64) }, Guid.NewGuid().ToString())).ShouldBe((HttpStatusCode.BadRequest, "execution.values_invalid"));
        (await PostAsync(Confirm(id), new { textHash = new string('a', 64) }, "not-a-guid")).ShouldBe((HttpStatusCode.BadRequest, "execution.values_invalid"));
        (await PostAsync(Skip(id), new { reason = "not today" })).ShouldBe((HttpStatusCode.NotFound, "execution.not_found"));
        (await PostAsync(Skip(id), new { reason = new string('x', 201) })).ShouldBe((HttpStatusCode.BadRequest, "execution.values_invalid"));
    }

    [Fact]
    public async Task A_day_unlock_needs_a_fresh_password_and_is_refused_for_an_account_that_is_not_semi_auto_and_nothing_is_written()
    {
        var account = await AccountAsync();

        (await PostAsync(Unlocks, new { accountId = account })).ShouldBe((HttpStatusCode.Unauthorized, "auth.invalid_credentials"));
        (await PostAsync(Unlocks, new { accountId = account, password = Factory.OwnerPassword })).ShouldBe((HttpStatusCode.Conflict, "execution.mode_not_executing"));
        (await PostAsync(Unlocks, new { accountId = Guid.NewGuid(), password = Factory.OwnerPassword })).ShouldBe((HttpStatusCode.NotFound, "execution.not_found"));
        (await PostAsync(Unlocks, new { password = Factory.OwnerPassword })).ShouldBe((HttpStatusCode.BadRequest, "execution.values_invalid"));
        (await ScalarAsync<long>("SELECT count(*) FROM execution.day_unlocks WHERE account_id = @a", ("a", account))).ShouldBe(0L);
    }
}
