using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// AU-16 over HTTP and a real database: the scope is set by the owner (audited, insert-only), refuses an empty scope and unknown ids, shows on the Essential row; the suggestions read model joins a stored plan with its reasons and evidence, lists a ticket without
/// the required evidence as not suggested, keeps reserve and rejected visible, and carries the no-order words. A plan stored with its evidence reads n_eff and the as-of date back.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class AutoScopeApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Session = new(2099, 2, 3);

    private static readonly DateTimeOffset Cutoff = new(2099, 2, 3, 3, 35, 0, TimeSpan.Zero);

    private const string Cell = "ORB|5m|BUY|intraday|fixed|*";

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM planning.auto_scopes");
    }

    public override async ValueTask DisposeAsync()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "DELETE FROM planning.auto_scopes");
        }

        await base.DisposeAsync();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, string uri, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(uri, UriKind.Relative)) { Content = body is null ? null : System.Net.Http.Json.JsonContent.Create(body) };
        using var response = await Client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task The_owner_sets_the_scope_unknown_ids_and_an_empty_scope_are_refused_and_the_essential_row_shows_it()
    {
        var stock = await SeedAsync("SCOPEA");

        var none = await SendAsync(HttpMethod.Get, "/api/auto/scope");
        var empty = await SendAsync(HttpMethod.Put, "/api/auto/scope", new { basketIds = Array.Empty<Guid>(), instrumentIds = Array.Empty<Guid>() });
        var unknown = await SendAsync(HttpMethod.Put, "/api/auto/scope", new { instrumentIds = new[] { Guid.NewGuid() } });
        var unknownBasket = await SendAsync(HttpMethod.Put, "/api/auto/scope", new { basketIds = new[] { Guid.NewGuid() }, instrumentIds = new[] { stock } });
        var ok = await SendAsync(HttpMethod.Put, "/api/auto/scope", new { instrumentIds = new[] { stock } });
        var read = await SendAsync(HttpMethod.Get, "/api/auto/scope");
        var essentials = await SendAsync(HttpMethod.Get, "/api/settings/essentials");
        using var anonymous = await Factory.CreateAnonymousClient().GetAsync(new Uri("/api/auto/scope", UriKind.Relative), Ct);

        none.Body.GetProperty("isSet").GetBoolean().ShouldBeFalse();
        none.Body.GetProperty("summary").GetString().ShouldBe("eligible");
        empty.Status.ShouldBe(HttpStatusCode.BadRequest);
        unknown.Status.ShouldBe(HttpStatusCode.BadRequest);
        unknownBasket.Status.ShouldBe(HttpStatusCode.BadRequest);
        ok.Status.ShouldBe(HttpStatusCode.OK);
        read.Body.GetProperty("instrumentIds")[0].GetGuid().ShouldBe(stock);
        read.Body.GetProperty("changedBy").GetString().ShouldStartWith("owner:");
        read.Body.GetProperty("hash").GetString()!.Length.ShouldBe(64);
        var row = essentials.Body.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "suggest.scope");
        row.GetProperty("value").GetString().ShouldBe("0 baskets, 1 stocks");
        row.GetProperty("isDefault").GetBoolean().ShouldBeFalse();
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE detail LIKE 'suggest.scope=%'")).ShouldBeGreaterThanOrEqualTo(1);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_suggestions_join_the_stored_plan_with_reasons_and_evidence_and_list_what_lacks_evidence_as_not_suggested()
    {
        var full = new PlanEvidence(Cell, 120, "cell statistics as of 2099-02-02", "basis", 0.55, 0.21, [], null, 0.5, 64.2, new DateOnly(2099, 2, 2));
        var old = full with { NEff = null, StatsAsOf = null };
        NewPlanItem Item(int rank, TicketGroup group, string symbol, PlanEvidence e, params string[] reasons) =>
            new(rank, group, symbol, "BUY", "ORB", new string('a', 64), "t", "s", "g", "v", reasons, e, []);
        var plan = new NewPlan("auto-scope-v1", Session, Cutoff, 1, new string('c', 64), "Cell statistics only (shrunk replayed-history estimates); tickets are conditional on tomorrow's bars.", true, "test", DateTimeOffset.UnixEpoch,
            [Item(1, TicketGroup.Primary, "NSE:GOOD", full), Item(2, TicketGroup.Primary, "NSE:OLD", old), Item(3, TicketGroup.Reserve, "NSE:RES", full, "same_bet_better_ticket"), Item(4, TicketGroup.Rejected, "NSE:REJ", full, "sample:fail")])
        { BasisFlags = new PlanBasisFlags(1, "normal", [], "clear", "applied", 0, "known", 1, false, "gross", []) { ScopeHash = new string('b', 64) } };
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct);
        }

        var result = await SendAsync(HttpMethod.Get, "/api/auto/suggestions?forDate=2099-02-03");
        var missing = await SendAsync(HttpMethod.Get, "/api/auto/suggestions?forDate=2098-01-01");

        result.Status.ShouldBe(HttpStatusCode.OK);
        var body = result.Body;
        body.GetProperty("notice").GetString().ShouldBe("Suggestion only, no order is placed");
        body.GetProperty("basis").GetString()!.ShouldContain("replayed-history");
        body.GetProperty("scopeHash").GetString().ShouldBe(new string('b', 64));
        var good = body.GetProperty("suggestions").EnumerateArray().Single();
        good.GetProperty("symbol").GetString().ShouldBe("NSE:GOOD");
        good.GetProperty("evidence").GetProperty("nEff").GetString().ShouldBe("64.2");
        good.GetProperty("evidence").GetProperty("dataAsOf").GetString().ShouldBe("2099-02-02");
        good.GetProperty("evidence").GetProperty("regime").GetString().ShouldBe("all regimes");
        var notSuggested = body.GetProperty("notSuggested").EnumerateArray().Single();
        notSuggested.GetProperty("symbol").GetString().ShouldBe("NSE:OLD");
        notSuggested.GetProperty("why").GetString().ShouldBe("Not suggested: n_eff, data as-of unavailable.");
        notSuggested.GetProperty("evidence").GetProperty("nEff").GetString().ShouldBe("unavailable");
        body.GetProperty("reserve")[0].GetProperty("reasons")[0].GetString().ShouldBe("The same bet as a better ticket.");
        body.GetProperty("rejected")[0].GetProperty("reasons")[0].GetString().ShouldBe("Failed: the cell has too few trades.");
        missing.Status.ShouldBe(HttpStatusCode.NotFound);
    }
}
