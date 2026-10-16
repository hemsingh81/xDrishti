using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Accounts.Connections;

/// <summary>The connection handlers on in-memory ports, a scripted Dhan and a controllable clock (start: 2026-10-10 14:30 IST).</summary>
internal sealed class ConnectionsFixture
{
    public static readonly Guid Session = Guid.Parse("00000000-0000-7000-8000-0000000005e1");

    public static readonly Guid OtherSession = Guid.Parse("00000000-0000-7000-8000-0000000005e2");

    public AccountsFixture Accounts { get; } = new();

    public FakeConnector Dhan { get; } = new();

    public InMemoryAccountData Data => Accounts.Data;

    public InMemoryConnectionData Connections => Accounts.Connections;

    public Microsoft.Extensions.Time.Testing.FakeTimeProvider Clock => Accounts.Clock;

    private ConnectionWriter Writer => new(Data, Connections, Data);

    public StartConnectHandler StartHandler() =>
        new(Data, Connections, Data, Writer, Dhan, Clock, NullLogger<StartConnectHandler>.Instance);

    public CompleteConnectHandler CompleteHandler() =>
        new(Data, Connections, Data, Data, Writer, Dhan, Accounts.Attention, Clock, NullLogger<CompleteConnectHandler>.Instance);

    public DisconnectHandler DisconnectHandler() => new(Data, Data, Connections, Data, Data, Writer, Accounts.Views, Clock, NullLogger<DisconnectHandler>.Instance);

    public Task<BrokerAccount> DhanAccountAsync() => Accounts.CreatedAsync();

    public async Task<Result<ConnectStartView>> StartAsync(Guid accountId, Guid? session = null) =>
        await StartHandler().HandleAsync(new StartConnectCommand(accountId, session ?? Session, AccountsFixture.Actor), CancellationToken.None);

    public async Task<ConnectReturn> CompleteAsync(string? code, Guid? session = null, bool signedIn = true)
    {
        var result = await CompleteHandler().HandleAsync(
            new CompleteConnectCommand(signedIn ? session ?? Session : null, code, AccountsFixture.Actor), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    /// <summary>Connects through the sign-in flow.</summary>
    public async Task ConnectByLoginAsync(BrokerAccount account)
    {
        (await StartAsync(account.Id)).IsSuccess.ShouldBeTrue();
        (await CompleteAsync("code-" + FakeConnector.Marker)).ErrorCode.ShouldBeNull();
    }

    public async Task<Result<AccountDetail>> DisconnectAsync(Guid accountId) =>
        await DisconnectHandler().HandleAsync(new DisconnectCommand(accountId, AccountsFixture.Actor), CancellationToken.None);

    public string? Stored(Guid accountId, BrokerSecretName name) =>
        Data.Secrets.TryGetValue((accountId, name), out var entry) ? entry.Value.Reveal() : null;

    public BrokerConnection? Connection(Guid accountId) => Connections.Connections.SingleOrDefault(c => c.AccountId == accountId);

    public List<AccountEvent> Events(AccountEventType type) => [.. Data.Events.Where(e => e.Event == type)];
}
