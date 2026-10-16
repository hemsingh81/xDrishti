using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>
/// Starts every holdings test with no accounts, a fake Dhan without holdings and the clock pinned to Monday 2026-10-12 10:00 India time (inside market
/// hours), with the owner signed in (and stepped up, for creating and connecting accounts) after the pin.
/// </summary>
public abstract class HoldingsTestBase(ApiFactory factory) : IAsyncLifetime
{
    /// <summary>Monday 2026-10-12 10:00 IST.</summary>
    protected static readonly DateTimeOffset MondayMorning = new(2026, 10, 12, 4, 30, 0, TimeSpan.Zero);

    protected const string TokenId = "tid-" + AccountsApi.Marker;

    protected ApiFactory Factory { get; } = factory;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected SignedInClient Owner { get; set; } = null!;

    protected HttpClient Client => Owner.Client;

    protected FakeBrokers Dhan => Factory.Brokers;

    public virtual async ValueTask InitializeAsync()
    {
        await Factory.ResetAccountsAsync();
        await Factory.ResetInstrumentsAsync();
        Factory.Brokers.Reset();
        Factory.Clock.Pin(MondayMorning);
        Owner = await Factory.SignInElevatedAsync(Ct);
    }

    public virtual async ValueTask DisposeAsync()
    {
        Factory.Brokers.Reset();
        await Factory.ResetClockAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>An account with the Portfolio role on (or the given <paramref name="roles"/>), connected to the fake Dhan through the real sign-in flow.</summary>
    protected async Task<AccountDetail> ConnectedAccountAsync(string name = "Example One", string clientId = AccountsApi.ClientId, object? portfolio = null, object? roles = null)
    {
        Factory.Brokers.DhanClientId = clientId;
        var account = await Client.CreateAccountAsync(AccountsApi.NewAccount(name, clientId, roles: roles ?? new { portfolio = new { enabled = true } }, portfolio: portfolio), Ct);
        await Client.StartConnectAsync(account.Id, Ct);
        using var callback = await Client.DhanCallbackAsync(TokenId, Ct);
        callback.Location().ShouldBe(ConnectApi.Connected(account.Id));
        return account;
    }

    protected async Task<AccountDetail> UnconnectedAccountAsync(string name = "Example One", string clientId = AccountsApi.ClientId, bool portfolioRole = true) =>
        await Client.CreateAccountAsync(AccountsApi.NewAccount(name, clientId, roles: portfolioRole ? new { portfolio = new { enabled = true } } : null), Ct);

    protected static FakeBrokers.FakeHolding Holding(string symbol, long qty, decimal avgCost, decimal ltp, string securityId, string? isin, string exchange = "ALL") =>
        new(securityId, symbol, isin, qty, avgCost, ltp, exchange);

    protected List<FakeBrokers.RecordedRequest> HoldingRequests => [.. Dhan.Requests.Where(r => r.Uri.AbsolutePath == "/v2/holdings")];

    /// <summary>Imports every holding the fake Dhan reports, all dated <paramref name="date"/>.</summary>
    protected async Task ImportAllAsync(Guid accountId, string date)
    {
        var rows = Dhan.Holdings.Select(h => new { key = h.Isin ?? $"{h.SecurityId}:{h.Exchange}", acquiredOn = date }).ToArray();
        (await Client.ImportAsync(accountId, date, rows, Ct)).Imported.ShouldBe(rows.Length);
    }

    /// <summary>No made-up secret (marker, access token) may be part of a response body, audit text or log line.</summary>
    protected static void AssertNoSecret(string text)
    {
        text.ShouldNotContain(AccountsApi.Marker);
        text.ShouldNotContain(FakeBrokers.DhanAccessToken);
    }
}
