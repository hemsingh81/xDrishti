using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Api.IntegrationTests.Selection;

/// <summary>BK-08 criterion 9 at the composition level (real wiring, real database): the basket row's age is judged by the same <c>SmartRulesOptions</c> instance the gate uses. With the owner's <c>GateMaxAgeDays</c> at 2 a row 3 days old is unavailable; with the default 4 it is read.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class GateBasketWiringTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly ForDate = new(2026, 10, 12);

    private static readonly DateOnly Session = new(2026, 10, 9);

    private async Task<Guid> ChooseGateBasketAsync()
    {
        using var created = await Client.PostJsonAsync("/api/baskets", new { name = "Gate wiring" }, Ct);
        var id = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct)).GetProperty("id").GetGuid();
        using var chosen = await Client.PutJsonAsync("/api/selection/basket-settings", new { breadthSource = $"basket:{id}", password = Factory.OwnerPassword }, Ct);
        chosen.EnsureSuccessStatusCode();
        return id;
    }

    private async Task<string?> NoteAsync(Guid basket, SmartRulesOptions? options)
    {
        using var host = options is null ? null : Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(options)));
        using var scope = (host?.Services ?? Factory.Services).CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBasketContextStore>();
        await store.InsertOnceAsync(new BasketContextRow(basket, Session, 50, 48, 0.62, null, null, null, null, null, null, "value", "single_snapshot"), Ct);
        var context = await scope.ServiceProvider.GetRequiredService<IPlanContextSource>().ReadAsync(new PlanContextRequest(Session, ForDate, []), Ct);
        return context.Value.MarketNote;
    }

    [Fact]
    public async Task With_the_owner_maximum_age_at_two_a_three_day_old_row_is_unavailable()
    {
        var basket = await ChooseGateBasketAsync();

        var note = await NoteAsync(basket, new SmartRulesOptions(GateMaxAgeDays: 2));

        note!.ShouldContain("stale");
    }

    [Fact]
    public async Task With_the_default_maximum_age_the_same_row_is_read_and_the_basket_is_named()
    {
        var basket = await ChooseGateBasketAsync();

        var note = await NoteAsync(basket, null);

        note.ShouldBe("breadth of Gate wiring: 0.62");
    }
}
