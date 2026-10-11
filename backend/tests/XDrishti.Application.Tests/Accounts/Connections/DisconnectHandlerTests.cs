using XDrishti.Application.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Accounts.Connections;

public sealed class DisconnectHandlerTests
{
    private readonly ConnectionsFixture _fx = new();

    [Fact]
    public async Task Disconnecting_deletes_the_access_token_keeps_the_app_secrets_and_is_audited()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.ConnectByLoginAsync(account);

        var result = await _fx.DisconnectAsync(account.Id);

        _fx.Stored(account.Id, BrokerSecretName.AccessToken).ShouldBeNull();
        _fx.Stored(account.Id, BrokerSecretName.ApiKey).ShouldNotBeNull();
        _fx.Stored(account.Id, BrokerSecretName.ApiSecret).ShouldNotBeNull();
        result.Value.Connection.Status.ShouldBe("not_connected");
        result.Value.Connection.LastConnectedAt.ShouldBe(AccountsFixture.Start);
        _fx.Events(AccountEventType.BrokerDisconnected).Single().Detail.ShouldBe("Disconnected from Dhan; stored access token deleted");
    }

    [Fact]
    public async Task Disconnecting_an_account_that_is_not_connected_does_nothing()
    {
        var account = await _fx.DhanAccountAsync();

        var result = await _fx.DisconnectAsync(account.Id);

        result.Value.Connection.Status.ShouldBe("not_connected");
        _fx.Events(AccountEventType.BrokerDisconnected).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_account_is_not_found() =>
        (await _fx.DisconnectAsync(Guid.NewGuid())).Error.Code.ShouldBe(AccountErrors.NotFoundCode);

    [Fact]
    public async Task The_status_reads_as_expired_after_the_expiry_without_any_call()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.ConnectByLoginAsync(account);
        _fx.Clock.Advance(TimeSpan.FromHours(24)); // Dhan states no expiry in the fake: 24 hours

        var detail = await _fx.Accounts.GetAccount().HandleAsync(new GetAccountQuery(account.Id), CancellationToken.None);
        var list = await _fx.Accounts.ListAccounts().HandleAsync(new ListAccountsQuery(), CancellationToken.None);

        detail.Value.Connection.Status.ShouldBe("expired");
        detail.Value.TokenStatus.ShouldBe("expired");
        list.Single().TokenStatus.ShouldBe("expired");
        _fx.Dhan.Starts.Count.ShouldBe(1); // only the sign-in itself called Dhan
    }
}
