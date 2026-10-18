using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation;
using XDrishti.Simulation.Replay;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// The database half of P3-04a against a real TimescaleDB (criteria 5, 6, 7, 8 and 13): the handler is resolved from the container and run through the real ports (the bar store, the instrument rows, the baskets, the corporate actions, the trading
/// calendar), with the invented days of P3-03 seeded through the real stores. The expected values are computed by direct, independent calls of the replay over bars the test reads from the database itself.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class RunSimulationDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly D1 = ReplayDays.D1;

    private static readonly DateOnly D2 = ReplayDays.D2;

    private static readonly DateOnly D3 = ReplayDays.D3;

    private static readonly DateOnly D4 = ReplayDays.D4;

    /// <summary>The 0.1 % entry limit and no slippage: the exact numbers of P3-03's tables.</summary>
    private static readonly FillRulesInput Exact = new(0.1, 0, 0, 0);

    private static readonly JsonSerializerOptions Json = new() { IncludeFields = false };

    private async Task<Result<SimulationRun>> RunAsync(SimulationRequest request)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
        return await handler.HandleAsync(new RunSimulationQuery(request), Ct);
    }

    private static SimulationRequest Request(IReadOnlyList<Guid> ids, bool acknowledge = false, DateOnly? from = null, DateOnly? to = null) =>
        new("ORB", null, "15m", from ?? D1, to ?? D3, ids, null, acknowledge, null, Exact);

    /// <summary>Stores the bars through the real bar store; a file name records them as a loaded CSV file (what <c>ListCsvAdjustedThroughAsync</c> reads).</summary>
    private async Task LoadAsync(Guid id, IEnumerable<Bar> bars, BarSource source = BarSource.Dhan, string? csvFile = null)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBarStore>();
        var list = bars.ToArray();
        await using var session = await store.BeginMergeAsync(id, source, Ct);
        await session.AddAsync(list, Ct);
        var mark = csvFile is null ? null : new CsvFileMark(csvFile, list.Length, DateTimeOffset.UnixEpoch, list.Length, 0, 0, list[0].Start, list[^1].Start);
        await session.CompleteAsync(mark, Ct);
    }

    private async Task<IReadOnlyList<Bar>> ReadAllAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBarStore>().ReadAsync(id, DateTimeOffset.UnixEpoch, new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero), Ct);
    }

    private async Task SetTickAsync(Guid id, decimal? paise)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "UPDATE market.instruments SET tick_size = @tick WHERE id = @id", ("tick", paise is null ? DBNull.Value : paise.Value), ("id", id));
    }

    private async Task AddSplitAsync(Guid id, DateOnly exDate, bool acknowledge = false)
    {
        using var response = await Client.PostJsonAsync(
            "/api/data/corporate-actions" + (acknowledge ? "?acknowledgeCsvAdjusted=true" : string.Empty),
            new { instrumentId = id, exDate = exDate.ToString("yyyy-MM-dd"), kind = "split", numerator = 1, denominator = 2 },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<Guid> InstrumentAsync(string ticker, IEnumerable<Bar>? bars = null, decimal? tickPaise = 5m, BarSource source = BarSource.Dhan)
    {
        var id = await SeedAsync(ticker);
        await SetTickAsync(id, tickPaise);
        if (bars is not null)
        {
            await LoadAsync(id, bars, source);
        }

        return id;
    }

    private static string Text(object value) => JsonSerializer.Serialize(value, Json);

    private static Bar[] A() => ReplayDays.Join(ReplayDays.Flat(D1, 99.00), ReplayDays.F1(D2), ReplayDays.Flat(D3, 105.00));

    private static Bar[] B() => ReplayDays.Join(ReplayDays.F2(D1), ReplayDays.Flat(D2, 98.50), ReplayDays.Flat(D3, 98.50));

    /// <summary>The replay the test makes itself: the bars, factors and calendar read through the ports by the test, the replay called directly.</summary>
    private async Task<InstrumentReplay> DirectReplayAsync(Guid id, string symbol, decimal? tickPaise, bool acknowledge = false)
    {
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var raw = await ReadAllAsync(id);
        var factors = await services.GetRequiredService<IAdjustmentFactors>().GetAsync(id, Ct);
        var calendar = await services.GetRequiredService<ITradingCalendar>().GetAsync(Ct);
        var through = await services.GetRequiredService<XDrishti.Application.Abstractions.Market.Actions.ICorporateActionQueries>().ListCsvAdjustedThroughAsync([id], Ct);
        var tick = TickSpec.FromMasterPaise(tickPaise).Value;
        var prepared = BuiltInStrategies.Prepare("ORB", null, Timeframe.M15, FillRules.Create(0.1, SlippageConfig.None).Value).Strategy.ShouldNotBeNull();
        DateOnly? csv = through.TryGetValue(id, out var last) ? last : null;
        var twice = !acknowledge && raw.Count > 0 && csv is { } c && factors.ActionsAfter(SessionClock.DateOf(raw[0].EpochSeconds)).Any(a => a.ExDate <= c);
        return InstrumentReplayer.Replay(prepared, new InstrumentReplayInput(id, symbol, raw, factors, csv, calendar, ReplayOptions.Default, D1, D3, tick, twice ? SkipReason.CsvAdjustedTwice : null, twice ? "direct" : null));
    }

    // criterion 6

    [Fact]
    public async Task A_split_after_the_last_day_halves_every_price_exactly_once_and_equals_a_direct_run_on_the_halved_bars()
    {
        var id = await InstrumentAsync("ABB", A());
        await AddSplitAsync(id, D4);

        var result = await RunAsync(Request([id]));

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var trade = result.Value.Trades.Single().Trade;
        (trade.EntryPrice, trade.StopPrice, trade.TargetPrice, trade.ExitPrice, trade.RExact).ShouldBe((50.25, 49.00, 52.75, 52.75, 2.0));
        var halved = ReplayDays.Scaled(A(), 0.5, 2);
        var prepared = BuiltInStrategies.Prepare("ORB", null, Timeframe.M15, FillRules.Create(0.1, SlippageConfig.None).Value).Strategy.ShouldNotBeNull();
        var direct = InstrumentReplayer.Replay(prepared, new InstrumentReplayInput(id, "NSE:ABB", halved, AdjustmentFactors.None, null, new(), ReplayOptions.Default, D1, D3, TickSpec.FromMasterPaise(5m).Value));
        Text(direct.Trades).ShouldBe(Text(result.Value.Trades.Select(t => t.Trade).ToList()));
        result.Value.Instruments.Single().Adjustments.Single().ShouldBe(new AppliedAdjustment(D4, CorporateActionKind.Split, 1, 2, 0.5m, false));
    }

    [Fact]
    public async Task A_split_on_the_second_day_makes_the_series_continuous_so_that_day_is_not_flagged_and_without_the_action_it_is()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(D1, 100.00), ReplayDays.Flat(D2, 50.05));
        var withSplit = await InstrumentAsync("WITH", bars);
        var without = await InstrumentAsync("WITHOUT", bars);
        await AddSplitAsync(withSplit, D2);

        var result = await RunAsync(Request([withSplit, without], to: D2));

        var runs = result.Value.Instruments;
        runs[0].Discontinuities.ShouldBeEmpty("the adjusted d1 close is 50.00 and the open of d2 is 50.05: ratio 1.001");
        runs[1].Discontinuities.Single().Date.ShouldBe(D2);
        runs[1].Discontinuities.Single().Ratio.ShouldBe(0.5005, 1e-9);
    }

    [Fact]
    public async Task A_csv_adjusted_series_with_an_action_inside_its_range_is_skipped_keeps_its_counts_and_runs_with_the_acknowledgement_marking_the_adjustment()
    {
        var csv = await InstrumentAsync("CSVSER", null);
        await LoadAsync(csv, A(), BarSource.Csv, "CSVSER_1m.csv.gz");
        await AddSplitAsync(csv, D2, acknowledge: true);

        var refused = await RunAsync(Request([csv]));
        var acknowledged = await RunAsync(Request([csv], acknowledge: true));

        var skipped = refused.Value.Instruments.Single();
        skipped.Skipped.ShouldBe(SkipReason.CsvAdjustedTwice);
        skipped.Sessions.Ran.ShouldBe(3, "a skipped instrument that was read keeps its session counts");
        skipped.BarsSha256.ShouldBe(BarStreamHash.Compute(await ReadAllAsync(csv)));
        skipped.BarsRead.ShouldBe(3 * 375);
        refused.Value.Trades.ShouldBeEmpty();
        var ran = acknowledged.Value.Instruments.Single();
        ran.Skipped.ShouldBeNull();
        ran.Adjustments.Single().OnCsvBars.ShouldBeTrue();
        acknowledged.Value.AcknowledgeCsvAdjusted.ShouldBeTrue();
        acknowledged.Value.RunKey.ShouldNotBe(refused.Value.RunKey);
    }

    [Fact]
    public async Task A_mixed_source_series_is_skipped_the_same_way_and_an_instrument_absent_from_the_csv_answer_is_not()
    {
        var mixed = await InstrumentAsync("MIXED", null);
        await LoadAsync(mixed, ReplayDays.Flat(D1, 99.00), BarSource.Dhan);
        await LoadAsync(mixed, ReplayDays.Join(ReplayDays.F1(D2), ReplayDays.Flat(D3, 105.00)), BarSource.Csv, "MIXED_1m.csv.gz");
        await AddSplitAsync(mixed, D3, acknowledge: true);
        var dhanOnly = await InstrumentAsync("DHANONLY", A());
        await AddSplitAsync(dhanOnly, D3);

        var result = await RunAsync(Request([mixed, dhanOnly]));

        result.Value.Instruments.Select(i => i.Skipped).ShouldBe([SkipReason.CsvAdjustedTwice, null], "the known edge of the rule, pinned so that a change of it is deliberate");
        result.Value.Instruments[0].Sessions.Ran.ShouldBe(3);
    }

    [Fact]
    public async Task An_action_dated_after_the_last_bar_the_csv_files_reach_does_not_trigger_the_guard()
    {
        var csv = await InstrumentAsync("CSVSER", null);
        await LoadAsync(csv, A(), BarSource.Csv, "CSVSER_1m.csv.gz");
        await AddSplitAsync(csv, D4);

        var result = await RunAsync(Request([csv]));

        result.Value.Instruments.Single().Skipped.ShouldBeNull();
    }

    // criterion 7

    [Fact]
    public async Task The_instrument_row_carries_the_tick_and_a_run_uses_it_a_missing_one_falls_back_and_an_invalid_one_is_skipped_and_the_next_runs()
    {
        var five = await InstrumentAsync("FIVEPAISE", A(), 5m);
        var none = await InstrumentAsync("NOTICK", A(), null);
        var bad = await InstrumentAsync("BADTICK", A(), 150m);
        var next = await InstrumentAsync("NEXT", A(), 10m);
        using (var scope = Factory.Services.CreateScope())
        {
            var queries = scope.ServiceProvider.GetRequiredService<IBarQueries>();
            (await queries.FindInstrumentAsync(five, Ct))!.TickSize.ShouldBe(5m);
            (await queries.FindInstrumentAsync(none, Ct))!.TickSize.ShouldBeNull();
            (await queries.FindInstrumentAsync(Guid.CreateVersion7(), Ct)).ShouldBeNull();
        }

        var result = await RunAsync(Request([five, none, bad, next]));

        var runs = result.Value.Instruments;
        runs.Select(r => r.Skipped).ShouldBe([null, null, SkipReason.TickSizeInvalid, null]);
        runs[0].TickSize.ShouldBe(0.05m);
        runs[0].TickFromFallback.ShouldBeFalse();
        runs[1].TickSize.ShouldBe(0.05m);
        runs[1].TickFromFallback.ShouldBeTrue();
        runs[2].SkipDetail.ShouldNotBeNullOrEmpty();
        runs[2].Sessions.Ran.ShouldBe(3, "the skipped instrument was read and keeps its counts");
        runs[3].TickSize.ShouldBe(0.10m);
        result.Value.Trades.Count.ShouldBe(3);
    }

    // criterion 8

    [Fact]
    public async Task The_record_is_what_the_assembler_gives_for_independent_replays_the_hash_is_that_of_the_stored_bars_and_a_merge_that_moves_one_close_changes_the_hash_and_the_key()
    {
        var a = await InstrumentAsync("ABB", A());
        var b = await InstrumentAsync("TCS", B());

        var result = await RunAsync(Request([a, b]));

        var run = result.Value;
        var replays = new[] { await DirectReplayAsync(a, "NSE:ABB", 5m), await DirectReplayAsync(b, "NSE:TCS", 5m) };
        var prepared = BuiltInStrategies.Prepare("ORB", null, Timeframe.M15, FillRules.Create(0.1, SlippageConfig.None).Value).Strategy.ShouldNotBeNull();
        using var scope = Factory.Services.CreateScope();
        var calendar = await scope.ServiceProvider.GetRequiredService<ITradingCalendar>().GetAsync(Ct);
        var header = new RunHeader(
            prepared.Record, SimulationEngine.Version, D1, D3, ReplayOptions.Default, false, FillRules.Create(0.1, SlippageConfig.None).Value.ToCanonicalJson(),
            new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(a, "NSE:ABB"), new UniverseMember(b, "NSE:TCS")]), calendar);
        var expected = RunAssembler.Assemble(header, replays);
        Text(run).ShouldBe(Text(expected));
        run.RunKey.ShouldBe(expected.RunKey);
        (await ReadAllAsync(a)).Count.ShouldBe(3 * 375);
        run.Instruments[0].BarsSha256.ShouldBe(BarStreamHash.Compute(await ReadAllAsync(a)));
        run.Instruments[0].BarsSha256.ShouldBe(BarStreamHash.Compute(await ReadAllAsync(a)));
        run.Instruments[1].BarsSha256.ShouldBe(BarStreamHash.Compute(await ReadAllAsync(b)));

        var stored = (await ReadAllAsync(a)).ToArray();
        var at = Array.FindIndex(stored, bar => bar.EpochSeconds == Day.Epoch(D2, (10 * 60) + 30));
        stored[at] = stored[at] with { Close = stored[at].Close - 0.05 };
        await LoadAsync(a, [stored[at]], BarSource.Dhan);
        var changed = (await RunAsync(Request([a, b]))).Value;

        changed.Instruments[0].BarsSha256.ShouldNotBe(run.Instruments[0].BarsSha256);
        changed.Instruments[1].BarsSha256.ShouldBe(run.Instruments[1].BarsSha256);
        changed.RunKey.ShouldNotBe(run.RunKey);
    }

    // criterion 13

    [Fact]
    public async Task End_to_end_two_instruments_an_instrument_with_a_split_and_a_basket_of_the_three_through_the_real_ports()
    {
        var a = await InstrumentAsync("ABB", A());
        var b = await InstrumentAsync("TCS", B());
        var c = await InstrumentAsync("SPLITCO", ReplayDays.Scaled(A(), 2.0, 1), tickPaise: null);
        await AddSplitAsync(c, D4);
        var basket = await CreateBasketAsync("Lab", a, b, c);
        var request = new SimulationRequest("ORB", null, "15m", D1, D3, null, basket, false, null, Exact);

        var first = await RunAsync(request);
        var second = await RunAsync(request);

        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Message : string.Empty);
        var run = first.Value;
        run.Universe.Label.ShouldBe("members as of today");
        run.Universe.BasketId.ShouldBe(basket);
        run.Universe.Members.Select(m => m.Id).ShouldBe([a, b, c]);
        run.Trades.Count.ShouldBe(3);
        run.Trades.ShouldAllBe(t => t.BasketIds.SequenceEqual(new[] { basket }));
        var f1 = run.Trades.Single(t => t.Trade.Symbol == "NSE:ABB").Trade;
        (f1.Side, f1.TradeDate, f1.EntryPrice, f1.StopPrice, f1.TargetPrice, f1.ExitPrice, f1.ExitReason, f1.RExact, f1.MaeR, f1.MfeR, f1.Fills).ShouldBe(
            (Side.Buy, D2, 100.50, 98.00, 105.50, 105.50, ExitReason.Target, 2.0, 0.4, 2.0, FillBasis.Exact));
        var f2 = run.Trades.Single(t => t.Trade.Symbol == "NSE:TCS").Trade;
        (f2.Side, f2.TradeDate, f2.EntryPrice, f2.StopPrice, f2.ExitPrice, f2.ExitReason, f2.RExact, f2.MaeR, f2.MfeR, f2.Fills).ShouldBe(
            (Side.Buy, D1, 100.50, 98.00, 98.00, ExitReason.Stop, -1.0, 1.0, 0.4, FillBasis.Exact));
        var split = run.Trades.Single(t => t.Trade.Symbol == "NSE:SPLITCO").Trade;
        (split.EntryPrice, split.StopPrice, split.TargetPrice, split.ExitPrice, split.RExact, split.TickFallback).ShouldBe((100.50, 98.00, 105.50, 105.50, 2.0, true));

        var replays = new[] { await DirectReplayAsync(a, "NSE:ABB", 5m), await DirectReplayAsync(b, "NSE:TCS", 5m), await DirectReplayAsync(c, "NSE:SPLITCO", null) };
        for (var i = 0; i < 3; i++)
        {
            Text(run.Instruments[i]).ShouldBe(Text(replays[i].Record), "the replay counters and the data notes are those of the replay for the same bars");
        }

        run.Totals.Instruments.ShouldBe(3);
        run.Totals.InstrumentsRun.ShouldBe(3);
        run.Totals.BarsRead.ShouldBe(run.Instruments.Sum(i => i.BarsRead));
        run.Totals.BarsFed.ShouldBe(run.Instruments.Sum(i => i.BarsFed));
        run.Totals.Sessions.Ran.ShouldBe(run.Instruments.Sum(i => i.Sessions.Ran));
        run.Totals.TradesInWindow.ShouldBe(run.Instruments.Sum(i => i.TradesInWindow));
        run.Totals.Fates.Trades.ShouldBe(3);
        run.Totals.CandidatesInWindow.ShouldBe(run.Instruments.Sum(i => i.CandidatesInWindow));
        second.Value.RunKey.ShouldBe(run.RunKey);
        Text(second.Value).ShouldBe(Text(run));
    }

    [Fact]
    public async Task In_the_same_run_an_instrument_skipped_for_the_double_adjustment_and_one_with_an_empty_read_keep_the_counts_and_hash_of_the_real_replay()
    {
        var ok = await InstrumentAsync("ABB", A());
        var csv = await InstrumentAsync("CSVSER", null);
        await LoadAsync(csv, A(), BarSource.Csv, "CSVSER_1m.csv.gz");
        await AddSplitAsync(csv, D2, acknowledge: true);
        var empty = await InstrumentAsync("EMPTY");

        var result = await RunAsync(Request([ok, csv, empty, Guid.CreateVersion7()]));

        var runs = result.Value.Instruments;
        runs.Select(r => r.Skipped).ShouldBe([null, SkipReason.CsvAdjustedTwice, SkipReason.NoBars, SkipReason.InstrumentNotFound]);
        runs[1].SkipDetail.ShouldNotBeNull().ShouldContain("CSV");
        Text(runs[1]).ShouldBe(Text((await DirectReplayAsync(csv, "NSE:CSVSER", 5m)).Record with { SkipDetail = runs[1].SkipDetail }));
        Text(runs[2]).ShouldBe(Text((await DirectReplayAsync(empty, "NSE:EMPTY", 5m)).Record));
        runs[2].BarsSha256.ShouldBe(BarStreamHash.Compute([]));
        runs[3].Symbol.ShouldBe("?");
        result.Value.Totals.Skipped.Sum(s => s.Instruments).ShouldBe(3);
    }

    [Fact]
    public async Task A_window_that_ends_before_the_first_bar_reads_nothing_and_the_database_accepts_the_bounds_with_offset_zero()
    {
        var id = await InstrumentAsync("ABB", A());

        var result = await RunAsync(Request([id], from: new DateOnly(2026, 2, 2), to: new DateOnly(2026, 2, 27)));

        result.Value.Instruments.Single().Skipped.ShouldBe(SkipReason.NoBars);
    }

    // the universe

    [Fact]
    public async Task A_portfolio_basket_an_archived_basket_and_an_unknown_id_are_not_found()
    {
        var id = await InstrumentAsync("ABB", A());
        var archived = await CreateBasketAsync("Old", id);
        using (var archive = await Client.DeleteAsync(new Uri($"/api/baskets/{archived}", UriKind.Relative), Ct))
        {
            archive.StatusCode.ShouldBe(HttpStatusCode.NoContent, await archive.Content.ReadAsStringAsync(Ct));
        }

        var elevated = (await Factory.SignInElevatedAsync(Ct)).Client;
        await elevated.CreateAccountAsync(AccountsApi.NewAccount("Dhan - Main", "1000000001", roles: new { portfolio = new { enabled = true } }), Ct);
        using var buckets = await elevated.GetAsync("/api/portfolio-buckets", Ct);
        var portfolio = (await buckets.ReadAsync<List<XDrishti.Application.Accounts.Buckets.BucketView>>(Ct)).Single().Id;

        foreach (var unreachable in new[] { archived, portfolio, Guid.CreateVersion7() })
        {
            var result = await RunAsync(new SimulationRequest("ORB", null, "15m", D1, D3, null, unreachable, false, null, Exact));

            result.Error.Code.ShouldBe(SimulationErrors.BasketNotFoundCode);
        }
    }

    [Fact]
    public async Task A_request_the_engine_refuses_fails_before_the_database_is_asked_for_a_bar()
    {
        var id = await InstrumentAsync("ABB", A());

        var unknown = await RunAsync(Request([id]) with { StrategyId = "MACD" });
        var parameter = await RunAsync(Request([id]) with { Parameters = new Dictionary<string, double> { ["bogus"] = 1 } });
        var compile = await RunAsync(Request([id]) with { Parameters = new Dictionary<string, double> { ["or_min"] = 20 } });

        unknown.Error.Code.ShouldBe(SimulationErrors.StrategyInvalidCode);
        unknown.Error.Fields.ShouldNotBeNull().Single().Code.ShouldBe(SimulationErrors.UnknownStrategyCode);
        parameter.Error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("parameters.bogus");
        compile.Error.Code.ShouldBe(SimulationErrors.StrategyInvalidCode);
        compile.Error.Fields.ShouldNotBeNull().ShouldNotBeEmpty();
    }

    private async Task<Guid> CreateBasketAsync(string name, params Guid[] members)
    {
        using var created = await Client.PostJsonAsync("/api/baskets", new { name }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var basket = (await created.Content.ReadFromJsonAsync<BasketView>(Ct))!.Id;
        using var added = await Client.PostJsonAsync($"/api/baskets/{basket}/members", new { instrumentIds = members }, Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync(Ct));
        return basket;
    }
}
