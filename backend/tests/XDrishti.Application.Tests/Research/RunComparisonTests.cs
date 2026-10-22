using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>
/// P4-05 at the use case with the engine faked: the exits of the request reach the engine; one candidate is a plain run, two or more are a comparison with one run record each, the report and the two keys; a comparison asked as a plain run (and the
/// reverse) is refused by name; the engine is asked to replay a comparison once for each instrument and a plain run as before.
/// </summary>
public sealed class RunComparisonTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ExitChoice Choice(params string[] specs)
    {
        var errors = new List<string>();
        var choice = ExitFlags.Parse(specs, null, errors);
        choice.ShouldNotBeNull(string.Join("; ", errors));
        return choice;
    }

    private static SimulationRequest Request(RunFixture fixture, ExitChoice? exits) =>
        RunFixture.List([fixture.Add("ABB", RunFixture.ThreeDays())]) with { Exits = exits };

    [Fact]
    public async Task The_exits_of_the_request_reach_the_engine_and_a_plain_request_sends_none()
    {
        var fixture = new RunFixture();
        var exits = Choice("PARTIAL_BE:book_pct=50,t1_rr=1,t2_rr=2");

        (await fixture.RunAsync(Request(fixture, exits), Ct)).IsSuccess.ShouldBeTrue();
        (await fixture.RunAsync(Request(fixture, null), Ct)).IsSuccess.ShouldBeTrue();

        fixture.Engine.PreparedExits.ShouldBe([exits, null]);
    }

    [Fact]
    public async Task One_candidate_is_a_plain_run_whose_record_is_the_record_of_that_candidate_and_the_engine_is_asked_to_replay_it_as_before()
    {
        var fixture = new RunFixture();

        var run = await fixture.RunAsync(Request(fixture, Choice("FIXED_RR:rr=3")), Ct);

        run.IsSuccess.ShouldBeTrue(run.IsFailure ? run.Error.Message : string.Empty);
        fixture.Engine.Replays.Count.ShouldBe(1);
        run.Value.Strategy.DefinitionSha256.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Two_candidates_are_a_comparison_with_a_run_record_each_one_report_and_the_keys()
    {
        var fixture = new RunFixture();
        var request = Request(fixture, Choice("FIXED_RR:rr=2|3"));

        var comparison = await fixture.Handler.HandleAsync(new RunComparisonQuery(request), Ct);

        comparison.IsSuccess.ShouldBeTrue(comparison.IsFailure ? comparison.Error.Message : string.Empty);
        comparison.Value.Candidates.Select(c => c.Id).ShouldBe(["FIXED_RR{rr=2}", "FIXED_RR{rr=3}"]);
        comparison.Value.Candidates.Select(c => c.Run.RunKey).Distinct().Count().ShouldBe(2, "the run key of a candidate names its exit");
        comparison.Value.Candidates.ShouldAllBe(c => c.Run.Strategy.Id == "ORB");
        comparison.Value.Report.Rows.Count.ShouldBe(2);
        comparison.Value.Report.GateApplied.ShouldBeTrue("an intraday strategy has nothing to gate");
        comparison.Value.EntriesKey.Length.ShouldBe(64);
        comparison.Value.ComparisonKey.Length.ShouldBe(64);
        fixture.Engine.Replays.Count.ShouldBe(1, "one replay of the comparison for the one instrument, not one for each candidate");
    }

    [Fact]
    public async Task A_comparison_asked_as_a_plain_run_and_a_plain_run_asked_as_a_comparison_are_refused_by_name()
    {
        var fixture = new RunFixture();

        var plain = await fixture.RunAsync(Request(fixture, Choice("FIXED_RR:rr=2|3")), Ct);
        var one = await fixture.Handler.HandleAsync(new RunComparisonQuery(Request(fixture, Choice("FIXED_RR:rr=2"))), Ct);
        var none = await fixture.Handler.HandleAsync(new RunComparisonQuery(Request(fixture, null)), Ct);

        plain.IsFailure.ShouldBeTrue();
        plain.Error.Fields!.ShouldContain(f => f.Code == "simulation.exits_are_a_comparison");
        one.Error.Fields!.ShouldContain(f => f.Code == "simulation.comparison_needs_candidates");
        none.Error.Fields!.ShouldContain(f => f.Code == "simulation.comparison_needs_candidates");
    }

    [Fact]
    public async Task A_strategy_the_engine_refuses_is_refused_before_any_port_is_read_for_a_comparison_too()
    {
        var fixture = new RunFixture();
        var request = Request(fixture, Choice("FIXED_RR:rr=2|3"));
        fixture.Engine.PrepareResult = Result.Failure<XDrishti.Application.Abstractions.Research.IPreparedStrategy>(SimulationErrors.StrategyInvalid([new FieldError("candidates[1].buy.exits", "refused", Code: "simulation.unsupported_exit")]));
        fixture.Store.Reads.Clear();

        var comparison = await fixture.Handler.HandleAsync(new RunComparisonQuery(request), Ct);

        comparison.IsFailure.ShouldBeTrue();
        comparison.Error.Fields!.ShouldContain(f => f.Field == "candidates[1].buy.exits");
        fixture.Store.Reads.ShouldBeEmpty();
        fixture.Engine.Replays.ShouldBeEmpty();
    }
}
