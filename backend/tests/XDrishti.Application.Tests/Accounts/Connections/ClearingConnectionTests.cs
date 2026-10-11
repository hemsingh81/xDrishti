using System.Text.Json;
using XDrishti.Application.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Accounts.Connections;

/// <summary>A token belongs to the client id it was issued for: a new client id, or archiving, clears the broker connection.</summary>
public sealed class ClearingConnectionTests
{
    private const string NewClientId = "1000000077";

    private readonly ConnectionsFixture _fx = new();

    private async Task<AccountDetail> UpdateConnectionAsync(BrokerAccount account, string? clientId, string name = "Example One")
    {
        var result = await _fx.Accounts.UpdateConnection().HandleAsync(
            new UpdateConnectionCommand(account.Id, Inputs.Connection(name, clientId), AccountsFixture.Actor), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private string? LastConnectionUpdate(Guid accountId) =>
        _fx.Data.Events.Last(e => e.AccountId == accountId && e.Event == AccountEventType.ConnectionUpdated).Detail;

    [Fact]
    public async Task A_new_client_id_deletes_the_token_resets_the_connection_and_closes_open_sign_ins()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.ConnectByLoginAsync(account);
        await _fx.StartAsync(account.Id); // a sign-in still open for the old client id

        var detail = await UpdateConnectionAsync(account, NewClientId);

        detail.Connection.Status.ShouldBe("not_connected");
        detail.TokenStatus.ShouldBe("not_connected");
        detail.Connection.LastConnectedAt.ShouldBe(AccountsFixture.Start);
        _fx.Stored(account.Id, BrokerSecretName.AccessToken).ShouldBeNull();
        _fx.Stored(account.Id, BrokerSecretName.ApiKey).ShouldNotBeNull();
        _fx.Connections.Pending.ShouldAllBe(p => p.ConsumedAt != null);
        LastConnectionUpdate(account.Id).ShouldBe("Connection: client id changed; broker connection cleared");
        JsonSerializer.Serialize(detail).ShouldNotContain(FakeConnector.Marker);
    }

    [Fact]
    public async Task An_expired_connection_is_cleared_too()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.ConnectByLoginAsync(account);
        _fx.Clock.Advance(TimeSpan.FromHours(25));

        var detail = await UpdateConnectionAsync(account, NewClientId);

        detail.Connection.Status.ShouldBe("not_connected");
        _fx.Stored(account.Id, BrokerSecretName.AccessToken).ShouldBeNull();
        LastConnectionUpdate(account.Id)!.ShouldContain("broker connection cleared");
    }

    [Fact]
    public async Task Keeping_the_client_id_keeps_the_connection()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.ConnectByLoginAsync(account);

        var detail = await UpdateConnectionAsync(account, AccountsFixture.ClientId, "Renamed One");

        detail.Connection.Status.ShouldBe("connected");
        _fx.Stored(account.Id, BrokerSecretName.AccessToken).ShouldNotBeNull();
        LastConnectionUpdate(account.Id).ShouldBe("Connection: name 'Example One' -> 'Renamed One'");
    }

    [Fact]
    public async Task A_new_client_id_on_an_account_that_is_not_connected_clears_nothing()
    {
        var account = await _fx.DhanAccountAsync();

        await UpdateConnectionAsync(account, NewClientId);

        LastConnectionUpdate(account.Id).ShouldBe("Connection: client id changed");
        _fx.Connection(account.Id).ShouldBeNull();
    }

    [Fact]
    public async Task Archiving_deletes_tokens_and_sign_ins_and_resets_the_connection()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.ConnectByLoginAsync(account);
        await _fx.StartAsync(account.Id);

        (await _fx.Accounts.Archive().HandleAsync(new ArchiveAccountCommand(account.Id, AccountsFixture.Actor), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        _fx.Data.Secrets.Keys.ShouldNotContain(k => k.AccountId == account.Id);
        _fx.Connection(account.Id)!.Status.ShouldBe(ConnectionStatus.NotConnected);
        _fx.Connections.Pending.ShouldNotContain(p => p.AccountId == account.Id);
    }
}
