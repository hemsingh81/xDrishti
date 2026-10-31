using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Accounts.Connections;

/// <summary>
/// ADR 0171 step 2: the execution service's sealed hand-over of the access token is written and removed with the vault's token, in the same unit of work. There is no
/// connect without a hand-over and no hand-over after a disconnect, a cleared connection or an archive.
/// </summary>
public sealed class ExecTokenHandoverTests
{
    private const string Code = "tid-" + FakeConnector.Marker;

    private readonly ConnectionsFixture _fx = new();

    [Fact]
    public async Task Connecting_hands_over_the_same_access_token_the_vault_stores()
    {
        var account = await _fx.DhanAccountAsync();

        await _fx.ConnectByLoginAsync(account);

        _fx.HandedOver(account.Id).ShouldBe($"access-1-{FakeConnector.Marker}");
        _fx.HandedOver(account.Id).ShouldBe(_fx.Stored(account.Id, BrokerSecretName.AccessToken));
        _fx.Data.Handovers[account.Id].At.ShouldBe(AccountsFixture.Start);
    }

    [Fact]
    public async Task A_new_login_replaces_the_hand_over()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.ConnectByLoginAsync(account);
        _fx.Clock.Advance(TimeSpan.FromHours(1));

        await _fx.ConnectByLoginAsync(account);

        _fx.HandedOver(account.Id).ShouldBe($"access-2-{FakeConnector.Marker}");
        _fx.Data.Handovers[account.Id].At.ShouldBe(AccountsFixture.Start.AddHours(1));
    }

    [Fact]
    public async Task Disconnecting_deletes_the_hand_over()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.ConnectByLoginAsync(account);

        (await _fx.DisconnectAsync(account.Id)).IsSuccess.ShouldBeTrue();

        _fx.HandedOver(account.Id).ShouldBeNull();
    }

    [Fact]
    public async Task Clearing_the_connection_for_a_new_client_id_deletes_the_hand_over()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.ConnectByLoginAsync(account);

        var result = await _fx.Accounts.UpdateConnection().HandleAsync(
            new UpdateConnectionCommand(account.Id, Inputs.Connection("Example One", "1000000077"), AccountsFixture.Actor), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _fx.HandedOver(account.Id).ShouldBeNull();
    }

    [Fact]
    public async Task Archiving_deletes_the_hand_over()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.ConnectByLoginAsync(account);

        (await _fx.Accounts.Archive().HandleAsync(new ArchiveAccountCommand(account.Id, AccountsFixture.Actor), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        _fx.HandedOver(account.Id).ShouldBeNull();
    }

    [Fact]
    public async Task When_the_hand_over_cannot_be_written_the_connect_fails_and_nothing_is_saved()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.StartAsync(account.Id);
        var saves = _fx.Data.SaveCount;
        _fx.Data.FailHandoverWith = new InvalidOperationException("hand-over write failed");

        await Should.ThrowAsync<InvalidOperationException>(async () => await _fx.CompleteHandler().HandleAsync(
            new CompleteConnectCommand(ConnectionsFixture.Session, Code, AccountsFixture.Actor), CancellationToken.None));

        _fx.Data.SaveCount.ShouldBe(saves);
        _fx.HandedOver(account.Id).ShouldBeNull();
        (_fx.Connection(account.Id)?.Status ?? ConnectionStatus.NotConnected).ShouldBe(ConnectionStatus.NotConnected);
        _fx.Events(AccountEventType.BrokerConnected).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_connect_whose_save_collides_leaves_no_hand_over()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.StartAsync(account.Id);
        _fx.Data.CollidingSaves = 1;

        var outcome = await _fx.CompleteAsync(Code);

        outcome.ErrorCode.ShouldBe(AccountErrors.ConflictCode);
        _fx.HandedOver(account.Id).ShouldBeNull();
        _fx.Stored(account.Id, BrokerSecretName.AccessToken).ShouldBeNull();
    }
}
