using System.Globalization;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Research;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Criterion 11 of P4-01 (D8, D15): a run stored under <c>sim-5</c> (trade schema 4) is read as before, a trade without legs equal to its schema-4 form in every other field; a schema the code never wrote is refused by name.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class RunStoreSchemaReadTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<T> WithStore<T>(Func<IRunStore, Task<T>> action) => StoreKit.WithStoreAsync(factory.Services, action);

    private async Task<SimulationRun> StoredAsync()
    {
        var run = StoredRunFixtures.Run(costed: false, to: RunStoreRoundTripTests.NextTo(), fixtureEdges: false);
        (await WithStore(s => s.StoreAsync(StoreKit.ToStore(run, null), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        return run;
    }

    private Task SetTradeSchemaAsync(SimulationRun run, int schema) =>
        StoreKit.ExecuteAsync(factory.AdminConnectionString, $"UPDATE research.sim_runs SET trade_schema_version = {schema.ToString(CultureInfo.InvariantCulture)} WHERE run_key = '{run.RunKey}'", Ct);

    [Fact]
    public async Task A_run_of_trade_schema_4_is_read_as_its_trades_without_legs()
    {
        var run = await StoredAsync();
        await SetTradeSchemaAsync(run, 4);

        var page = await WithStore(s => s.PageTradesAsync(run.RunKey, new TradePageFilter(), new PageRequest(500, null), Ct).AsTask());

        page.IsSuccess.ShouldBeTrue(page.IsFailure ? page.Error.Message : string.Empty);
        page.Value.Items.Count.ShouldBeGreaterThan(0);
        page.Value.Items.ShouldAllBe(t => t.Trade.Legs == null && t.Trade.SchemaVersion == SimTrade.CurrentSchemaVersion);
        page.Value.Items.Select(t => t.Trade with { SchemaVersion = 0 }).ShouldBe(run.Trades.Concat(run.OpenAtEnd).Select(t => t.Trade with { SchemaVersion = 0 }).Take(page.Value.Items.Count), ignoreOrder: true);
    }

    [Fact]
    public async Task A_run_of_a_trade_schema_the_code_never_wrote_is_refused_by_name()
    {
        var run = await StoredAsync();
        await SetTradeSchemaAsync(run, 3);

        var page = await WithStore(s => s.PageTradesAsync(run.RunKey, new TradePageFilter(), new PageRequest(10, null), Ct).AsTask());

        page.IsFailure.ShouldBeTrue();
        page.Error.Message.ShouldContain("schema 3");
    }
}
