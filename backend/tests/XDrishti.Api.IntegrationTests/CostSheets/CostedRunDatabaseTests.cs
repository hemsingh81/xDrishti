using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Simulate;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.CostSheets;

/// <summary>
/// Criterion 3 of P3-03f: a run with a stored sheet over the real ports and the real stores. The sheet is the ordinary invented <c>fixture-a</c> (the numbers of the TEST sheet), stored through <c>CostSheetStore</c>; the F1, F2 and split days of
/// P3-03 and the basket of P3-04a are seeded through the real stores. The expected costs are the hand figures of P3-03c (W7 and W8, <c>WorkedCosts</c>), written before the code and not produced by it. Then the verb, with <c>--costs</c> and
/// <c>--gross</c>, and its refusals before any bar is read.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class CostedRunDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly D1 = ReplayDays.D1;

    private static readonly DateOnly D2 = ReplayDays.D2;

    private static readonly DateOnly D3 = ReplayDays.D3;

    private static readonly DateOnly D4 = ReplayDays.D4;

    private static readonly FillRulesInput Exact = new(0.1, 0, 0, 0);

    private static Bar[] A() => ReplayDays.Join(ReplayDays.Flat(D1, 99.00), ReplayDays.F1(D2), ReplayDays.Flat(D3, 105.00));

    private static Bar[] B() => ReplayDays.Join(ReplayDays.F2(D1), ReplayDays.Flat(D2, 98.50), ReplayDays.Flat(D3, 98.50));

    private async Task<Guid> InstrumentAsync(string ticker, IEnumerable<Bar> bars, decimal? tickPaise = 5m)
    {
        var id = await SeedAsync(ticker);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.instruments SET tick_size = @tick WHERE id = @id", ("tick", tickPaise is null ? DBNull.Value : tickPaise.Value), ("id", id));
        }

        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBarStore>();
        await using var session = await store.BeginMergeAsync(id, BarSource.Dhan, Ct);
        await session.AddAsync(bars.ToArray(), Ct);
        await session.CompleteAsync(null, Ct);
        return id;
    }

    private async Task StoreFixtureSheetAsync()
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(FixtureSheet.Sheet, "cli:cost-sheet", DateTimeOffset.UnixEpoch, Ct);
        (await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
    }

    private async Task<(Guid Abb, Guid Tcs, Guid Split)> SeedDaysAsync()
    {
        var abb = await InstrumentAsync("ABB", A());
        var tcs = await InstrumentAsync("TCS", B());
        var split = await InstrumentAsync("SPLITCO", ReplayDays.Scaled(A(), 2.0, 1), tickPaise: null);
        using var response = await Client.PostJsonAsync("/api/data/corporate-actions", new { instrumentId = split, exDate = D4.ToString("yyyy-MM-dd"), kind = "split", numerator = 1, denominator = 2 }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (abb, tcs, split);
    }

    private async Task<Result<SimulationRun>> RunAsync(Guid[]? ids, Guid? basket, CostChoice costs)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
        return await handler.HandleAsync(new RunSimulationQuery(new SimulationRequest("ORB", null, "15m", D1, D3, ids, basket, costs, false, null, Exact)), Ct);
    }

    [Fact]
    public async Task A_costed_run_over_the_stored_sheet_has_the_walkers_trades_and_the_hand_figures_of_w7_and_w8_in_every_cost_field()
    {
        var (abb, tcs, split) = await SeedDaysAsync();
        await StoreFixtureSheetAsync();

        var gross = await RunAsync([abb, tcs, split], null, CostChoice.Gross);
        var costed = await RunAsync([abb, tcs, split], null, CostChoice.Sheet(FixtureSheet.Id));

        costed.IsSuccess.ShouldBeTrue(costed.IsFailure ? costed.Error.Code + ": " + costed.Error.Message : string.Empty);
        var run = costed.Value;
        run.Trades.Count.ShouldBe(3);
        var bySymbol = run.Trades.ToDictionary(t => t.Trade.Symbol, t => t.Trade);
        WorkedAssert.ShouldMatch(bySymbol["NSE:ABB"], WorkedCosts.W7B, sheetId: FixtureSheet.Id);
        WorkedAssert.ShouldMatch(bySymbol["NSE:TCS"], WorkedCosts.W8, sheetId: FixtureSheet.Id);
        WorkedAssert.ShouldMatch(bySymbol["NSE:SPLITCO"], WorkedCosts.W7B, sheetId: FixtureSheet.Id);
        (bySymbol["NSE:ABB"].EntryPrice, bySymbol["NSE:ABB"].ExitPrice, bySymbol["NSE:ABB"].RExact).ShouldBe((100.50, 105.50, 2.0), "P3-03's table");
        (bySymbol["NSE:TCS"].ExitReason, bySymbol["NSE:TCS"].RExact).ShouldBe((ExitReason.Stop, -1.0));
        run.Trades.Select(t => t.Trade with { Costing = null, Costs = CostBasis.None }).ShouldBe(gross.Value.Trades.Select(t => t.Trade));
        run.Costs.ShouldNotBeNull().SheetId.ShouldBe(FixtureSheet.Id);
    }

    [Fact]
    public async Task The_fates_and_totals_add_up_and_the_record_embeds_the_sheet_text_and_the_rates_hash()
    {
        var (abb, tcs, split) = await SeedDaysAsync();
        await StoreFixtureSheetAsync();

        var gross = (await RunAsync([abb, tcs, split], null, CostChoice.Gross)).Value;
        var costed = (await RunAsync([abb, tcs, split], null, CostChoice.Sheet(FixtureSheet.Id))).Value;

        for (var i = 0; i < costed.Instruments.Count; i++)
        {
            var fates = costed.Instruments[i].Fates;
            (fates.Trades + fates.OpenAtEnd + fates.SkippedQuantity + fates.SkippedCost).ShouldBe(gross.Instruments[i].Fates.Trades + gross.Instruments[i].Fates.OpenAtEnd);
            fates.Total.ShouldBe(gross.Instruments[i].Fates.Total);
        }

        costed.Totals.TradesInWindow.ShouldBe(costed.Trades.Count + costed.OpenAtEnd.Count);
        var record = costed.Costs.ShouldNotBeNull();
        record.SheetJson.ShouldBe(FixtureSheet.Sheet.ToCanonicalJson());
        record.RatesSha256.ShouldBe(FixtureSheet.Sheet.RatesSha256);
        (record.Capital, record.Profile, record.MaxCostR).ShouldBe((1_000_000m, SizingProfile.Standard, 0.10m));
        gross.Costs.ShouldBeNull();
        gross.Trades.ShouldAllBe(t => t.Trade.Costing == null && t.Trade.Costs == CostBasis.None);
        costed.RunKey.ShouldNotBe(gross.RunKey, "the costs are part of the key");
    }

    [Fact]
    public async Task A_second_run_gives_the_same_record_and_key_and_a_changed_sheet_changes_the_key_but_not_an_old_record()
    {
        var (abb, tcs, split) = await SeedDaysAsync();
        await StoreFixtureSheetAsync();

        var first = (await RunAsync([abb, tcs, split], null, CostChoice.Sheet(FixtureSheet.Id))).Value;
        var second = (await RunAsync([abb, tcs, split], null, CostChoice.Sheet(FixtureSheet.Id))).Value;
        var other = FixtureSheet.Json.Replace("\"stt_sell_pct\":0.09", "\"stt_sell_pct\":0.095", StringComparison.Ordinal);
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(CostSheetJson.Parse(other).Value, "cli:cost-sheet", DateTimeOffset.UnixEpoch, Ct);
            await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct);
        }

        var changed = (await RunAsync([abb, tcs, split], null, CostChoice.Sheet(FixtureSheet.Id))).Value;

        second.RunKey.ShouldBe(first.RunKey);
        second.Costs.ShouldBe(first.Costs);
        second.Trades.Select(t => t.Trade.Key).ShouldBe(first.Trades.Select(t => t.Trade.Key));
        changed.RunKey.ShouldNotBe(first.RunKey, "other rates, other key");
        first.Costs!.RatesSha256.ShouldBe(FixtureSheet.Sheet.RatesSha256, "the old record keeps the rates it was charged with");
    }

    [Fact]
    public async Task A_basket_run_is_costed_the_same_way()
    {
        var (abb, tcs, split) = await SeedDaysAsync();
        await StoreFixtureSheetAsync();
        using var created = await Client.PostJsonAsync("/api/baskets", new { name = "Lab" }, Ct);
        var basket = (await created.Content.ReadFromJsonAsync<XDrishti.Application.Market.Baskets.BasketView>(Ct))!.Id;
        using var added = await Client.PostJsonAsync($"/api/baskets/{basket}/members", new { instrumentIds = new[] { abb, tcs, split } }, Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.OK);

        var run = (await RunAsync(null, basket, CostChoice.Sheet(FixtureSheet.Id))).Value;

        run.Trades.Count.ShouldBe(3);
        run.Trades.ShouldAllBe(t => t.Trade.Costing != null && t.Trade.Costing.SheetId == FixtureSheet.Id);
    }

    [Fact]
    public async Task An_unknown_sheet_a_test_id_and_a_window_before_the_first_schedule_are_refused_and_no_bar_is_read()
    {
        await StoreFixtureSheetAsync();
        var reads = new SimulateDatabaseTests.NoReads();
        using var host = SimulateHost.Create(
            new Dictionary<string, string?> { ["Database:ConnectionString"] = Factory.AdminConnectionString },
            services =>
            {
                services.AddSingleton<IBarStore>(reads);
                services.AddSingleton<IBarQueries>(reads);
            });
        using var scope = host.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
        var id = Guid.CreateVersion7();
        var early = new DateOnly(2025, 12, 31);

        var unknown = await handler.HandleAsync(new RunSimulationQuery(new SimulationRequest("ORB", null, "15m", D1, D3, [id], null, CostChoice.Sheet("nope"), false, null, Exact)), Ct);
        var test = await handler.HandleAsync(new RunSimulationQuery(new SimulationRequest("ORB", null, "15m", D1, D3, [id], null, CostChoice.Sheet(TestCostSheet.Id), false, null, Exact)), Ct);
        var notEffective = await handler.HandleAsync(new RunSimulationQuery(new SimulationRequest("ORB", null, "15m", early, D3, [id], null, CostChoice.Sheet(FixtureSheet.Id), false, null, Exact)), Ct);

        unknown.Error.Code.ShouldBe(SimulationErrors.CostSheetNotFoundCode);
        test.Error.Code.ShouldBe(SimulationErrors.CostSheetNotFoundCode);
        notEffective.Error.Code.ShouldBe(SimulationErrors.CostSheetNotEffectiveCode);
        reads.Calls.ShouldBeEmpty();
    }

    // the verb

    private sealed record Outcome(int Exit, string Out, string Error, IReadOnlyCollection<string> Logs);

    private async Task<Outcome> VerbAsync(string args, TempFolder temp, SimulateDatabaseTests.NoReads? noReads = null)
    {
        var logs = new CapturedLogs();
        using var host = SimulateHost.Create(
            new Dictionary<string, string?> { ["Database:ConnectionString"] = Factory.AdminConnectionString, ["BrokerVault:Key"] = string.Empty },
            services =>
            {
                services.AddSingleton<ILogEventSink>(logs);
                if (noReads is not null)
                {
                    services.AddSingleton<IBarStore>(noReads);
                    services.AddSingleton<IBarQueries>(noReads);
                }
            });
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await SimulateCommand.RunAsync(args.Split(' ', StringSplitOptions.RemoveEmptyEntries), host.Services, new SimulateConsole(output, error), temp.Root, Ct);

        return new Outcome(exit, output.ToString(), error.ToString(), [.. logs.All]);
    }

    private const string Window = "--from 2026-03-02 --to 2026-03-04";

    [Fact]
    public async Task The_verb_with_costs_writes_the_cost_columns_and_labels_and_with_gross_says_costs_none()
    {
        await SeedDaysAsync();
        await StoreFixtureSheetAsync();
        using var temp = new TempFolder();

        var costed = await VerbAsync("--strategy ORB --symbols ABB,TCS,SPLITCO " + Window + " --fills no-slippage --costs fixture-a", temp);
        var gross = await VerbAsync("--strategy ORB --symbols ABB,TCS,SPLITCO " + Window + " --fills no-slippage --gross", temp);

        costed.Exit.ShouldBe(0, costed.Error);
        gross.Exit.ShouldBe(0, gross.Error);
        costed.Out.ShouldContain("costs: sheet fixture-a (not checked against bills)");
        costed.Out.ShouldContain("rates " + FixtureSheet.Sheet.RatesSha256);
        costed.Out.ShouldContain("capital 1000000, profile standard, max cost in R 0.1");
        gross.Out.ShouldContain("costs: none");
        var folders = Directory.EnumerateDirectories(Path.Combine(temp.Root, "sim")).ToList();
        folders.Count.ShouldBe(2, "the cost choice is part of the run key, so the two runs have two folders");
        var withCosts = folders.Single(f => File.ReadAllText(Path.Combine(f, "trades.csv")).Contains("fixture-a", StringComparison.Ordinal));
        File.ReadAllLines(Path.Combine(withCosts, "trades.csv"))[0].ShouldContain("costing_sheet");
        File.ReadAllText(Path.Combine(withCosts, "README.txt")).ShouldContain("costs: sheet fixture-a");
        File.ReadAllText(Path.Combine(withCosts, "run.json")).ShouldContain(FixtureSheet.Sheet.RatesSha256);
    }

    [Fact]
    public async Task The_verb_refuses_an_unknown_sheet_and_a_late_sheet_with_exit_1_before_any_bar_is_read_and_a_missing_choice_with_exit_2()
    {
        await SeedAsync("ABB");
        await StoreFixtureSheetAsync();
        using var temp = new TempFolder();
        var reads = new SimulateDatabaseTests.NoReads();

        var unknown = await VerbAsync("--strategy ORB --symbols ABB " + Window + " --costs nope", temp, reads);
        var early = await VerbAsync("--strategy ORB --symbols ABB --from 2025-12-01 --to 2026-03-04 --costs fixture-a", temp, reads);
        var neither = await VerbAsync("--strategy ORB --symbols ABB " + Window, temp, reads);

        unknown.Exit.ShouldBe(1);
        unknown.Error.ShouldContain("simulation.cost_sheet_not_found");
        early.Exit.ShouldBe(1);
        early.Error.ShouldContain("costs.sheet_id: simulation.cost_sheet_not_effective");
        neither.Exit.ShouldBe(2);
        neither.Error.ShouldContain("--gross");
        reads.Calls.ShouldBeEmpty();
        Directory.Exists(temp.Root).ShouldBeFalse();
    }
}
