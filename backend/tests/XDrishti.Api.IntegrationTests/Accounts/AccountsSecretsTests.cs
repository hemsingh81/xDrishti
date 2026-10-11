using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.RiskProfiles;
using XDrishti.Domain.Accounts;

namespace XDrishti.Api.IntegrationTests.Accounts;

/// <summary>
/// Broker secrets are write-only (ADR 0008): stored encrypted, reported only as set / not set, and never part of a response body, an audit
/// entry or a log line. The full client id never leaves the server either. Every secret in these tests carries a made-up marker.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class AccountsSecretsTests(ApiFactory factory) : IAsyncLifetime
{
    private const string NewKey = "new-key-" + AccountsApi.Marker;

    private const string NewSecret = "new-secret-" + AccountsApi.Marker;

    /// <summary>A client id used by no other test (the Dhan profile's example id is AccountsApi.ClientId).</summary>
    private const string LeakClientId = "5550001234";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAccountsAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<string> TextAsync(HttpResponseMessage response) => response.StatusCode + " " + await response.Content.ReadAsStringAsync(Ct);

    private async Task<string?> RevealAsync(Guid accountId, BrokerSecretName name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var vault = scope.ServiceProvider.GetRequiredService<IBrokerSecretVault>();
        return (await vault.GetAsync(accountId, name, Ct))?.Reveal();
    }

    private async Task<List<(string Name, byte[] Ciphertext, int KeyVersion)>> StoredRowsAsync(Guid accountId)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT name, ciphertext, key_version FROM accounts.broker_secrets WHERE account_id = @id ORDER BY name", connection);
        command.Parameters.AddWithValue("id", accountId);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<(string, byte[], int)>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add((reader.GetString(0), (byte[])reader[1], reader.GetInt32(2)));
        }

        return rows;
    }

    [Fact]
    public async Task The_API_key_and_secret_are_stored_encrypted_and_only_reported_as_set()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;

        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);

        account.Secrets.Select(s => (s.Name, s.Set)).ShouldBe([("apiKey", true), ("apiSecret", true)]);
        var rows = await StoredRowsAsync(account.Id);
        rows.Select(r => (r.Name, r.KeyVersion)).ShouldBe([("api_key", 1), ("api_secret", 1)]);
        rows.ShouldAllBe(r => !System.Text.Encoding.UTF8.GetString(r.Ciphertext).Contains(AccountsApi.Marker, StringComparison.Ordinal));
        (await RevealAsync(account.Id, BrokerSecretName.ApiKey)).ShouldBe(AccountsApi.ApiKey);
        (await RevealAsync(account.Id, BrokerSecretName.ApiSecret)).ShouldBe(AccountsApi.ApiSecret);
    }

    [Fact]
    public async Task Replacing_secrets_needs_step_up_keeps_what_is_left_out_and_is_audited_without_values()
    {
        var elevated = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await elevated.CreateAccountAsync(AccountsApi.NewAccount(apiKeyIssuedOn: "2026-01-15"), Ct);
        var plain = (await factory.SignInAsync(Ct)).Client;
        AccountDetail detail;
        HttpStatusCode refusedStatus;
        factory.Clock.Advance(TimeSpan.FromMinutes(1)); // still inside the step-up window
        try
        {
            using var refused = await plain.PutJsonAsync($"/api/accounts/{account.Id}/secrets", new { apiKey = NewKey }, Ct);
            using var replaced = await elevated.PutJsonAsync($"/api/accounts/{account.Id}/secrets", new { apiKey = NewKey }, Ct);
            refusedStatus = refused.StatusCode;
            detail = await replaced.ReadAsync<AccountDetail>(Ct);
        }
        finally
        {
            factory.Clock.Reset();
        }

        refusedStatus.ShouldBe(HttpStatusCode.Forbidden);
        (await RevealAsync(account.Id, BrokerSecretName.ApiKey)).ShouldBe(NewKey);
        (await RevealAsync(account.Id, BrokerSecretName.ApiSecret)).ShouldBe(AccountsApi.ApiSecret);
        detail.Secrets[0].UpdatedAt!.Value.ShouldBeGreaterThan(account.Secrets[0].UpdatedAt!.Value.AddSeconds(30));
        detail.Secrets[1].UpdatedAt.ShouldBe(account.Secrets[1].UpdatedAt);
        detail.ApiKeyIssuedOn.ShouldNotBe(new DateOnly(2026, 1, 15)); // a new key was generated today
        var activity = await (await elevated.GetAsync($"/api/accounts/{account.Id}/activity", Ct)).ReadAsync<List<AccountEventView>>(Ct);
        activity[0].Event.ShouldBe("SecretsReplaced");
        activity[0].Detail!.ShouldStartWith("API key replaced; API key issued on ");
    }

    [Fact]
    public async Task Empty_or_missing_secrets_are_refused_with_the_field_names()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);

        using var create = await client.PostJsonAsync("/api/accounts", AccountsApi.NewAccount("Example Two", secrets: new { apiKey = " ", apiSecret = (string?)null }), Ct);
        using var replace = await client.PutJsonAsync($"/api/accounts/{account.Id}/secrets", new { apiKey = "", apiSecret = NewSecret }, Ct);
        using var nothing = await client.PutJsonAsync($"/api/accounts/{account.Id}/secrets", new { }, Ct);

        (await create.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["secrets.apiKey", "secrets.apiSecret"], ignoreOrder: true);
        (await replace.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["apiKey"]);
        nothing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RevealAsync(account.Id, BrokerSecretName.ApiSecret)).ShouldBe(AccountsApi.ApiSecret);
    }

    [Fact]
    public async Task Archiving_deletes_the_stored_secrets()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);

        (await client.DeleteAsync($"/api/accounts/{account.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await StoredRowsAsync(account.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task No_response_audit_entry_or_log_line_contains_a_secret_or_the_full_client_id()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var bodies = new List<string>();

        async Task<HttpResponseMessage> Record(Task<HttpResponseMessage> call)
        {
            var response = await call;
            bodies.Add(await TextAsync(response));
            return response;
        }

        var created = await Record(client.PostJsonAsync(
            "/api/accounts",
            AccountsApi.NewAccount(
                clientId: LeakClientId,
                roles: new { data = new { enabled = true, kind = "primary" }, trading = new { enabled = true }, portfolio = new { enabled = true } },
                trading: AccountsApi.Trading(AccountsApi.StandardProfileId),
                portfolio: new { defaultBucket = "Trading" }),
            Ct));
        var id = (await created.ReadAsCreatedAsync<AccountDetail>(Ct)).Id;
        var second = await (await Record(client.PostJsonAsync("/api/accounts", AccountsApi.NewAccount("Example Two", clientId: "1000000002"), Ct))).ReadAsCreatedAsync<AccountDetail>(Ct);
        await Record(client.PostJsonAsync("/api/accounts", AccountsApi.NewAccount(clientId: LeakClientId), Ct)); // conflict body
        await Record(client.PostJsonAsync("/api/accounts", AccountsApi.NewAccount(name: "", clientId: LeakClientId, secrets: new { apiKey = "has space " + AccountsApi.Marker, apiSecret = AccountsApi.ApiSecret }), Ct)); // validation body
        await Record(client.PutJsonAsync($"/api/accounts/{id}/connection", new { name = "Example One", clientId = LeakClientId }, Ct));
        await Record(client.PutJsonAsync($"/api/accounts/{id}/secrets", new { apiKey = NewKey, apiSecret = NewSecret }, Ct));
        await Record(client.PutJsonAsync($"/api/accounts/{id}/secrets", new { apiKey = "two " + AccountsApi.Marker }, Ct)); // validation body
        await Record(client.PutJsonAsync($"/api/accounts/{second.Id}/secrets", new { apiSecret = NewSecret }, Ct));
        await Record(client.PutJsonAsync($"/api/accounts/{id}/trading", AccountsApi.Trading(AccountsApi.StandardProfileId, "manual_live"), Ct));
        await Record(client.PutJsonAsync($"/api/accounts/{id}/portfolio", new { defaultBucket = "MyChoice" }, Ct));
        await Record(client.PutJsonAsync($"/api/accounts/{id}/data", new { liveFeedBudget = 300 }, Ct));
        await Record(client.PutJsonAsync($"/api/accounts/{id}/roles", new { portfolio = new { enabled = false } }, Ct));
        await Record(client.GetAsync("/api/accounts", Ct));
        await Record(client.GetAsync($"/api/accounts/{id}", Ct));
        await Record(client.GetAsync($"/api/accounts/{id}/activity?take=200", Ct));
        await Record(client.GetAsync("/api/brokers/dhan", Ct));
        await Record(client.GetAsync("/api/risk-profiles", Ct));
        await Record(client.DeleteAsync($"/api/accounts/{second.Id}", Ct));

        bodies.Count.ShouldBe(18);
        bodies.ShouldAllBe(b => !b.Contains(AccountsApi.Marker, StringComparison.Ordinal));
        bodies.ShouldAllBe(b => !b.Contains(LeakClientId, StringComparison.Ordinal));
        var details = await factory.WithDbAsync(db => db.AccountEvents.AsNoTracking().Select(e => e.Detail + " " + e.Actor).ToListAsync(Ct));
        details.ShouldNotBeEmpty();
        details.ShouldAllBe(d => !d.Contains(AccountsApi.Marker, StringComparison.Ordinal) && !d.Contains(LeakClientId, StringComparison.Ordinal));
        factory.Logs.All.ShouldNotBeEmpty();
        factory.Logs.All.ShouldAllBe(line => !line.Contains(AccountsApi.Marker, StringComparison.Ordinal));
        factory.Logs.All.ShouldAllBe(line => !line.Contains(LeakClientId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Risk_profile_responses_carry_no_account_data()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        await client.CreateAccountAsync(AccountsApi.NewAccount(trading: AccountsApi.Trading(AccountsApi.StandardProfileId)), Ct);

        var text = await TextAsync(await client.GetAsync("/api/risk-profiles", Ct));

        text.ShouldNotContain("Example One");
        text.ShouldNotContain(AccountsApi.ClientId);
        (await (await client.GetAsync("/api/risk-profiles", Ct)).ReadAsync<List<RiskProfileView>>(Ct)).Single(p => p.Name == "Standard").AccountCount.ShouldBe(1);
    }
}
