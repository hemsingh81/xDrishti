using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Api.IntegrationTests.Accounts;

/// <summary>Disconnecting, expiry, and clearing the connection when the client id changes or the account is archived (specs P1-16c, P1-16d).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class BrokerDisconnectTests(ApiFactory factory) : IAsyncLifetime
{
    private const string TokenId = "tid-" + AccountsApi.Marker;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAccountsAsync();
        factory.Brokers.Reset();
    }

    public async ValueTask DisposeAsync()
    {
        factory.Brokers.Reset();
        await factory.ResetAccountsAsync();
    }

    private List<FakeBrokers.RecordedRequest> Sent(string path) => [.. factory.Brokers.Requests.Where(r => r.Uri.AbsolutePath == path)];

    private async Task<string?> RevealAsync(Guid accountId, BrokerSecretName name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IBrokerSecretVault>().GetAsync(accountId, name, Ct))?.Reveal();
    }

    // The account's hand-over row (ADR 0171): the sealed token and its key version, or null when there is none.
    private async Task<(byte[] Sealed, int KeyVersion)?> HandoverAsync(Guid accountId)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT sealed_token, key_version FROM accounts.exec_token_handover WHERE account_id = @id", connection);
        command.Parameters.AddWithValue("id", accountId);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        return await reader.ReadAsync(Ct) ? (reader.GetFieldValue<byte[]>(0), reader.GetInt32(1)) : null;
    }

    private static async Task<AccountDetail> ConnectAsync(HttpClient client, Guid accountId)
    {
        await client.StartConnectAsync(accountId, Ct);
        (await client.DhanCallbackAsync(TokenId, Ct)).Location().ShouldBe(ConnectApi.Connected(accountId));
        return await client.GetAccountAsync(accountId, Ct);
    }

    [Fact]
    public async Task The_status_reads_as_expired_after_the_expiry_without_a_broker_call()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await ConnectAsync(client, account.Id);
        var calls = factory.Brokers.Requests.Count;
        await factory.WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE accounts.broker_connections SET expires_at = now() - interval '1 minute' WHERE account_id = {account.Id}", Ct));

        var detail = await client.GetAccountAsync(account.Id, Ct);
        var list = await (await client.GetAsync("/api/accounts", Ct)).ReadAsync<List<AccountSummary>>(Ct);

        detail.Connection.Status.ShouldBe("expired");
        detail.TokenStatus.ShouldBe("expired");
        list.Single().TokenStatus.ShouldBe("expired");
        factory.Brokers.Requests.Count.ShouldBe(calls);
    }

    [Fact]
    public async Task A_connect_hands_the_token_over_sealed_with_its_own_key_and_a_disconnect_deletes_the_hand_over()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await ConnectAsync(client, account.Id);
        var (sealedToken, keyVersion) = (await HandoverAsync(account.Id)).ShouldNotBeNull();

        new SecretCipher(factory.HandoverKey, SecretCipherPurpose.ExecHandover).TryDecrypt(account.Id, "access_token", keyVersion, sealedToken, out var token).ShouldBeTrue();
        token.Reveal().ShouldBe(FakeBrokers.DhanAccessToken);
        new SecretCipher(factory.VaultKey, SecretCipherPurpose.BrokerVault).TryDecrypt(account.Id, "access_token", keyVersion, sealedToken, out _).ShouldBeFalse();
        new SecretCipher(factory.HandoverKey, SecretCipherPurpose.ExecHandover).TryDecrypt(Guid.NewGuid(), "access_token", keyVersion, sealedToken, out _).ShouldBeFalse();

        (await client.DeleteAsync($"/api/accounts/{account.Id}/token", Ct)).EnsureSuccessStatusCode();

        (await HandoverAsync(account.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Disconnecting_deletes_the_token_keeps_the_app_secrets_and_calls_no_broker()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await ConnectAsync(client, account.Id);
        (await RevealAsync(account.Id, BrokerSecretName.AccessToken)).ShouldBe(FakeBrokers.DhanAccessToken);
        var calls = factory.Brokers.Requests.Count;

        using var response = await client.DeleteAsync($"/api/accounts/{account.Id}/token", Ct);
        var detail = await response.ReadAsync<AccountDetail>(Ct);

        detail.Connection.Status.ShouldBe("not_connected");
        (await RevealAsync(account.Id, BrokerSecretName.AccessToken)).ShouldBeNull();
        (await RevealAsync(account.Id, BrokerSecretName.ApiSecret)).ShouldBe(AccountsApi.ApiSecret);
        factory.Brokers.Requests.Count.ShouldBe(calls);
    }

    [Fact]
    public async Task A_new_client_id_clears_the_connection_and_its_tokens_in_the_same_change()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await ConnectAsync(client, account.Id);
        await client.StartConnectAsync(account.Id, Ct); // a Dhan sign-in still open for the old client id
        var calls = factory.Brokers.Requests.Count;

        using var response = await client.PutJsonAsync($"/api/accounts/{account.Id}/connection", new { name = "Example One", clientId = "1000000077", color = "#1A2B3C", notes = "made-up account" }, Ct);
        var detail = await response.ReadAsync<AccountDetail>(Ct);
        using var lateCallback = await client.DhanCallbackAsync(TokenId, Ct);

        detail.MaskedClientId.ShouldBe("1000••••77");
        detail.Connection.Status.ShouldBe("not_connected");
        detail.TokenStatus.ShouldBe("not_connected");
        (await RevealAsync(account.Id, BrokerSecretName.AccessToken)).ShouldBeNull();
        (await RevealAsync(account.Id, BrokerSecretName.ApiKey)).ShouldBe(AccountsApi.ApiKey);
        (await HandoverAsync(account.Id)).ShouldBeNull();
        lateCallback.Location().ShouldBe(ConnectApi.Failed(null, "accounts.connect_expired"));
        factory.Brokers.Requests.Count.ShouldBe(calls); // no broker call, and the old sign-in never completed
        var activity = await (await client.GetAsync($"/api/accounts/{account.Id}/activity", Ct)).ReadAsync<List<AccountEventView>>(Ct);
        activity[0].Event.ShouldBe("ConnectionUpdated");
        activity[0].Detail.ShouldBe("Connection: client id changed; broker connection cleared");
    }

    [Fact]
    public async Task Archiving_clears_tokens_connection_and_pending_sign_ins()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await ConnectAsync(client, account.Id);
        await client.StartConnectAsync(account.Id, Ct);

        (await client.DeleteAsync($"/api/accounts/{account.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var (secrets, status, pending) = await factory.WithDbAsync(async db => (
            await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM accounts.broker_secrets WHERE account_id = {account.Id}").SingleAsync(Ct),
            await db.BrokerConnections.AsNoTracking().Where(c => c.AccountId == account.Id).Select(c => c.Status).SingleAsync(Ct),
            await db.PendingConnects.AsNoTracking().CountAsync(p => p.AccountId == account.Id, Ct)));
        secrets.ShouldBe(0);
        (await HandoverAsync(account.Id)).ShouldBeNull();
        status.ShouldBe(ConnectionStatus.NotConnected);
        pending.ShouldBe(0);
    }
}
