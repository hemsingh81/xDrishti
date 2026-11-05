using NSubstitute;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>
/// CF-07 part B (ADR 0209): the placebo walk query is answered by the run handler with the same preparation and reading as a run, over the window of the request only; the instruments that carry a signal (the roll order) are read and handed to the placebo engine instead of the replay;
/// the outcomes of every instrument are collected; no run record is built and nothing is stored.
/// </summary>
public sealed class RunPlaceboWalkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly RunFixture _fixture = new();

    private readonly IPlaceboEngine _engine = Substitute.For<IPlaceboEngine>();

    private RunSimulationHandler Handler() =>
        new(_fixture.Store, _fixture.BarQueries, _fixture.Baskets, _fixture.Factors, _fixture.Actions, _fixture.Calendar, _fixture.Engine, _fixture.Strategies, _fixture.CostSheets, _fixture.ContextDays, _fixture.EventSnapshots, _fixture.FilingTimes, _fixture.SurpriseReader, _fixture.Time, _fixture.Log,
            Substitute.For<IRandomEntryEngine>(), _engine);

    private static PlaceboSignal Signal(int id, Guid instrument) => new(id, instrument, RunFixture.D2, Side.Buy, 0.01, 2, CostSegment.IntradayEquity, 1);

    private void Answer() =>
        _engine.WalkPlacebos(Arg.Any<IPreparedStrategy>(), Arg.Any<InstrumentReplayInput>(), Arg.Any<PlaceboWalkPlan>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var input = ci.Arg<InstrumentReplayInput>();
                ci.Arg<IProgress<int>?>()?.Report(1);
                return new PlaceboWalkResult([new PlaceboOutcome(0, 0, input.InstrumentId == _fixtureFirst ? 0.1 : -0.2, null)]);
            });

    private Guid _fixtureFirst;

    [Fact]
    public async Task Only_the_instruments_of_the_roll_order_are_read_over_the_window_of_the_request_their_outcomes_are_collected_and_nothing_is_replayed()
    {
        var a = _fixture.Add("NSE:AAA", RunFixture.ThreeDays());
        var b = _fixture.Add("NSE:BBB", RunFixture.ThreeDays());
        var c = _fixture.Add("NSE:CCC", RunFixture.ThreeDays());
        _fixtureFirst = a;
        Answer();
        var plan = new PlaceboWalkPlan([Signal(0, a)], PlaceboShapes.RollOrder([a, b]));
        var progress = new Sync();

        var result = await Handler().HandleAsync(new RunPlaceboWalkQuery(RunFixture.List([a, b, c], RunFixture.D1, RunFixture.D2), plan, progress), Ct);

        result.IsSuccess.ShouldBeTrue();
        _fixture.Store.Reads.Select(r => r.Item1).Order().ShouldBe(new[] { a, b }.Order(), "the instrument without a signal is not read");
        _fixture.Store.Reads.ShouldAllBe(r => r.Item3 == new DateTimeOffset(2026, 3, 3, 23, 59, 0, TimeSpan.FromMinutes(330)), "the end of the last day of the window: the third day of the bars is never read");
        _fixture.Engine.Replays.ShouldBeEmpty("the replay is not run");
        _engine.ReceivedCalls().Select(x => x.GetArguments()[1]).OfType<InstrumentReplayInput>().ShouldAllBe(i => i.To == RunFixture.D2);
        result.Value.Outcomes.Count.ShouldBe(2);
        progress.Seen.ShouldBe([1, 2], "one pair per instrument here, as a running total");
    }

    [Fact]
    public async Task The_token_and_the_progress_reach_the_engine_and_an_unreadable_instrument_adds_nothing()
    {
        var a = _fixture.Add("NSE:AAA", RunFixture.ThreeDays());
        var missing = Guid.NewGuid();
        _fixtureFirst = a;
        Answer();
        using var cts = new CancellationTokenSource();

        var result = await Handler().HandleAsync(new RunPlaceboWalkQuery(RunFixture.List([a, missing]), new PlaceboWalkPlan([Signal(0, a)], PlaceboShapes.RollOrder([a, missing])), new Sync()), cts.Token);

        var args = _engine.ReceivedCalls().Single().GetArguments();
        args[3].ShouldNotBeNull();
        args[4].ShouldBe(cts.Token);
        result.Value.Outcomes.Count.ShouldBe(1);
    }

    private sealed class Sync : IProgress<int>
    {
        public List<int> Seen { get; } = [];

        public void Report(int value) => Seen.Add(value);
    }
}
