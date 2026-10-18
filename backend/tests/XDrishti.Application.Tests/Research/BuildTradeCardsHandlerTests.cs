using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Research;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Research;

/// <summary>Criterion 3 of P3-04c: the second read for the trade cards (C2), over fake ports.</summary>
public sealed class BuildTradeCardsHandlerTests
{
    private static readonly DateOnly D1 = ReplayDays.D1;

    private static readonly DateOnly D2 = ReplayDays.D2;

    private static readonly DateOnly D3 = ReplayDays.D3;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A run of five instruments over the three F days, with a trade on D2 for the instruments at positions 0, 2 and 4.</summary>
    private sealed class World
    {
        public World(IReadOnlyList<AppliedAdjustment>? adjustments = null, params int[] withTrade)
        {
            foreach (var symbol in new[] { "NSE:AAA", "NSE:BBB", "NSE:CCC", "NSE:DDD", "NSE:EEE" })
            {
                var id = Fixture.Add(symbol, RunFixture.ThreeDays());
                Ids.Add(id);
                Symbols.Add(symbol);
            }

            var traded = withTrade.Length == 0 ? new[] { 0, 2, 4 } : withTrade;
            var instruments = Ids.Select((id, i) => FabricatedRun.Ran(id, Symbols[i], D1, D3) with
            {
                BarsSha256 = BarStreamHash.Compute(Fixture.Store.Stored[id]),
                TickSize = 0.05m,
                Adjustments = adjustments ?? [],
            }).ToList();
            var trades = traded.Select(i => FabricatedRun.Trade(Ids[i], Symbols[i], Side.Buy, D2)).ToList();
            Run = FabricatedRun.Assemble(FabricatedRun.List([.. Ids.Select((id, i) => (id, Symbols[i]))]), instruments, trades, from: D1, to: D3);
        }

        public RunFixture Fixture { get; } = new();

        public List<Guid> Ids { get; } = [];

        public List<string> Symbols { get; } = [];

        public SimulationRun Run { get; }

        public BuildTradeCardsHandler Handler(IBarStore? store = null) => new(store ?? Fixture.Store, Fixture.BarQueries, Fixture.Factors);

        public string[] Keys(params int[] positions) => [.. positions.Select(p => Run.Trades.Single(t => t.Trade.InstrumentId == Ids[p]).Trade.Key)];

        public ValueTask<Result<IReadOnlyList<TradeCardData>>> Ask(IReadOnlyList<string> keys, CancellationToken cancellationToken) => Ask(keys, null, cancellationToken);

        public ValueTask<Result<IReadOnlyList<TradeCardData>>> Ask(IReadOnlyList<string> keys, IBarStore? store, CancellationToken cancellationToken) =>
            Handler(store).HandleAsync(new BuildTradeCardsQuery(Run, keys), cancellationToken);
    }

    [Fact]
    public async Task Only_the_instruments_with_a_chosen_trade_are_read_in_the_universe_order_one_at_a_time_and_the_cards_come_back_in_the_order_of_the_keys()
    {
        var world = new World();
        world.Fixture.Store.AwaitReads = true;

        var result = await world.Ask(world.Keys(4, 0, 2), Ct);

        result.IsSuccess.ShouldBeTrue();
        world.Fixture.Store.Reads.Select(r => r.InstrumentId).ShouldBe([world.Ids[0], world.Ids[2], world.Ids[4]], "the run's universe order, only the instruments with a card");
        world.Fixture.Store.MaxConcurrentReads.ShouldBe(1);
        result.Value.Select(c => c.Trade.Trade.InstrumentId).ShouldBe([world.Ids[4], world.Ids[0], world.Ids[2]], "in the order of the keys asked for");
        result.Value.ShouldAllBe(c => c.DayBars.Count == 375 && c.Trade.Trade.TradeDate == D2);
        world.Fixture.Baskets.ReceivedCalls().ShouldBeEmpty("the basket is never resolved again");
    }

