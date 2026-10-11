using System.Text.Json;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Application.Auth;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Accounts.Connections;

public sealed class CompleteConnectHandlerTests
{
    private const string Code = "tid-" + FakeConnector.Marker;

    private readonly ConnectionsFixture _fx = new();

    [Fact]
    public async Task The_sessions_open_sign_in_is_exchanged_stored_encrypted_and_audited()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.StartAsync(account.Id);

        var outcome = await _fx.CompleteAsync(Code);

        outcome.ShouldBe(new ConnectReturn(account.Id, null));
        var completion = _fx.Dhan.Completions.Single();
        completion.Code.Reveal().ShouldBe(Code);
        completion.App.ApiSecret.Reveal().ShouldBe(AccountsFixture.ApiSecret);
        _fx.Stored(account.Id, BrokerSecretName.AccessToken).ShouldBe($"access-1-{FakeConnector.Marker}");
        var connection = _fx.Connection(account.Id)!;
        (connection.Status, connection.ExpiresAt, connection.BrokerUserId, connection.BrokerUserName)
            .ShouldBe((ConnectionStatus.Connected, (DateTimeOffset?)AccountsFixture.Start.AddHours(24), (string?)AccountsFixture.ClientId, (string?)"Example Holder"));
        _fx.Connections.Pending.Single().ConsumedAt.ShouldBe(AccountsFixture.Start);
        var entry = _fx.Events(AccountEventType.BrokerConnected).Single();
        entry.Detail.ShouldBe("Connected by signing in at Dhan as Example Holder; valid until 2026-10-11 14:30 IST");
        entry.Actor.ShouldBe(AccountsFixture.Actor);
    }

    [Fact]
    public async Task The_expiry_Dhan_states_is_used()
    {
        var account = await _fx.DhanAccountAsync();
        _fx.Dhan.StatedExpiry = AccountsFixture.Start.AddHours(23);
        await _fx.StartAsync(account.Id);

        await _fx.CompleteAsync(Code);

        _fx.Connection(account.Id)!.ExpiresAt.ShouldBe(AccountsFixture.Start.AddHours(23));
    }

    [Fact]
    public async Task The_same_sign_in_cannot_be_used_twice()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.StartAsync(account.Id);
        await _fx.CompleteAsync(Code);

        var replay = await _fx.CompleteAsync(Code);

        replay.ShouldBe(new ConnectReturn(null, ConnectionErrors.ConnectExpiredCode));
        _fx.Dhan.Completions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Another_session_cannot_use_the_sign_in()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.StartAsync(account.Id);

        var outcome = await _fx.CompleteAsync(Code, ConnectionsFixture.OtherSession);

        outcome.ShouldBe(new ConnectReturn(null, ConnectionErrors.ConnectExpiredCode));
        _fx.Connections.Pending.Single().ConsumedAt.ShouldBeNull();
        _fx.Dhan.Completions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_sign_in_that_comes_back_after_ten_minutes_is_refused_and_closed()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.StartAsync(account.Id);
        _fx.Clock.Advance(TimeSpan.FromMinutes(10));

        var outcome = await _fx.CompleteAsync(Code);

        outcome.ShouldBe(new ConnectReturn(account.Id, ConnectionErrors.ConnectExpiredCode));
        _fx.Dhan.Completions.ShouldBeEmpty();
        _fx.Connections.Pending.Single().ConsumedAt.ShouldNotBeNull();
        _fx.Connection(account.Id)!.LastError.ShouldBe("the sign-in expired");
    }

    [Fact]
    public async Task Without_a_signed_in_session_nothing_happens()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.StartAsync(account.Id);

        var outcome = await _fx.CompleteAsync(Code, signedIn: false);

        outcome.ShouldBe(new ConnectReturn(null, AuthErrors.UnauthenticatedCode));
        _fx.Connections.Pending.Single().ConsumedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Without_an_open_sign_in_nothing_happens()
    {
        await _fx.DhanAccountAsync();

        var outcome = await _fx.CompleteAsync(Code);

        outcome.ShouldBe(new ConnectReturn(null, ConnectionErrors.ConnectExpiredCode));
        _fx.Dhan.Completions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_sign_in_of_another_client_id_stores_nothing()
    {
        var account = await _fx.DhanAccountAsync();
        _fx.Dhan.HolderId = "1000000099";
        await _fx.StartAsync(account.Id);

        var outcome = await _fx.CompleteAsync(Code);

        outcome.ShouldBe(new ConnectReturn(account.Id, ConnectionErrors.ConnectMismatchCode));
        _fx.Stored(account.Id, BrokerSecretName.AccessToken).ShouldBeNull();
        _fx.Connection(account.Id)!.StatusAt(_fx.Clock.GetUtcNow()).ShouldBe(ConnectionStatus.NotConnected);
        _fx.Connection(account.Id)!.LastError.ShouldBe("The Dhan sign-in belongs to another client id; nothing was stored");
        _fx.Events(AccountEventType.BrokerConnectFailed).Count.ShouldBe(1);
        _fx.Events(AccountEventType.BrokerConnectFailed).Single().Detail!.ShouldNotContain("1000000099");
    }

    [Fact]
    public async Task A_refused_tokenId_keeps_the_current_token_and_notes_only_the_broker_code()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.ConnectByLoginAsync(account);
        _fx.Dhan.CompleteFailure = new BrokerFailure(BrokerFailureKind.Rejected, "DH-901");
        await _fx.StartAsync(account.Id);

        var outcome = await _fx.CompleteAsync(Code);

        outcome.ShouldBe(new ConnectReturn(account.Id, ConnectionErrors.BrokerErrorCode));
        _fx.Stored(account.Id, BrokerSecretName.AccessToken).ShouldBe($"access-1-{FakeConnector.Marker}");
        var connection = _fx.Connection(account.Id)!;
        connection.StatusAt(_fx.Clock.GetUtcNow()).ShouldBe(ConnectionStatus.Connected);
        connection.LastError.ShouldBe("Sign-in failed: Dhan refused the request (error DH-901).");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Coming_back_without_a_tokenId_is_a_cancelled_sign_in(string? code)
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.StartAsync(account.Id);

        var outcome = await _fx.CompleteAsync(code);

        outcome.ShouldBe(new ConnectReturn(account.Id, ConnectionErrors.ConnectCancelledCode));
        _fx.Dhan.Completions.ShouldBeEmpty();
        _fx.Connections.Pending.Single().ConsumedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Without_the_API_secret_the_exchange_is_not_attempted()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.StartAsync(account.Id);
        _fx.Data.Secrets.Remove((account.Id, BrokerSecretName.ApiSecret));

        var outcome = await _fx.CompleteAsync(Code);

        outcome.ShouldBe(new ConnectReturn(account.Id, ConnectionErrors.AppNotConfiguredCode));
        _fx.Dhan.Completions.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_secret_token_or_code_appears_in_the_audit_trail_the_view_or_the_command_text()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.StartAsync(account.Id);
        var command = new CompleteConnectCommand(ConnectionsFixture.Session, Code, AccountsFixture.Actor);

        await _fx.CompleteAsync(Code);
        var detail = await _fx.Accounts.GetAccount().HandleAsync(new GetAccountQuery(account.Id), CancellationToken.None);

        JsonSerializer.Serialize(detail.Value).ShouldNotContain(FakeConnector.Marker);
        JsonSerializer.Serialize(detail.Value).ShouldNotContain(AccountsFixture.ClientId);
        detail.Value.Connection.BrokerUserId.ShouldBe("1000••••01");
        _fx.Data.Events.ShouldAllBe(e => !(e.Detail ?? string.Empty).Contains(FakeConnector.Marker, StringComparison.Ordinal));
        command.ToString().ShouldNotContain(FakeConnector.Marker);
    }
}
