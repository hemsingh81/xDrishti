using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Holdings;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Accounts.Buckets;

namespace XDrishti.Api.IntegrationTests.Live;

/// <summary>
/// The instruments a Portfolio screen follows are the instruments of the holdings its summary is made of (ADR 0024 §7): for the default scope, one account, one bucket, and for accounts the summary leaves out (excluded from totals,
/// without the Portfolio role, archived). Account One holds ALPHA and BETA (BETA is sold at the second sync), Two (not in totals) GAMMA, Three (archived) DELTA, Four (no Portfolio role) EPSILON.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LiveScopeAgreementTests(ApiFactory factory) : HoldingsTestBase(factory)
{
    private Guid _one;
    private Guid _two;
    private Guid _three;
    private Guid _four;
    private Guid _bucketOne;
    private Guid _bucketTwo;
    private readonly Dictionary<string, Guid> _instruments = [];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        foreach (var (symbol, isin, security) in new[] { ("ALPHA", "INE000A01011", "1001"), ("BETA", "INE000B01012", "1002"), ("GAMMA", "INE000G01013", "1003"), ("DELTA", "INE000D01014", "1004"), ("EPSILON", "INE000E01015", "1005") })
        {
            _instruments[symbol] = await Factory.SeedInstrumentAsync(symbol, isin, security, symbol + " Limited", Ct);
        }

        _one = (await ConnectedAccountAsync("Example One", "1000000001")).Id;
        Dhan.Holdings.Clear();
        Dhan.Holdings.AddRange([Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"), Holding("BETA", 10, 200m, 180m, "1002", "INE000B01012")]);
        await ImportAllAsync(_one, "2024-10-12");
        Dhan.Holdings.RemoveAll(h => h.Symbol == "BETA");
        (await Client.SyncAsync(_one, Ct)).ShouldNotBeNull();

        _two = await AddAccountAsync("Example Two", "1000000002", Holding("GAMMA", 5, 1000m, 1500m, "1003", "INE000G01013"));
        using var excluded = await Client.PutJsonAsync($"/api/accounts/{_two}/portfolio", new { includeInTotals = false }, Ct);
        excluded.StatusCode.ShouldBe(HttpStatusCode.OK);

        _three = await AddAccountAsync("Example Three", "1000000003", Holding("DELTA", 8, 25m, 26m, "1004", "INE000D01014"));
        _four = await AddAccountAsync("Example Four", "1000000004", Holding("EPSILON", 3, 100m, 110m, "1005", "INE000E01015"));
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE accounts.portfolio_settings SET enabled = false WHERE account_id = {0}", [_four], Ct);
            return 0;
        });
        using var archived = await Client.DeleteAsync($"/api/accounts/{_three}", Ct);
        archived.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var buckets = await (await Client.GetAsync("/api/portfolio-buckets", Ct)).ReadAsync<List<BucketView>>(Ct);
        _bucketOne = buckets.Single(b => b.Name == "Example One · Portfolio").Id;
        _bucketTwo = buckets.Single(b => b.Name == "Example Two · Portfolio").Id;
    }

    private async Task<Guid> AddAccountAsync(string name, string clientId, FakeBrokers.FakeHolding holding)
    {
        var account = await ConnectedAccountAsync(name, clientId);
        Dhan.Holdings.Clear();
        Dhan.Holdings.Add(holding);
        await ImportAllAsync(account.Id, "2024-10-12");
        return account.Id;
    }

    /// <summary>The instruments of the holdings the summary of this scope reads: the same query, then the open holdings that have an instrument.</summary>
    private async Task<HashSet<Guid>> SummarySetAsync(Guid? accountId, Guid? bucketId)
    {
        using var scope = Factory.Services.CreateScope();
        var holdings = await scope.ServiceProvider.GetRequiredService<IHoldingQueries>()
            .ListAsync(new HoldingFilter(accountId, bucketId, null, null, TotalsOnly: accountId is null), Ct);
        return [.. holdings.Where(h => h.IsOpen && h.InstrumentId is not null).Select(h => h.InstrumentId!.Value)];
    }

    private async Task<HashSet<Guid>> ResolvedAsync(Guid? accountId, Guid? bucketId, params Guid[] holdingIds)
    {
        using var scope = Factory.Services.CreateScope();
        return [.. await scope.ServiceProvider.GetRequiredService<ILiveScopeResolver>().ResolveAsync(accountId, bucketId, holdingIds, Ct)];
    }

    private HashSet<Guid> Of(params string[] symbols) => [.. symbols.Select(s => _instruments[s])];

    [Fact]
    public async Task The_default_scope_follows_the_instruments_of_the_accounts_in_totals_only()
    {
        var resolved = await ResolvedAsync(null, null);

        resolved.ShouldBe(Of("ALPHA")); // not BETA (sold), GAMMA (not in totals), DELTA (archived), EPSILON (no Portfolio role)
        resolved.ShouldBe(await SummarySetAsync(null, null));
        (await Client.SummaryAsync("", Ct)).OpenCount.ShouldBe(1);
    }

    [Fact]
    public async Task One_account_is_followed_whatever_its_role_in_totals_and_an_archived_one_is_not()
    {
        foreach (var (account, expected) in new[] { (_one, Of("ALPHA")), (_two, Of("GAMMA")), (_three, Of()), (_four, Of("EPSILON")) })
        {
            (await ResolvedAsync(account, null)).ShouldBe(expected);
            (await ResolvedAsync(account, null)).ShouldBe(await SummarySetAsync(account, null));
        }
    }

    [Fact]
    public async Task A_bucket_narrows_the_scope_as_it_does_for_the_summary()
    {
        (await ResolvedAsync(null, _bucketOne)).ShouldBe(Of("ALPHA"));
        (await ResolvedAsync(null, _bucketOne)).ShouldBe(await SummarySetAsync(null, _bucketOne));
        (await ResolvedAsync(null, _bucketTwo)).ShouldBeEmpty(); // Two is not in totals
        (await ResolvedAsync(null, _bucketTwo)).ShouldBe(await SummarySetAsync(null, _bucketTwo));
        (await ResolvedAsync(_two, _bucketTwo)).ShouldBe(Of("GAMMA"));
        (await ResolvedAsync(_two, _bucketTwo)).ShouldBe(await SummarySetAsync(_two, _bucketTwo));
        (await ResolvedAsync(_one, _bucketTwo)).ShouldBeEmpty();
        (await ResolvedAsync(_one, _bucketTwo)).ShouldBe(await SummarySetAsync(_one, _bucketTwo));
    }

    [Fact]
    public async Task Listed_holdings_are_added_unless_their_account_is_archived()
    {
        var gamma = await Factory.HoldingAsync("GAMMA", Ct);
        var delta = await Factory.HoldingAsync("DELTA", Ct);
        var epsilon = await Factory.HoldingAsync("EPSILON", Ct);

        (await ResolvedAsync(null, null, gamma.Id, delta.Id, epsilon.Id)).ShouldBe(Of("ALPHA", "GAMMA", "EPSILON"), ignoreOrder: true);
    }
}
