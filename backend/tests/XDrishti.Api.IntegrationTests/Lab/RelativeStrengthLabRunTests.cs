using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Strength;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Lab;

/// <summary>
/// BK-09 criteria 6, 7 and 11 on a real database: the seeded strategy <c>RS_ROTATION</c> (version 1) run through the real handler and engine reads the stored <c>mom_rank</c> and <c>sector_rs</c> rows of the sessions before each decision and trades; rows that exist only
/// for the decision session or later give no trade (no look-ahead); the run records the basket id, the basis and "members as of" in its label.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class RelativeStrengthLabRunTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid Nifty100 = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    private const int Sessions = 16;

    private static readonly string[] Symbols = ["NSE:RSROT"];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "DELETE FROM lab.test_ledger; DELETE FROM lab.lab_results; DELETE FROM lab.lab_runs");
            await ExecuteAsync(
                connection,
                "INSERT INTO market.baskets (id, name, colour, purpose, position, created_at, revision, kind, system) VALUES (@id, 'NSE Nifty 100', '#112233', 'watch', 0, now(), 0, 'index', true)",
                ("id", Nifty100));
        }

        await StrategySeeding.SeedAsync(Factory.Services, Ct);
    }

    private async Task<Guid> StockAsync()
    {
        var id = await SeedAsync("RSROT");
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.instruments SET tick_size = 5, sector = 'Banks' WHERE id = @id", ("id", id));
        }

        // a steady climb: each session opens at the close before it, a wick of 0.25 each way, 4 minutes of bars
        var days = new List<Day>();
        var previous = 100.0;
        for (var i = 0; i < Sessions; i++)
        {
            var close = 100.0 + (i * 0.8);
            days.Add(BaseDays.Ohlc(i, previous, Math.Max(previous, close) + 0.25, Math.Min(previous, close) - 0.25, close));
            previous = close;
        }

        using var scope = Factory.Services.CreateScope();
        await using var session = await scope.ServiceProvider.GetRequiredService<IBarStore>().BeginMergeAsync(id, BarSource.Dhan, Ct);
        await session.AddAsync(BaseDays.Bars(days), Ct);
        await session.CompleteAsync(null, Ct);
        return id;
    }

    private async Task StoreRowsAsync(Guid stock, IEnumerable<int> sessions, double rank = 0.9, int sectorRank = 1)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRelativeStrengthStore>();
        foreach (var s in sessions)
        {
            var day = BaseDays.Date(s);
            (await store.InsertDayAsync(
                day,
                [new StrengthRow(day, stock, Nifty100, 126, 21, 1.0, rank, 12, "value", "single_snapshot")],
                [new SectorRow(day, "Banks", 63, 0.05, sectorRank, 6, "value")],
                Ct)).ShouldBeTrue();
        }
    }

    private async Task<Result<SimulationRun>> RunAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
        var ids = await ScalarAsync<Guid>("SELECT id FROM market.instruments WHERE canonical_symbol = 'NSE:RSROT'");
        return await handler.HandleAsync(new RunSimulationQuery(new SimulationRequest("RS_ROTATION", null, "1D", BaseDays.Date(7), BaseDays.Date(Sessions - 1), [ids], null, CostChoice.Gross, StrategyVersion: 1)), Ct);
    }

    [Fact]
    public async Task Stored_rows_of_the_sessions_before_each_decision_give_trades_and_the_run_names_the_basket_the_basis_and_the_date()
    {
        var stock = await StockAsync();
        await StoreRowsAsync(stock, Enumerable.Range(0, Sessions));

        var result = await RunAsync();

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        var run = result.Value;
        run.Instruments.Single().Skipped.ShouldBeNull();
        run.Trades.Count.ShouldBeGreaterThan(0);
        var record = run.RelativeStrength.ShouldNotBeNull();
        record.Rows.ShouldBeGreaterThan(0);
        var basket = record.Baskets.Single();
        (basket.BasketId, basket.Name, basket.Basis).ShouldBe((Nifty100, "Nifty 100", "single_snapshot"));
        RunLabels.Of(run, FillRules.Default).Lines.ShouldContain(l => l.Contains("members as of " + BaseDays.Date(Sessions - 1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) && l.Contains("single_snapshot", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rows_stored_only_for_the_decision_sessions_or_later_give_no_trade()
    {
        var stock = await StockAsync();
        await StoreRowsAsync(stock, [Sessions - 1]);

        var result = await RunAsync();

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Trades.ShouldBeEmpty("the row of the last session is not known at any decision of the window");
    }

    [Fact]
    public async Task A_low_sector_rank_gives_no_trade_never_a_pass()
    {
        var stock = await StockAsync();
        await StoreRowsAsync(stock, Enumerable.Range(0, Sessions), sectorRank: 7);

        var weak = await RunAsync();

        weak.IsSuccess.ShouldBeTrue();
        weak.Value.Trades.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_lab_run_of_the_strategy_is_one_ledger_row_and_one_variant_of_its_family_and_the_same_post_adds_none()
    {
        var stock = await StockAsync();
        await StoreRowsAsync(stock, Enumerable.Range(0, Sessions));
        _ = stock;

        using var first = await Client.PostJsonAsync("/api/lab/runs", new { strategyId = "RS_ROTATION", version = 1, universe = new { symbols = Symbols }, method = "quick" }, Ct);
        using var again = await Client.PostJsonAsync("/api/lab/runs", new { strategyId = "RS_ROTATION", version = 1, universe = new { symbols = Symbols }, method = "quick" }, Ct);
        using var ledger = await Client.GetAsync(new Uri("/api/lab/strategies/RS_ROTATION/ledger", UriKind.Relative), Ct);
        var body = await ledger.Content.ReadFromJsonAsync<JsonElement>(Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted, await first.Content.ReadAsStringAsync(Ct));
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("variantCount").GetInt32().ShouldBe(1);
        body.GetProperty("rows").GetArrayLength().ShouldBe(1);
    }
}
