using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Accounts.Connections;

public sealed class StartConnectHandlerTests
{
    private readonly ConnectionsFixture _fx = new();

    [Fact]
    public async Task Dhan_is_asked_for_a_consent_and_a_ten_minute_sign_in_is_opened_for_the_session()
    {
        var account = await _fx.DhanAccountAsync();

        var result = await _fx.StartAsync(account.Id);

        result.Value.AuthorizeUrl.ShouldBe("https://login.example.invalid/dhan?n=1");
        result.Value.ExpiresAt.ShouldBe(AccountsFixture.Start.AddMinutes(10));
        var start = _fx.Dhan.Starts.Single();
        start.ClientId.ShouldBe(AccountsFixture.ClientId);
        start.App.ApiKey.Reveal().ShouldBe(AccountsFixture.ApiKey);
        start.App.ApiSecret.Reveal().ShouldBe(AccountsFixture.ApiSecret);
        var pending = _fx.Connections.Pending.Single();
        (pending.AccountId, pending.SessionId, pending.ConsumedAt).ShouldBe((account.Id, ConnectionsFixture.Session, (DateTimeOffset?)null));
    }

    [Fact]
    public async Task A_newer_sign_in_of_the_session_replaces_the_older_one()
    {
        var account = await _fx.DhanAccountAsync();

        await _fx.StartAsync(account.Id);
        await _fx.StartAsync(account.Id);

        _fx.Connections.Pending.Count.ShouldBe(2);
        _fx.Connections.Pending[0].ConsumedAt.ShouldNotBeNull();
        _fx.Connections.Pending[1].ConsumedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Dhan_refuses_the_26th_consent_of_an_Indian_day_until_midnight_IST()
    {
        var account = await _fx.DhanAccountAsync();
        for (var i = 0; i < 25; i++)
        {
            (await _fx.StartAsync(account.Id)).IsSuccess.ShouldBeTrue();
        }

        var refused = await _fx.StartAsync(account.Id);
        _fx.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 18, 30, 0, TimeSpan.Zero)); // 00:00 IST on the 11th
        var nextDay = await _fx.StartAsync(account.Id);

        refused.Error.Code.ShouldBe(ConnectionErrors.ConsentLimitCode);
        refused.Error.Kind.ShouldBe(ErrorKind.RateLimited);
        refused.Error.RetryAfterSeconds.ShouldBe(34_200); // 14:30 IST → midnight
        nextDay.IsSuccess.ShouldBeTrue();
        _fx.Dhan.Starts.Count.ShouldBe(26);
    }

    [Fact]
    public async Task Without_the_API_secret_nothing_is_started()
    {
        var account = await _fx.DhanAccountAsync();
        _fx.Data.Secrets.Remove((account.Id, BrokerSecretName.ApiSecret));

        var result = await _fx.StartAsync(account.Id);

        result.Error.Code.ShouldBe(ConnectionErrors.AppNotConfiguredCode);
        _fx.Dhan.Starts.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_broker_refusal_is_a_safe_502_and_opens_nothing()
    {
        var account = await _fx.DhanAccountAsync();
        _fx.Dhan.StartFailure = new BrokerFailure(BrokerFailureKind.Rejected, "DH-901");

        var result = await _fx.StartAsync(account.Id);

        result.Error.Code.ShouldBe(ConnectionErrors.BrokerErrorCode);
        result.Error.Kind.ShouldBe(ErrorKind.Upstream);
        result.Error.Message.ShouldBe("Dhan refused the request (error DH-901).");
        _fx.Connections.Pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_account_is_not_found() =>
        (await _fx.StartAsync(Guid.NewGuid())).Error.Code.ShouldBe(AccountErrors.NotFoundCode);

    [Fact]
    public async Task Sign_ins_older_than_two_days_are_purged()
    {
        var account = await _fx.DhanAccountAsync();
        await _fx.StartAsync(account.Id);
        _fx.Clock.Advance(TimeSpan.FromDays(3));

        await _fx.StartAsync(account.Id);

        _fx.Connections.Pending.Count.ShouldBe(1);
    }
}