    [Fact]
    public async Task The_bounds_of_the_read_are_those_of_the_run_from_the_year_2000_to_23_59_india_time_of_To_both_with_offset_zero()
    {
        var world = new World();

        await world.Ask(world.Keys(0), Ct);

        var (_, from, to) = world.Fixture.Store.Reads.Single();
        from.Offset.ShouldBe(TimeSpan.Zero);
        to.Offset.ShouldBe(TimeSpan.Zero);
        from.ShouldBe(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.FromMinutes(330)));
        to.ShouldBe(new DateTimeOffset(2026, 3, 4, 23, 59, 0, TimeSpan.FromMinutes(330)));
    }

    [Fact]
    public async Task An_empty_key_list_reads_nothing_and_a_merge_is_never_begun()
    {
        var world = new World();

        var result = await world.Ask([], Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
        world.Fixture.Store.Reads.ShouldBeEmpty();
        world.Fixture.BarQueries.ReceivedCalls().ShouldBeEmpty();
        world.Fixture.Factors.ReceivedCalls().ShouldBeEmpty();
        world.Fixture.Store.Sessions.ShouldBeEmpty();
        world.Fixture.Store.ForbidMerge.ShouldBeTrue("a merge would have thrown");
    }

    [Fact]
    public async Task Two_trades_of_one_instrument_on_one_day_are_one_read_and_two_cards_sharing_the_bars()
    {
        var world = new World(null, 0);
        var sell = new RunTrade(FabricatedRun.Trade(world.Ids[0], world.Symbols[0], Side.Sell, D2), []);
        var run = world.Run with { Trades = [.. world.Run.Trades, sell] };

        var result = await new BuildTradeCardsHandler(world.Fixture.Store, world.Fixture.BarQueries, world.Fixture.Factors).HandleAsync(new BuildTradeCardsQuery(run, [.. run.Trades.Select(t => t.Trade.Key)]), Ct);

        result.Value.Count.ShouldBe(2);
        world.Fixture.Store.Reads.Count.ShouldBe(1);
        result.Value[0].DayBars.ShouldBeSameAs(result.Value[1].DayBars);
    }

    [Fact]
    public async Task The_list_of_each_instrument_is_released_before_the_next_one_is_read()
    {
        var world = new World();
        var store = new TrackingStore(world.Fixture.Store);

        var result = await world.Ask(world.Keys(0, 2, 4), store, Ct);

        result.IsSuccess.ShouldBeTrue();
        store.Reads.ShouldBe(3);
        store.AliveAtNextRead.ShouldBe([false, false], "the list of the first read was collectable when the second began, and the second's when the third began");
    }

    // the guard

    private static async Task<Error> StaleAsync(World world)
    {
        var result = await world.Ask(world.Keys(0, 2, 4), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("simulation.cards_stale");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        return result.Error;
    }

    [Fact]
    public async Task A_bar_merged_since_the_run_is_stale_and_names_the_instrument_and_the_difference()
    {
        var world = new World();
        world.Fixture.Store.Stored[world.Ids[2]].Add(new Bar(Day.Epoch(D3, 600) + 30 * 60, 1, 1, 1, 1, 1));
        world.Fixture.Store.Stored[world.Ids[2]].Sort((a, b) => a.EpochSeconds.CompareTo(b.EpochSeconds));

        var error = await StaleAsync(world);

        error.Fields!.Single().Field.ShouldBe("NSE:CCC");
        error.Fields!.Single().Code.ShouldBe("bars_changed");
    }

    [Fact]
    public async Task A_changed_price_is_stale_even_when_the_number_of_bars_is_the_same()
    {
        var world = new World();
        var list = world.Fixture.Store.Stored[world.Ids[0]];
        list[10] = list[10] with { Close = list[10].Close + 0.05 };

        var error = await StaleAsync(world);

        error.Fields!.Single().Code.ShouldBe("bars_changed");
    }

    [Fact]
    public async Task An_action_added_since_the_run_is_stale()
    {
        var world = new World();
        world.Fixture.Factors.GetAsync(world.Ids[0], Arg.Any<CancellationToken>()).Returns(RunFixture.Split(D3));

        var error = await StaleAsync(world);

        error.Fields!.Single().Field.ShouldBe("NSE:AAA");
        error.Fields!.Single().Code.ShouldBe("adjustments_changed");
    }

    [Fact]
    public async Task A_changed_ratio_is_stale_and_the_same_actions_are_not()
    {
        var recorded = new[] { new AppliedAdjustment(D3, CorporateActionKind.Split, 1, 2, 0.5m, false) };
        var world = new World(recorded);
        world.Fixture.Factors.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(RunFixture.Split(D3));

        var same = await world.Ask(world.Keys(0, 2, 4), Ct);

        same.IsSuccess.ShouldBeTrue("the recorded split is the applied split");
        world.Fixture.Factors.GetAsync(world.Ids[2], Arg.Any<CancellationToken>()).Returns(AdjustmentFactors.From([new AppliedAction(D3, CorporateActionKind.Split, 1, 3, CorporateActionRatio.Factor(CorporateActionKind.Split, 1, 3))]));
        var error = await StaleAsync(world);
        error.Fields!.Single().Field.ShouldBe("NSE:CCC");
        error.Fields!.Single().Code.ShouldBe("adjustments_changed");
    }

    [Fact]
    public async Task A_changed_tick_is_stale()
    {
        var world = new World();
        world.Fixture.BarQueries.FindInstrumentAsync(world.Ids[4], Arg.Any<CancellationToken>()).Returns(new BarsInstrument(world.Ids[4], "NSE:EEE", "EEE Ltd", 10m));

        var error = await StaleAsync(world);

        error.Fields!.Single().Field.ShouldBe("NSE:EEE");
        error.Fields!.Single().Code.ShouldBe("tick_changed");
    }

    [Fact]
    public async Task A_tick_that_the_master_lost_is_stale_because_the_fallback_differs_from_the_recorded_tick()
    {
        var world = new World();
        world.Fixture.BarQueries.FindInstrumentAsync(world.Ids[0], Arg.Any<CancellationToken>()).Returns(new BarsInstrument(world.Ids[0], "NSE:AAA", "AAA Ltd", null));

        var error = await StaleAsync(world);

        error.Fields!.Single().Code.ShouldBe("tick_changed");
    }

    [Fact]
    public async Task A_member_that_vanished_is_stale()
    {
        var world = new World();
        world.Fixture.BarQueries.FindInstrumentAsync(world.Ids[2], Arg.Any<CancellationToken>()).Returns((BarsInstrument?)null);

        var error = await StaleAsync(world);

        error.Fields!.Single().Field.ShouldBe("NSE:CCC");
        error.Fields!.Single().Code.ShouldBe("instrument_missing");
    }

    [Fact]
    public async Task A_key_that_is_not_a_trade_of_the_run_is_simulation_invalid_and_nothing_is_read()
    {
        var world = new World();

        var result = await world.Ask([.. world.Keys(0), "ORB|NSE:ZZZ|Buy|2026-03-03"], Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("simulation.invalid");
        result.Error.Fields!.Single().Field.ShouldBe("trade_keys[1]");
        world.Fixture.Store.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_member_with_no_instrument_record_in_the_run_is_simulation_invalid_with_a_field_and_nothing_is_read()
    {
        var world = new World();
        var run = world.Run with { Instruments = [.. world.Run.Instruments.Where(i => i.InstrumentId != world.Ids[2])] };

        var result = await new BuildTradeCardsHandler(world.Fixture.Store, world.Fixture.BarQueries, world.Fixture.Factors).HandleAsync(new BuildTradeCardsQuery(run, world.Keys(2)), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("simulation.invalid");
        result.Error.Fields!.Single().Field.ShouldBe("run.instruments");
        world.Fixture.Store.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_token_cancelled_between_two_instruments_ends_the_call_with_the_cancellation()
    {
        var world = new World();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        world.Fixture.Store.OnRead = _ => cancel.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () => await world.Ask(world.Keys(0, 2, 4), cancel.Token));

        world.Fixture.Store.Reads.Count.ShouldBe(1, "the second instrument was not read");
    }

    // the adjustment of the chart bars

    [Fact]
    public async Task The_chart_bars_are_the_stored_bars_adjusted_as_the_replay_does_an_action_on_the_first_day_is_not_applied_and_one_on_the_trade_day_leaves_the_trade_day()
    {
        var world = new World(null, 0);
        var factors = AdjustmentFactors.From(
        [
            new AppliedAction(D1, CorporateActionKind.Split, 1, 3, CorporateActionRatio.Factor(CorporateActionKind.Split, 1, 3)),
            new AppliedAction(D3, CorporateActionKind.Split, 1, 2, CorporateActionRatio.Factor(CorporateActionKind.Split, 1, 2)),
        ]);
        world.Fixture.Factors.GetAsync(world.Ids[0], Arg.Any<CancellationToken>()).Returns(factors);
        var recorded = new AppliedAdjustment(D3, CorporateActionKind.Split, 1, 2, 0.5m, false);
        var run = world.Run with
        {
            Instruments = [.. world.Run.Instruments.Select(i => i.InstrumentId == world.Ids[0] ? i with { Adjustments = [recorded] } : i)],
            Trades = [world.Run.Trades[0], new RunTrade(FabricatedRun.Trade(world.Ids[0], world.Symbols[0], Side.Sell, D3), [])],
        };

        var result = await new BuildTradeCardsHandler(world.Fixture.Store, world.Fixture.BarQueries, world.Fixture.Factors).HandleAsync(new BuildTradeCardsQuery(run, [.. run.Trades.Select(t => t.Trade.Key)]), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var (beforeSplit, onSplitDay) = (result.Value[0], result.Value[1]);
        beforeSplit.DayBars.Count.ShouldBe(375);
        beforeSplit.DayBars[0].ShouldBe(new Bar(Day.Epoch(D2, 555), 49.5, 49.5, 49.5, 49.5, 2000), "the 09:15 bar of D2 halved, volume doubled");
        var at1110 = beforeSplit.DayBars.Single(b => b.EpochSeconds == Day.Epoch(D2, 670));
        (at1110.Open, at1110.High, at1110.Low, at1110.Close).ShouldBe((52.0, 52.75, 51.75, 52.5));
        beforeSplit.OpeningBar.ShouldBe(beforeSplit.DayBars[0]);
        beforeSplit.OpeningBarRaw.ShouldBe(new Bar(Day.Epoch(D2, 555), 99, 99, 99, 99, 1000), "the stored bar, because the factor of the day is not 1");
        onSplitDay.DayBars.ShouldBe(world.Fixture.Store.Stored[world.Ids[0]].Where(b => b.EpochSeconds >= Day.Epoch(D3, 555)).ToList(), "the trade day of the ex-date is not adjusted");
        onSplitDay.OpeningBarRaw.ShouldBeNull("the factor of the day is 1");
        onSplitDay.OpeningBar!.Value.Open.ShouldBe(105.0);
    }

    [Fact]
    public async Task The_opening_bar_is_null_on_a_day_with_no_09_15_bar_and_no_other_minute_stands_in_for_it()
    {
        var world = new World(null, 0);
        var list = world.Fixture.Store.Stored[world.Ids[0]];
        list.RemoveAll(b => b.EpochSeconds == Day.Epoch(D2, 555));
        var run = world.Run with { Instruments = [.. world.Run.Instruments.Select(i => i.InstrumentId == world.Ids[0] ? i with { BarsSha256 = BarStreamHash.Compute(list) } : i)] };

        var result = await new BuildTradeCardsHandler(world.Fixture.Store, world.Fixture.BarQueries, world.Fixture.Factors).HandleAsync(new BuildTradeCardsQuery(run, [run.Trades[0].Trade.Key]), Ct);

        var card = result.Value.Single();
        card.OpeningBar.ShouldBeNull();
        card.OpeningBarRaw.ShouldBeNull();
        card.DayBars.Count.ShouldBe(374);
        card.DayBars[0].EpochSeconds.ShouldBe(Day.Epoch(D2, 556));
    }

    [Fact]
    public async Task A_day_bar_before_the_session_or_after_it_is_not_in_the_chart()
    {
        var world = new World(null, 0);
        var list = world.Fixture.Store.Stored[world.Ids[0]];
        list.Add(new Bar(Day.Epoch(D2, 530), 1, 1, 1, 1, 1));
        list.Add(new Bar(Day.Epoch(D2, 18 * 60), 1, 1, 1, 1, 1));
        list.Sort((a, b) => a.EpochSeconds.CompareTo(b.EpochSeconds));
        var run = world.Run with { Instruments = [.. world.Run.Instruments.Select(i => i.InstrumentId == world.Ids[0] ? i with { BarsSha256 = BarStreamHash.Compute(list) } : i)] };

        var result = await new BuildTradeCardsHandler(world.Fixture.Store, world.Fixture.BarQueries, world.Fixture.Factors).HandleAsync(new BuildTradeCardsQuery(run, [run.Trades[0].Trade.Key]), Ct);

        result.Value.Single().DayBars.Count.ShouldBe(375);
    }

    /// <summary>A store that remembers each list it returned with a weak reference and, at the start of the next read, collects garbage and notes whether the earlier lists are still alive.</summary>
    private sealed class TrackingStore(FakeBarStore inner) : IBarStore
    {
        private readonly List<WeakReference> _lists = [];

        public int Reads => _lists.Count;

        public List<bool> AliveAtNextRead { get; } = [];

        public ValueTask<IBarMergeSession> BeginMergeAsync(Guid instrumentId, BarSource source, CancellationToken cancellationToken) => throw new InvalidOperationException("no merge");

        public async ValueTask<IReadOnlyList<Bar>> ReadAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            if (_lists.Count > 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                AliveAtNextRead.Add(_lists[^1].IsAlive);
            }

            var list = await inner.ReadAsync(instrumentId, from, to, cancellationToken);
            _lists.Add(new WeakReference(list));
            return list;
        }
    }
}
