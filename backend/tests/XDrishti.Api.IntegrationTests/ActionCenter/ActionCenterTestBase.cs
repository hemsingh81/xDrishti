using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using XDrishti.Api.IntegrationTests.Holdings;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Actions;
using XDrishti.Application.Portfolio.Alerts;

namespace XDrishti.Api.IntegrationTests.ActionCenter;

/// <summary>
/// Shared set-up of the Action-center and price-alert tests (P2-09): one connected account that is also the primary Data account (so the quote poll can run), holdings in the master with sectors, and the calls of the API. Clock: Monday 2026-10-12 10:00 India time.
/// <code>
/// AAA  Banks  100 x 100 -> 70   below cost -30 %        7,000   (security 1001)
/// BBB  Banks   10 x 100 -> 100  flat, in basket Core    1,000   (security 1002)
/// CCC  IT      10 x 100 -> 140  gain +40 %              1,400   (security 1003)
/// </code>
/// All three were bought on 2024-10-12. The account holds 9,400: AAA is 74.5 % of it and Banks 85.1 %.
/// </summary>
public abstract class ActionCenterTestBase(ApiFactory factory) : PortfolioHistoryTestBase(factory)
{
    protected const string Service = "holdings-quotes";

    protected static readonly object BothRoles = new { data = new { enabled = true, kind = "primary" }, portfolio = new { enabled = true } };

    protected AccountDetail Account { get; private set; } = null!;

    protected Guid Aaa { get; private set; }

    protected Guid Bbb { get; private set; }

    protected Guid Ccc { get; private set; }

    protected Guid Core { get; private set; }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await Factory.ResetServicesAsync();
        Aaa = await ShareAsync("AAA", "1001", "INE000A01011");
        Bbb = await ShareAsync("BBB", "1002", "INE000B01012");
        Ccc = await ShareAsync("CCC", "1003", "INE000C01013");
        await ExecuteAsync("UPDATE market.instruments SET sector = 'Banks' WHERE id = @a OR id = @b", ("a", Aaa), ("b", Bbb));
        await ExecuteAsync("UPDATE market.instruments SET sector = 'IT' WHERE id = @c", ("c", Ccc));
        Account = await ConnectedAccountAsync(roles: BothRoles);
        Dhan.Holdings.Clear();
        Dhan.Holdings.AddRange(
        [
            Holding("AAA", 100, 100m, 70m, "1001", "INE000A01011"),
            Holding("BBB", 10, 100m, 100m, "1002", "INE000B01012"),
            Holding("CCC", 10, 100m, 140m, "1003", "INE000C01013"),
        ]);
        await ImportAllAsync(Account.Id, "2024-10-12");
        Core = await CreateBasketAsync("Core");
        using var members = await Client.PostJsonAsync($"/api/baskets/{Core}/members", new { instrumentIds = new[] { Bbb } }, Ct);
        members.StatusCode.ShouldBe(HttpStatusCode.OK, await members.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>Leaves no account behind: these tests make a primary Data account, which the data-job tests (that assume there is none) would otherwise find.</summary>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Factory.ResetAccountsAsync();
        GC.SuppressFinalize(this);
    }

    protected async Task<Guid> CreateBasketAsync(string name)
    {
        using var response = await Client.PostJsonAsync("/api/baskets", new { name }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<BasketView>(Ct))!.Id;
    }

    protected async Task<ActionList> ActionsAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/portfolio/actions" + query, Ct);
        return await response.ReadAsync<ActionList>(Ct);
    }

    protected async Task<HttpResponseMessage> DecideAsync(string id, string decision) =>
        await Client.PostAsync($"/api/portfolio/actions/{id}/{decision}", Ct);

    protected async Task<ActionDecisionView> DecidedAsync(string id, string decision)
    {
        using var response = await DecideAsync(id, decision);
        return await response.ReadAsync<ActionDecisionView>(Ct);
    }

    protected async Task<PortfolioNotices> NoticesAsync()
    {
        using var response = await Client.GetAsync("/api/portfolio/notices", Ct);
        return await response.ReadAsync<PortfolioNotices>(Ct);
    }

    protected async Task<PriceAlertPage> AlertsAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/portfolio/alerts" + query, Ct);
        return await response.ReadAsync<PriceAlertPage>(Ct);
    }

    protected async Task<HttpResponseMessage> CreateAlertAsync(Guid instrumentId, string direction, decimal level, string? note = null) =>
        await Client.PostJsonAsync("/api/portfolio/alerts", new { instrumentId, direction, level, note }, Ct);

    protected async Task<PriceAlertView> CreatedAlertAsync(Guid instrumentId, string direction, decimal level, string? note = null)
    {
        using var response = await CreateAlertAsync(instrumentId, direction, level, note);
        return await response.ReadAsync<PriceAlertView>(Ct);
    }

    protected async Task<string?> TextAsync(string sql)
    {
        var count = await CountAsync($"SELECT count(*) FROM ({sql}) q");
        if (count == 0)
        {
            return null;
        }

        await using var connection = new Npgsql.NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
    }

    protected Task<string?> StateOfAsync(string rule, Guid holdingId) =>
        TextAsync($"SELECT state FROM portfolio.action_states WHERE rule = '{rule}' AND key = '{holdingId}'");

    protected async Task<Guid> HoldingIdAsync(string symbol) => (await Factory.HoldingAsync(symbol, Ct)).Id;

    protected Task SetPriceAsync(string symbol, decimal price) =>
        ExecuteAsync("UPDATE portfolio.holdings SET last_price = @p WHERE trading_symbol = @s", ("p", price), ("s", symbol));

    protected Task<ServiceRunView> RunQuotesAsync() => Factory.RunNowAsync(Client, Service, Ct);

    protected Task<long> EventsAsync(string type) => CountAsync($"SELECT count(*) FROM accounts.account_events WHERE event = '{type}'");
}
