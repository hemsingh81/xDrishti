using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Domain.Accounts;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>Helpers for the basket tests: the imported sample, the three seeded baskets and thin wrappers over the endpoints.</summary>
public abstract class BasketsTestBase(ApiFactory factory) : MarketTestBase(factory)
{
    protected static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(1);

    /// <summary>The baskets list also shows the portfolio buckets (ADR 0014), so a basket test starts without accounts and buckets left behind by other tests.</summary>
    public override async ValueTask InitializeAsync()
    {
        await Factory.ResetAccountsAsync();
        await base.InitializeAsync();
    }

    protected async Task ImportAsync() => (await RefreshAsync()).Status.ShouldBe("succeeded");

    protected async Task<InstrumentSummary> FindAsync(string symbol) =>
        (await SearchAsync($"?q={Uri.EscapeDataString(symbol)}&includeRetired=true&pageSize=100")).Items.Single(i => i.Symbol == symbol);

    protected async Task<Guid[]> SharesAsync(int count) => [.. (await SearchAsync($"?type=share&pageSize={count}")).Items.Select(i => i.Id)];

    protected async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request, Ct);
    }

    protected async Task<List<BasketView>> ListBasketsAsync(bool includeArchived = false)
    {
        using var response = await Client.GetAsync("/api/baskets" + (includeArchived ? "?includeArchived=true" : string.Empty), Ct);
        return await response.ReadAsync<List<BasketView>>(Ct);
    }

    protected async Task<BasketView> BasketAsync(string name) => (await ListBasketsAsync(includeArchived: true)).Single(b => b.Name == name);

    protected async Task<BasketView> CreateBasketAsync(string name, string? colour = null, string? purpose = null, string? description = null)
    {
        using var response = await Client.PostJsonAsync("/api/baskets", new { name, colour, purpose, description }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var created = (await response.Content.ReadFromJsonAsync<BasketView>(Ct))!;
        response.Headers.Location!.ToString().ShouldBe($"/api/baskets/{created.Id}");
        return created;
    }

    protected Task<HttpResponseMessage> AddRawAsync(Guid basketId, object? body) => Client.PostJsonAsync($"/api/baskets/{basketId}/members", body, Ct);

    protected async Task<AddMembersResult> AddAsync(Guid basketId, string? note = null, params Guid[] ids)
    {
        using var response = await AddRawAsync(basketId, new { instrumentIds = ids, note });
        return await response.ReadAsync<AddMembersResult>(Ct);
    }

    protected Task<AddMembersResult> AddAsync(Guid basketId, params Guid[] ids) => AddAsync(basketId, null, ids);

    protected async Task<RemoveMembersResult> RemoveAsync(Guid basketId, params Guid[] ids)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/api/baskets/{basketId}/members", new { instrumentIds = ids });
        return await response.ReadAsync<RemoveMembersResult>(Ct);
    }

    protected async Task<BasketMembers> MembersAsync(Guid basketId, string? asOf = null)
    {
        using var response = await Client.GetAsync($"/api/baskets/{basketId}/members" + (asOf is null ? string.Empty : $"?asOf={asOf}"), Ct);
        return await response.ReadAsync<BasketMembers>(Ct);
    }

    protected async Task<Guid[]> MemberIdsAsync(Guid basketId) => [.. (await MembersAsync(basketId)).Items.Select(m => m.Instrument.Id)];

    protected async Task<BasketHistory> HistoryAsync(Guid basketId)
    {
        using var response = await Client.GetAsync($"/api/baskets/{basketId}/history", Ct);
        return await response.ReadAsync<BasketHistory>(Ct);
    }

    protected Task<HttpResponseMessage> TransferRawAsync(object body) => Client.PostJsonAsync("/api/baskets/transfer", body, Ct);

    protected async Task<TransferResult> TransferAsync(Guid from, Guid to, string mode, params Guid[] ids)
    {
        using var response = await TransferRawAsync(new { fromBasketId = from, toBasketId = to, instrumentIds = ids, mode });
        return await response.ReadAsync<TransferResult>(Ct);
    }

    protected async Task<List<InstrumentBasketView>> BasketsOfAsync(Guid instrumentId)
    {
        using var response = await Client.GetAsync($"/api/instruments/{instrumentId}/baskets", Ct);
        return await response.ReadAsync<List<InstrumentBasketView>>(Ct);
    }

    /// <summary>Reads a problem response: status, stable code and the field errors (<c>errors</c>) by field.</summary>
    protected static async Task<(string? Code, Dictionary<string, string[]> Errors)> ProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = document.RootElement;
        var errors = root.TryGetProperty("errors", out var e)
            ? e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(x => x.GetString()!).ToArray())
            : [];
        return (root.GetProperty("code").GetString(), errors);
    }

    private static readonly AccountEventType[] BasketEventTypes = [.. Enum.GetValues<AccountEventType>().Where(t => t.ToString().StartsWith("Basket", StringComparison.Ordinal))];

    protected Task<List<AccountEvent>> BasketEventsAsync() =>
        Factory.WithDbAsync(db => db.AccountEvents.AsNoTracking()
            .Where(e => BasketEventTypes.Contains(e.Event))
            .OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync(Ct));

    protected async Task RetireFutureAsync()
    {
        Factory.MasterData.MasterCsv = WithoutLine(Factory.MasterData.MasterCsv, "RELIANCE-Oct2026-FUT");
        Factory.MasterData.MasterETag = "\"dhan-retired\"";
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        (await RefreshAsync()).Retired.ShouldBe(1);
    }
}
