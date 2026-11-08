using NSubstitute;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>
/// CF-07 (ADR 0208): the random-entry walk through the run handler. It prepares the strategy and reads the instruments of the plan only, over the window of the request and not a bar further, hands each to the random-entry engine instead of the replay, and
/// returns the draws in plan order. No run record is built and nothing is stored.
/// </summary>
public sealed class RunNullWalkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly RunFixture _fixture = new();

    private readonly IRandomEntryEngine _engine = Substitute.For<IRandomEntryEngine>();

    private RunSimulationHandler Handler(IRandomEntryEngine engine) =>
        new(_fixture.Store, _fixture.BarQueries, _fixture.Baskets, _fixture.Factors, _fixture.Actions, _fixture.Calendar, _fixture.Engine, _fixture.Strategies, _fixture.CostSheets, _fixture.ContextDays, _fixture.EventSnapshots, _fixture.FilingTimes, _fixture.SurpriseReader, _fixture.Time, _fixture.Log, engine, Substitute.For<IPlaceboEngine>(), _fixture.EntryTiers);

    private static NullTradeSpec Spec(int id, Guid instrument, DateOnly date) => new(id, instrument, date, Side.Buy, 0.01, 2, CostSegment.IntradayEquity);

    private void Answer() =>
        _engine.WalkRandomEntries(Arg.Any<IPreparedStrategy>(), Arg.Any<InstrumentReplayInput>(), Arg.Any<NullWalkPlan>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var mine = ci.Arg<NullWalkPlan>().Trades.Where(t => t.InstrumentId == ci.Arg<InstrumentReplayInput>().InstrumentId).ToList();
                foreach (var _ in mine)
                {
                    ci.Arg<IProgress<int>?>()?.Report(1);
                }

                return new NullWalkResult([.. mine.Select(t => new NullTradeDraws(t.Id, [0.1, -0.2], 1, null))]);
            });

    [Fact]
    public async Task A_strategy_that_names_min_turnover_gets_the_threshold_in_its_fills_and_the_lookup_in_the_walk_input_and_keeps_a_delay()
    {
        var a = _fixture.Add("NSE:AAA", RunFixture.ThreeDays());
        _fixture.Engine.ReadsTurnover = true;
        var tiers = NSubstitute.Substitute.For<XDrishti.Application.Abstractions.Market.Bars.IEntryTierSource>();
        tiers.TurnoverForAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new Func<DateOnly, decimal?>(_ => 1m));
        _fixture.EntryTiers = tiers;
        Answer();
        var plan = new NullWalkPlan([Spec(0, a, RunFixture.D2)], 2, 5, 7);
        var fills = new FillRulesInput(0.1, 2, 0.10, 2, EntryDelayMinutes: 2);

        var result = await Handler(_engine).HandleAsync(new RunNullWalkQuery(RunFixture.List([a], RunFixture.D1, RunFixture.D2) with { Fills = fills }, plan, null), Ct);

        result.IsSuccess.ShouldBeTrue();
        var prepared = _fixture.Engine.Prepared.Last().Fills.ShouldNotBeNull();
        (prepared.MinTurnoverCrore, prepared.EntryDelayMinutes).ShouldBe((20m, 2), "the threshold is added and the delay of a delay or hold-out request is kept");
    }

    [Fact]
    public async Task Only_the_instruments_of_the_plan_are_read_over_the_window_of_the_request_and_nothing_is_replayed()
    {
        var a = _fixture.Add("NSE:AAA", RunFixture.ThreeDays());
        var b = _fixture.Add("NSE:BBB", RunFixture.ThreeDays());
        Answer();
        var plan = new NullWalkPlan([Spec(0, a, RunFixture.D2)], 2, 5, 7);

        var progress = new Sync();

        var result = await Handler(_engine).HandleAsync(new RunNullWalkQuery(RunFixture.List([a, b], RunFixture.D1, RunFixture.D2), plan, progress), Ct);

        result.IsSuccess.ShouldBeTrue();
        var (id, _, to) = _fixture.Store.Reads.Single();
        id.ShouldBe(a);
        to.ShouldBe(new DateTimeOffset(2026, 3, 3, 23, 59, 0, TimeSpan.FromMinutes(330)), "the end of the last day of the window: the third day of the bars is never read");
        _fixture.Engine.Replays.ShouldBeEmpty("the replay is not run");
        var given = _engine.ReceivedCalls().Single().GetArguments()[1].ShouldBeOfType<InstrumentReplayInput>();
        (given.To, given.RawBars.Count).ShouldBe((RunFixture.D2, 2 * 375));
        result.Value.Trades.Single().NetR.ShouldBe([0.1, -0.2]);
        progress.Seen.ShouldBe([1], "one trade walked");
    }

    [Fact]
    public async Task The_draws_come_back_in_plan_order_and_an_instrument_that_could_not_be_read_says_so()
    {
        var a = _fixture.Add("NSE:AAA", RunFixture.ThreeDays());
        var missing = Guid.NewGuid();
        Answer();
        var plan = new NullWalkPlan([Spec(0, missing, RunFixture.D2), Spec(1, a, RunFixture.D2)], 2, 5, 7);

        var result = await Handler(_engine).HandleAsync(new RunNullWalkQuery(RunFixture.List([a, missing]), plan), Ct);

        result.Value.Trades.Select(t => t.TradeId).ShouldBe([0, 1]);
        result.Value.Trades[0].Unavailable.ShouldNotBeNull();
        result.Value.Trades[0].NetR.ShouldBeEmpty();
        result.Value.Trades[1].Unavailable.ShouldBeNull();
    }

    [Fact]
    public async Task The_token_and_the_progress_reach_the_engine()
    {
        var a = _fixture.Add("NSE:AAA", RunFixture.ThreeDays());
        Answer();
        using var cts = new CancellationTokenSource();

        await Handler(_engine).HandleAsync(new RunNullWalkQuery(RunFixture.List([a]), new NullWalkPlan([Spec(0, a, RunFixture.D2), Spec(1, a, RunFixture.D2)], 2, 5, 7), new Sync()), cts.Token);

        var args = _engine.ReceivedCalls().Single().GetArguments();
        args[3].ShouldNotBeNull();
        args[4].ShouldBe(cts.Token);
    }

    private sealed class Sync : IProgress<int>
    {
        public List<int> Seen { get; } = [];

        public void Report(int value) => Seen.Add(value);
    }
}
