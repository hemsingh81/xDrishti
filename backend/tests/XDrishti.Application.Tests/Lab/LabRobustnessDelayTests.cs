using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Lab;

/// <summary>CF-05 (ADR 0206): the delay pass of the robustness stage: the same request with an entry delay, the delayed trades matched to the base signals by instrument, side and decision instant, a missed signal left as null.</summary>
public sealed class LabRobustnessDelayTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 6, 0, 0, TimeSpan.Zero);

    private readonly IQueryHandler<RunSimulationQuery, Result<SimulationRun>> _simulation = Substitute.For<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();

    private static LabRun Run() => new(
        Guid.NewGuid(), new string('1', 64), "LAB_TEST", 2, new string('a', 64),
        new LabUniverse("symbols", null, "symbols as listed", 2, [new LabInstrument(FabricatedRun.IdA, "AAA"), new LabInstrument(FabricatedRun.IdB, "BBB")], false, null),
        new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "robust", new string('c', 64), "sim-5", LabRunState.Running, "robustness", 0, 0, [], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);

    private static SimulationRequest Template() => new("LAB_TEST", new Dictionary<string, double>(), "15m", new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), [FabricatedRun.IdA, FabricatedRun.IdB], null, CostChoice.Gross);

    private static FullTestOutcome Full() => new(
        [], new LabVerdictView("FAIL", 1, 1, new EvidenceView(0, null, null, 0, 0, null, 0, "walk_forward"), [], [], [], [], []), 1, 1, 0, []);

    private static async Task<RobustnessOutcome> RunAsync(LabRobustness service) =>
        (await service.RunAsync(Run(), Now, service.Plan(new StrategyDescription("LAB_TEST", "intraday", new string('a', 64), "{}", [], [], [])), Template(), Full(), null, Ct)).Value;

    private LabRobustness Service(CostStressOptions? options, Func<SimulationRun, SimulationRun> delayed)
    {
        var baseRun = FabricatedRun.Standard();
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var q = ci.Arg<RunSimulationQuery>();
            return Result.Success(q.Request.Fills is { EntryDelayMinutes: > 0 } ? delayed(baseRun) : baseRun);
        });
        return new LabRobustness(_simulation, LabSettings.Default, null, null, options);
    }

    [Fact]
    public async Task A_signal_the_delayed_run_no_longer_fills_is_null_and_the_others_are_matched_by_instrument_side_and_decision_instant()
    {
        // the base run has a +1.95 Buy on AAA and a -1.1 Sell on M&M; the delayed run keeps only the Sell
        var outcome = await RunAsync(Service(new CostStressOptions(), b => b with { Trades = [b.Trades[1]] }));

        var delay = outcome.Delay.ShouldNotBeNull();
        delay.Minutes.ShouldBe(1);
        delay.Error.ShouldBeNull();
        delay.Signals.ShouldBe([(1.95, (double?)null), (-1.1, -1.1)]);
    }

    [Fact]
    public async Task The_base_pass_is_the_default_exit_run_and_the_delayed_one_follows_it_with_the_same_request_but_the_delay()
    {
        await RunAsync(Service(new CostStressOptions(), b => b));

        var requests = _simulation.ReceivedCalls().Select(c => ((RunSimulationQuery)c.GetArguments()[0]!).Request).ToList();
        var delayed = requests.Last();
        var baseRequest = requests.First();
        delayed.Fills!.EntryDelayMinutes.ShouldBe(1);
        baseRequest.Fills.ShouldBeNull("the template of the test gave none; the default is used and the base is not delayed");
        (delayed with { Fills = null }).ShouldBe(baseRequest with { Fills = null, Parameters = delayed.Parameters }, "nothing else differs");
    }

    [Fact]
    public async Task A_failed_delayed_run_is_kept_as_the_reason_and_not_as_a_number()
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(ci =>
            ci.Arg<RunSimulationQuery>().Request.Fills is { EntryDelayMinutes: > 0 } ? Result.Failure<SimulationRun>(Error.Validation("x", "no bars")) : Result.Success(FabricatedRun.Standard()));

        var outcome = await RunAsync(new LabRobustness(_simulation, LabSettings.Default, null, null, new CostStressOptions()));

        outcome.Delay.ShouldNotBeNull().Error.ShouldBe("no bars");
        outcome.Delay!.Signals.ShouldBeEmpty();
    }

    [Fact]
    public async Task Off_or_no_options_takes_no_extra_pass_and_returns_no_signals()
    {
        var off = await RunAsync(Service(new CostStressOptions(ConfidenceMode.Off), b => b));
        var callsOff = _simulation.ReceivedCalls().Count();
        _simulation.ClearReceivedCalls();
        var none = await RunAsync(Service(null, b => b));

        off.Delay.ShouldBeNull();
        none.Delay.ShouldBeNull();
        callsOff.ShouldBe(_simulation.ReceivedCalls().Count());
        _simulation.ReceivedCalls().Select(c => ((RunSimulationQuery)c.GetArguments()[0]!).Request.Fills?.EntryDelayMinutes ?? 0).ShouldAllBe(m => m == 0);
    }

    [Fact]
    public async Task A_base_that_filled_no_signal_gives_an_empty_list_for_the_test_to_call_not_evaluable()
    {
        var outcome = await RunAsync(Service(new CostStressOptions(), b => b with { Trades = [] }));

        // the base is the Standard run (it has trades); an empty base is arranged here by an empty answer for both
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(FabricatedRun.Standard() with { Trades = [] }));
        var empty = await RunAsync(new LabRobustness(_simulation, LabSettings.Default, null, null, new CostStressOptions()));

        outcome.Delay.ShouldNotBeNull();
        empty.Delay.ShouldNotBeNull().Signals.ShouldBeEmpty();
        empty.Delay!.Error.ShouldBeNull();
    }
}
