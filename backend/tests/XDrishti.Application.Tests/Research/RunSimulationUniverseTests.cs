using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>Criteria 3 and 4 of P3-04a: the universe (a list in the order given, or a manual basket as of today) and the skip list with its precedence.</summary>
public sealed class RunSimulationUniverseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A fake engine that gives every instrument that was read a trade, so that the basket ids on trades can be seen.</summary>
    private static void TradesForEveryRunInstrument(RunFixture fixture) =>
        fixture.Engine.Script = input => input.RawBars.Count == 0 || input.PresetSkip is not null
            ? FakeSimulationEngine.Default(input)
            : new InstrumentReplay(FakeSimulationEngine.Record(input, null), [FakeSimulationEngine.Trade(input, RunFixture.D2)], []);

    [Fact]
    public async Task A_list_is_run_in_the_order_given_the_missing_row_is_skipped_and_trades_carry_no_basket_id()
    {
        var fixture = new RunFixture();
        TradesForEveryRunInstrument(fixture);
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var missing = Guid.CreateVersion7();
        var tcs = fixture.Add("NSE:TCS", RunFixture.ThreeDays());
        fixture.BarQueries.FindInstrumentAsync(missing, Arg.Any<CancellationToken>()).Returns((BarsInstrument?)null);

        var result = await fixture.RunAsync(RunFixture.List([tcs, missing, abb]), Ct);

        var run = result.Value;
        run.Universe.Kind.ShouldBe(UniverseKind.InstrumentList);
        run.Universe.Label.ShouldBe("instruments as listed");
        run.Universe.BasketId.ShouldBeNull();
        run.Universe.Members.Select(m => (m.Id, m.Symbol)).ShouldBe([(tcs, "NSE:TCS"), (missing, "?"), (abb, "NSE:ABB")]);
        run.Instruments.Select(i => i.Skipped).ShouldBe([null, SkipReason.InstrumentNotFound, null]);
        fixture.Engine.Replays.Select(r => r.InstrumentId).ShouldBe([tcs, abb], "the instrument without a row is never given to the engine and nothing is read for it");
        fixture.Store.Reads.Select(r => r.InstrumentId).ShouldBe([tcs, abb]);
        run.Trades.Count.ShouldBe(2);
        run.Trades.ShouldAllBe(t => t.BasketIds.Count == 0);
    }

    [Fact]
    public async Task A_basket_is_run_as_of_today_in_its_own_order_retired_members_and_members_without_bars_included_and_every_trade_carries_the_basket_id()
    {
        var fixture = new RunFixture();
        TradesForEveryRunInstrument(fixture);
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var retired = fixture.Add("NSE:OLD", RunFixture.ThreeDays());
        var empty = fixture.Add("NSE:NEW");
        var basket = Guid.CreateVersion7();
        fixture.SetBasket(basket, "Nifty watch", null, (empty, "NSE:NEW", 2, 5m, false), (abb, "NSE:ABB", 0, 5m, false), (retired, "NSE:OLD", 1, 5m, true));

        var result = await fixture.RunAsync(RunFixture.Basket(basket), Ct);

        var run = result.Value;
        run.Universe.Kind.ShouldBe(UniverseKind.Basket);
        run.Universe.Label.ShouldBe("members as of today");
        (run.Universe.BasketId, run.Universe.BasketName).ShouldBe((basket, "Nifty watch"));
        run.Universe.Members.Select(m => m.Id).ShouldBe([abb, retired, empty], "the basket's order is the position, not the order the query returned");
        run.Instruments.Select(i => i.Skipped).ShouldBe([null, null, SkipReason.NoBars]);
        run.Trades.Count.ShouldBe(2);
        run.Trades.ShouldAllBe(t => t.BasketIds.SequenceEqual(new[] { basket }));
        await fixture.Baskets.Received(1).MembersAsync(basket, null, null, Arg.Any<CancellationToken>());
        await fixture.BarQueries.DidNotReceive().FindInstrumentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_portfolio_basket_or_an_unknown_id_is_not_found_because_the_query_knows_only_the_manual_baskets()
    {
        var fixture = new RunFixture();
        fixture.Baskets.MembersAsync(Arg.Any<Guid>(), null, null, Arg.Any<CancellationToken>()).Returns((XDrishti.Application.Market.Baskets.BasketMembers?)null);

        var result = await fixture.RunAsync(RunFixture.Basket(Guid.CreateVersion7()), Ct);

        result.Error.Code.ShouldBe(SimulationErrors.BasketNotFoundCode);
        result.Error.Kind.ShouldBe(XDrishti.Domain.Common.ErrorKind.NotFound);
        fixture.Store.Reads.ShouldBeEmpty();
        fixture.Calendar.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_archived_basket_is_not_found()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var basket = Guid.CreateVersion7();
        fixture.SetBasket(basket, "Old", DateTimeOffset.UnixEpoch, (abb, "NSE:ABB", 0, 5m, false));

        var result = await fixture.RunAsync(RunFixture.Basket(basket), Ct);

        result.Error.Code.ShouldBe(SimulationErrors.BasketNotFoundCode);
        fixture.Store.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_basket_of_one_member_runs()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var basket = Guid.CreateVersion7();
        fixture.SetBasket(basket, "One", null, (abb, "NSE:ABB", 0, 5m, false));

        var result = await fixture.RunAsync(RunFixture.Basket(basket), Ct);

        result.Value.Instruments.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_empty_basket_succeeds_with_an_empty_record_that_says_so()
    {
        var fixture = new RunFixture();
        var basket = Guid.CreateVersion7();
        fixture.SetBasket(basket, "Empty", null);

        var result = await fixture.RunAsync(RunFixture.Basket(basket), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Instruments.ShouldBeEmpty();
        result.Value.Trades.ShouldBeEmpty();
        result.Value.Universe.Label.ShouldBe("members as of today");
    }

    [Fact]
    public async Task Each_skip_reason_is_recorded_with_the_precedence_of_the_spec_and_the_run_succeeds()
    {
        var fixture = new RunFixture();
        var days = RunFixture.ThreeDays();
        var missing = Guid.CreateVersion7();
        fixture.BarQueries.FindInstrumentAsync(missing, Arg.Any<CancellationToken>()).Returns((BarsInstrument?)null);
        var empty = fixture.Add("EMPTY");
        var twice = fixture.Add("TWICE", days);
        var badTick = fixture.Add("BADTICK", days, tickPaise: 150m);
        var both = fixture.Add("BOTH", days, tickPaise: -5m);
        var decided = new[] { fixture.Add("RUNNABLE", days), fixture.Add("WINDOW", days), fixture.Add("READY", days) };
        fixture.CsvThrough[twice] = RunFixture.D3;
        fixture.CsvThrough[both] = RunFixture.D3;
        fixture.Factors.GetAsync(twice, Arg.Any<CancellationToken>()).Returns(RunFixture.Split(RunFixture.D2));
        fixture.Factors.GetAsync(both, Arg.Any<CancellationToken>()).Returns(RunFixture.Split(RunFixture.D2));
        var byReplay = new Dictionary<Guid, SkipReason>
        {
            [decided[0]] = SkipReason.NoRunnableSessions,
            [decided[1]] = SkipReason.NoSessionsInWindow,
            [decided[2]] = SkipReason.FeaturesNotReady,
        };
        fixture.Engine.Script = input => byReplay.TryGetValue(input.InstrumentId, out var reason)
            ? new InstrumentReplay(FakeSimulationEngine.Record(input, reason), [], [])
            : FakeSimulationEngine.Default(input);
        Guid[] ids = [missing, empty, twice, badTick, both, .. decided];

        var result = await fixture.RunAsync(RunFixture.List(ids), Ct);

        var run = result.Value;
        run.Instruments.Select(i => i.Skipped).ShouldBe(
        [
            SkipReason.InstrumentNotFound, SkipReason.NoBars, SkipReason.CsvAdjustedTwice, SkipReason.TickSizeInvalid, SkipReason.CsvAdjustedTwice,
            SkipReason.NoRunnableSessions, SkipReason.NoSessionsInWindow, SkipReason.FeaturesNotReady,
        ]);
        run.Trades.ShouldBeEmpty();
        run.Totals.Instruments.ShouldBe(ids.Length);
        var given = fixture.Engine.Replays.ToDictionary(r => r.InstrumentId);
        given[empty].RawBars.ShouldBeEmpty("an empty read is handed to the engine as it is: the replay decides no_bars");
        given[empty].PresetSkip.ShouldBeNull();
        given[twice].PresetSkip.ShouldBe(SkipReason.CsvAdjustedTwice);
        given[twice].Tick.ShouldNotBeNull();
        given[badTick].PresetSkip.ShouldBe(SkipReason.TickSizeInvalid);
        given[badTick].Tick.ShouldBeNull();
        given[badTick].SkipDetail.ShouldNotBeNullOrEmpty();
        given[both].PresetSkip.ShouldBe(SkipReason.CsvAdjustedTwice, "double adjustment comes before the tick in the precedence");
        given[both].Tick.ShouldBeNull("nothing is scanned, so the record has no tick");
        given[both].SkipDetail.ShouldNotBeNull().ShouldContain("tick");
        given[decided[0]].PresetSkip.ShouldBeNull();
        given.ContainsKey(missing).ShouldBeFalse();
    }

    [Fact]
    public async Task A_run_in_which_every_instrument_is_skipped_succeeds_and_says_so()
    {
        var fixture = new RunFixture();
        var a = fixture.Add("A");
        var b = fixture.Add("B");

        var result = await fixture.RunAsync(RunFixture.List([a, b]), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Instruments.Select(i => i.Skipped).ShouldBe([SkipReason.NoBars, SkipReason.NoBars]);
        result.Value.Trades.ShouldBeEmpty();
        result.Value.Totals.Instruments.ShouldBe(2);
    }

    [Fact]
    public async Task The_double_adjustment_guard_applies_only_to_an_action_that_changes_the_bars_and_falls_inside_the_csv_range_and_the_acknowledgement_lifts_it()
    {
        var fixture = new RunFixture();
        var days = RunFixture.ThreeDays();
        var inside = fixture.Add("INSIDE", days);
        var beforeFirstBar = fixture.Add("BEFORE", days);
        var afterTheFiles = fixture.Add("AFTER", days);
        var noCsv = fixture.Add("NOCSV", days);
        var acknowledged = fixture.Add("ACK", days);
        fixture.CsvThrough[inside] = RunFixture.D3;
        fixture.CsvThrough[beforeFirstBar] = RunFixture.D3;
        fixture.CsvThrough[afterTheFiles] = RunFixture.D2;
        fixture.CsvThrough[acknowledged] = RunFixture.D3;
        fixture.Factors.GetAsync(inside, Arg.Any<CancellationToken>()).Returns(RunFixture.Split(RunFixture.D2));
        fixture.Factors.GetAsync(beforeFirstBar, Arg.Any<CancellationToken>()).Returns(RunFixture.Split(RunFixture.D1)); // an ex-date on the first bar's date changes no bar read
        fixture.Factors.GetAsync(afterTheFiles, Arg.Any<CancellationToken>()).Returns(RunFixture.Split(RunFixture.D3));
        fixture.Factors.GetAsync(noCsv, Arg.Any<CancellationToken>()).Returns(RunFixture.Split(RunFixture.D2));
        fixture.Factors.GetAsync(acknowledged, Arg.Any<CancellationToken>()).Returns(RunFixture.Split(RunFixture.D2));

        var refused = await fixture.RunAsync(RunFixture.List([inside, beforeFirstBar, afterTheFiles, noCsv]), Ct);
        var consented = await fixture.RunAsync(RunFixture.List([acknowledged]) with { AcknowledgeCsvAdjusted = true }, Ct);

        refused.Value.Instruments.Select(i => i.Skipped).ShouldBe([SkipReason.CsvAdjustedTwice, null, null, null]);
        consented.Value.Instruments.Single().Skipped.ShouldBeNull();
        consented.Value.AcknowledgeCsvAdjusted.ShouldBeTrue();
        var ackInput = fixture.Engine.Replays.Single(r => r.InstrumentId == acknowledged);
        ackInput.PresetSkip.ShouldBeNull();
        ackInput.CsvAdjustedThrough.ShouldBe(RunFixture.D3, "the replay marks each action on CSV bars");
        fixture.Engine.Replays.Single(r => r.InstrumentId == noCsv).CsvAdjustedThrough.ShouldBeNull();
    }

    [Fact]
    public async Task The_acknowledgement_is_part_of_the_record_whether_or_not_there_is_anything_to_acknowledge()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());

        var plain = await fixture.RunAsync(RunFixture.List([abb]), Ct);
        var acknowledged = await fixture.RunAsync(RunFixture.List([abb]) with { AcknowledgeCsvAdjusted = true }, Ct);

        plain.Value.Instruments.Select(i => i.Skipped).ShouldBe(acknowledged.Value.Instruments.Select(i => i.Skipped));
        acknowledged.Value.RunKey.ShouldNotBe(plain.Value.RunKey);
    }

    [Fact]
    public async Task The_same_request_twice_gives_equal_records_and_the_same_key()
    {
        var fixture = new RunFixture();
        TradesForEveryRunInstrument(fixture);
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var request = RunFixture.List([abb]);

        var first = await fixture.RunAsync(request, Ct);
        var second = await fixture.RunAsync(request, Ct);

        second.Value.RunKey.ShouldBe(first.Value.RunKey);
        second.Value.Trades.ShouldBe(first.Value.Trades);
    }

    [Fact]
    public async Task The_engine_version_and_the_strategy_record_of_the_port_are_written_into_the_record()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());

        var result = await fixture.RunAsync(RunFixture.List([abb]), Ct);

        result.Value.EngineVersion.ShouldBe("fake-1");
        result.Value.Strategy.Id.ShouldBe("ORB");
        result.Value.Strategy.EvaluationTimeframe.ShouldBe(Timeframe.M15);
        result.Value.Options.ShouldBe(ReplayOptions.Default);
        result.Value.RunKey.ShouldNotBeNullOrEmpty();
    }
}
