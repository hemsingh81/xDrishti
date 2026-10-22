using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Lab;

/// <summary>P5-03 criteria 2, 3 and 9 end to end: a Lab run appends one ledger row, the same post twice leaves one, a cancelled run's row stays, the endpoint shows rows, variant count and factor, and an unknown family is 404.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabLedgerApiTests(ApiFactory factory) : DataTestBase(factory)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "DELETE FROM lab.test_ledger");
            await ExecuteAsync(connection, "DELETE FROM lab.lab_results; DELETE FROM lab.lab_runs");
        }

        await StrategySeeding.SeedAsync(Factory.Services, Ct);
    }

    private async Task<string> SymbolAsync(string ticker)
    {
        var id = await SeedAsync(ticker);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.instruments SET tick_size = 5 WHERE id = @id", ("id", id));
        }

        using var scope = Factory.Services.CreateScope();
        await using var session = await scope.ServiceProvider.GetRequiredService<IBarStore>().BeginMergeAsync(id, BarSource.Dhan, Ct);
        await session.AddAsync(ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.F1(ReplayDays.D2), ReplayDays.Flat(ReplayDays.D3, 105.00)), Ct);
        await session.CompleteAsync(null, Ct);
        return ticker;
    }

    private Task<HttpResponseMessage> PostAsync(params string[] symbols) =>
        Client.PostJsonAsync("/api/lab/runs", new { strategyId = "ORB", version = 1, universe = new { symbols }, method = "quick" }, Ct);

    private async Task<JsonElement> LedgerAsync(string id = "ORB")
    {
        using var response = await Client.GetAsync(new Uri($"/api/lab/strategies/{id}/ledger", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task A_run_appends_one_row_the_same_post_again_adds_none_and_the_endpoint_shows_it()
    {
        var a = await SymbolAsync("LEDA");
        using var first = await PostAsync(a);
        using var again = await PostAsync(a);
        var run = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);

        var ledger = await LedgerAsync();

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        ledger.GetProperty("strategyId").GetString().ShouldBe("ORB");
        ledger.GetProperty("variantCount").GetInt32().ShouldBe(1);
        ledger.GetProperty("adjustmentFactor").GetDouble().ShouldBe(1.0);
        var row = ledger.GetProperty("rows").EnumerateArray().Single();
        row.GetProperty("runId").GetGuid().ShouldBe(run.GetProperty("id").GetGuid());
        row.GetProperty("runKey").GetString().ShouldBe(run.GetProperty("key").GetString());
        row.GetProperty("strategyHash").GetString()!.Length.ShouldBe(64);
        row.GetProperty("version").GetInt32().ShouldBe(1);
        row.GetProperty("method").GetString().ShouldBe("quick");
        row.GetProperty("window").GetProperty("from").GetString().ShouldNotBeNullOrEmpty();
        row.GetProperty("createdAt").ValueKind.ShouldBe(JsonValueKind.String);
    }

    [Fact]
    public async Task A_cancelled_runs_row_stays_and_a_second_universe_of_the_same_hash_is_a_row_but_not_a_variant()
    {
        var a = await SymbolAsync("LEDB");
        var b = await SymbolAsync("LEDC");
        using var first = await PostAsync(a);
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        using var cancel = await Client.PostAsync(new Uri($"/api/lab/runs/{id}/cancel", UriKind.Relative), null, Ct);
        cancel.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var second = await PostAsync(a, b);
        var ledger = await LedgerAsync();

        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        ledger.GetProperty("rows").GetArrayLength().ShouldBe(2);
        ledger.GetProperty("variantCount").GetInt32().ShouldBe(1);
        ledger.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("runId").GetGuid()).ShouldContain(id);
    }

    [Fact]
    public async Task An_unknown_family_is_404_and_a_stored_one_without_runs_is_an_empty_ledger()
    {
        using var missing = await Client.GetAsync(new Uri("/api/lab/strategies/NOPE_NOT_STORED/ledger", UriKind.Relative), Ct);
        var empty = await LedgerAsync("BASE_BREAKOUT");

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        empty.GetProperty("rows").GetArrayLength().ShouldBe(0);
        empty.GetProperty("variantCount").GetInt32().ShouldBe(0);
        empty.GetProperty("adjustmentFactor").GetDouble().ShouldBe(1.0);
    }
}
