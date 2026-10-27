using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Research;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// P6-02 end to end on a real database (ADR 0146): with a stored context the cells are split by regime, the plain readers still see the <c>*</c> cells only, the regime endpoint shows the regime cells with their basis, and the labels per day are
/// readable; with no context every trade is counted as having none and no regime cell exists.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class RegimeCellsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly AsOf = new(2026, 4, 30);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.cell_stats");
        await ExecuteAsync(connection, "DELETE FROM market.context_daily");
    }

    /// <summary>About 460 weekdays from 2024-06-03 to 2026-03-31: enough for the regime (265 days) well before the run's trades.</summary>
    private async Task InsertContextAsync()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO market.context_daily (date, index_instrument_id, index_high, index_low, index_close, vix_close, index_bars, vix_bars, source, written_at)
            SELECT d::date, gen_random_uuid(), 20000 + n * 3 + 50, 20000 + n * 3 - 50, 20000 + n * 3 + 30 * sin(n), 12 + (n % 9), 375, 375, 'derived_1m', now()
            FROM (SELECT d, row_number() OVER (ORDER BY d) AS n FROM generate_series('2024-06-03'::date, '2026-03-31'::date, interval '1 day') AS g(d) WHERE extract(isodow FROM d) < 6) days
            """);
    }

    private async Task<string> StoredRunKeyAsync()
    {
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo());
        using var scope = Factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>().HandleAsync(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor), Ct);
        stored.IsSuccess.ShouldBeTrue();
        return run.RunKey;
    }

    private async Task<CellStatsOutcome> ComputeAsync(string runKey, bool byRegime)
    {
        using var scope = Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ComputeCellStatsCommand, CellStatsOutcome>>()
            .HandleAsync(new ComputeCellStatsCommand(AsOf, [new CellSource(runKey, "intraday", "FIXED_RR")], new CellStatsOptions(MinTrades: 3), "test", byRegime), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private async Task<JsonElement> GetAsync(string url)
    {
        using var response = await Client.GetAsync(new Uri(url, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task With_a_stored_context_the_regime_cells_are_written_and_the_plain_readers_still_see_only_the_star_cells()
    {
        await InsertContextAsync();
        var key = await StoredRunKeyAsync();

        var outcome = await ComputeAsync(key, byRegime: true);

        outcome.Regime.ShouldNotBeNull();
        outcome.Regime.Labelled.ShouldBe(outcome.Trades, "every trade of the run falls after 265 context days");
        outcome.RegimeCells.ShouldBeGreaterThan(0);
        outcome.Rows.ShouldBe((outcome.Cells + outcome.RegimeCells) * 3);
        var star = await GetAsync("/api/cells");
        star.GetArrayLength().ShouldBe(outcome.Cells * 3);
        star.EnumerateArray().ShouldAllBe(r => r.GetProperty("regime").GetString() == "*" && r.GetProperty("regimeBasis").GetString() == "all");
        var regimes = await GetAsync("/api/cells/regimes");
        regimes.GetArrayLength().ShouldBe(outcome.RegimeCells * 3);
        regimes.EnumerateArray().ShouldAllBe(r => r.GetProperty("regimeName").GetString() != null && (r.GetProperty("regimeBasis").GetString() == "index_and_vix" || r.GetProperty("regimeBasis").GetString() == "index_only"));
        regimes.EnumerateArray().Where(r => r.GetProperty("window").GetString() == "all").Sum(r => r.GetProperty("n").GetInt32())
            .ShouldBe(star.EnumerateArray().Where(r => r.GetProperty("window").GetString() == "all").Sum(r => r.GetProperty("n").GetInt32()), "the regime cells hold every trade of the star cells in the all window");
        (await GetAsync("/api/cells/regimes?window=60d&strategy=ORB")).EnumerateArray().ShouldAllBe(r => r.GetProperty("window").GetString() == "60d");
        using var bad = await Client.GetAsync(new Uri("/api/cells/regimes?window=7d", UriKind.Relative), Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_run_of_cells_without_the_split_has_no_regime_rows_and_the_regime_endpoint_is_empty()
    {
        await InsertContextAsync();
        var key = await StoredRunKeyAsync();

        var outcome = await ComputeAsync(key, byRegime: false);

        outcome.Regime.ShouldBeNull();
        (await GetAsync("/api/cells/regimes")).GetArrayLength().ShouldBe(0);
        (await GetAsync("/api/cells")).GetArrayLength().ShouldBe(outcome.Cells * 3);
    }

    [Fact]
    public async Task With_no_context_stored_every_trade_is_counted_as_having_none_and_there_are_no_regime_cells()
    {
        var key = await StoredRunKeyAsync();

        var outcome = await ComputeAsync(key, byRegime: true);

        outcome.RegimeCells.ShouldBe(0);
        outcome.Regime!.Labelled.ShouldBe(0);
        outcome.Regime.UnavailableByReason.ShouldBe(new Dictionary<string, int> { ["no_context"] = outcome.Trades });
        (await GetAsync("/api/cells/regimes")).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_labels_per_day_start_where_the_regime_is_ready_and_say_why_before_and_a_bad_range_is_refused()
    {
        await InsertContextAsync();

        var labels = await GetAsync("/api/data/regimes?from=2024-06-03&to=2025-06-30");

        labels.GetProperty("contextDays").GetInt32().ShouldBeGreaterThan(450);
        var days = labels.GetProperty("days").EnumerateArray().ToList();
        days.Count.ShouldBe(labels.GetProperty("sessions").GetInt32());
        days[0].GetProperty("unavailable").GetString().ShouldBe("before_history");
        days[100].GetProperty("unavailable").GetString().ShouldBe("regime_not_ready");
        var last = days[^1];
        last.GetProperty("regime").GetString().ShouldNotBeNullOrEmpty();
        last.GetProperty("basis").GetString().ShouldBe("index_and_vix");
        last.GetProperty("contextAsOf").GetString().ShouldNotBeNull();
        labels.GetProperty("counts").GetArrayLength().ShouldBe(9);
        labels.GetProperty("firstLabel").GetString().ShouldNotBeNull();
        using var bad = await Client.GetAsync(new Uri("/api/data/regimes?from=2026-03-01&to=2026-02-01", UriKind.Relative), Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
