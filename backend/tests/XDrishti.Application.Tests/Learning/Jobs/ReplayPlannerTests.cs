#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using XDrishti.Application.Learning.Jobs;
using XDrishti.Application.Research;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Learning.Jobs;

/// <summary>Spec P8-18 criteria 5 and 6 and assumption 5, behind the engine port: what a stored record must carry for a replay, the proof by compiling that the exits passed give back the baseline's definition, and a candidate request that differs from the baseline's in exactly one field. Fails closed.</summary>
public sealed class ReplayPlannerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static ReplayPlanner Planner(Func<ExitChoice?, bool> reproduces, string? stored = null, bool builtIn = true) =>
        new(JobFixtures.Engine(reproduces, builtIn), JobFixtures.Source(stored));

    private static ValueTask<XDrishti.Domain.Common.Result<ReplayPlan>> Plan(ReplayPlanner planner, string? record = null, int schema = 6, string? fills = null) =>
        planner.PlanAsync(JobFixtures.Header(schema: schema, fills: fills), record ?? JobFixtures.Record(), Ct);

    [Fact]
    public async Task A_built_in_run_is_replayed_by_id_its_own_exits_reproduce_it_and_the_plain_exit_is_read_through_the_effective_parameter()
    {
        var plan = (await Plan(Planner(exits => exits is null))).Value;

        plan.Stored.ShouldBeFalse();
        plan.BaselineExits.ShouldBeNull();
        plan.Baseline.Exit.ShouldNotBeNull().Id.ShouldBe("FIXED_RR{rr=2}"); // "rr" names the parameter, whose value in the record is 2
        plan.Offers.Select(o => o.Name).ShouldBe(["FIXED_RR.rr", "slippage.ticks", "slippage.bar_range_frac", "slippage.stop_exit_extra_ticks"]);
        plan.Baseline.InstrumentIds.ShouldBe([JobFixtures.InstrumentA, JobFixtures.InstrumentB]);
        plan.Baseline.Costs.ShouldBe(CostChoice.Gross);
    }

    [Fact]
    public async Task A_stored_strategy_is_replayed_by_its_definition_hash_and_the_costs_of_a_sheet_run_are_read_back()
    {
        var plan = (await Plan(Planner(exits => exits is null, stored: "source text"), JobFixtures.Record(costs: JobFixtures.SheetCosts))).Value;

        plan.Stored.ShouldBeTrue();
        var request = plan.BaselineRequest();
        request.StrategyHash.ShouldBe(JobFixtures.DefinitionSha);
        request.Costs.ShouldBe(CostChoice.Sheet("default", 1_000_000m, "standard", 0.10m));
        request.InstrumentIds.ShouldBe([JobFixtures.InstrumentA, JobFixtures.InstrumentB]);
        request.BasketId.ShouldBeNull();
    }

    [Fact]
    public async Task A_run_made_with_an_exit_override_of_a_stored_strategy_has_a_hash_no_version_has_and_is_refused_not_guessed()
    {
        var result = await Plan(Planner(exits => exits is null, stored: null, builtIn: false));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("experiments.baseline_not_rebuildable");
        result.Error.Message.ShouldContain("neither a stored version");
    }

    [Fact]
    public async Task A_built_in_run_made_with_an_exit_override_is_reproduced_by_the_exits_read_from_its_definition()
    {
        var plan = (await Plan(Planner(exits => exits is { Candidates.Count: 1 }))).Value;

        plan.BaselineExits.ShouldNotBeNull().Candidates.Single().Id.ShouldBe("FIXED_RR{rr=2}");
    }

    [Fact]
    public async Task When_nothing_compiled_today_gives_the_stored_definition_the_baseline_is_refused()
    {
        var result = await Plan(Planner(_ => false));

        result.Error.Code.ShouldBe("experiments.baseline_not_rebuildable");
        result.Error.Message.ShouldContain("does not give the definition");
    }

    [Fact]
    public async Task A_record_of_another_schema_fills_that_do_not_rebuild_a_basket_without_an_id_and_unreadable_text_are_each_refused_with_a_reason()
    {
        var planner = Planner(exits => exits is null);

        (await Plan(planner, schema: 5)).Error.Message.ShouldContain("schema 5");
        (await Plan(planner, fills: "{\"entry_limit_percent\":0.1}")).Error.Message.ShouldContain("fill rules");
        (await Plan(planner, JobFixtures.Record(fills: "{\"entry_limit_percent\":0.2,\"slippage\":{\"ticks\":1,\"bar_range_frac\":0.05,\"stop_exit_extra_ticks\":2,\"gap_fill\":\"at_open\"}}"))).Error.Message.ShouldContain("fill rules"); // header and record text differ
        (await Plan(planner, JobFixtures.Record(universeKind: "basket"))).Error.Message.ShouldContain("universe");
        (await Plan(planner, JobFixtures.Record(timeframe: "7m"))).Error.Message.ShouldContain("strategy part");
        (await Plan(planner, "not json")).Error.Message.ShouldContain("cannot be read");
    }

    [Fact]
    public async Task A_basket_run_is_replayed_by_its_basket()
    {
        var basket = Guid.NewGuid();
        var plan = (await Plan(Planner(exits => exits is null), JobFixtures.Record(universeKind: "basket", basket: basket.ToString("D")))).Value;

        plan.BaselineRequest().BasketId.ShouldBe(basket);
        plan.BaselineRequest().InstrumentIds.ShouldBeNull();
    }

    [Fact]
    public async Task An_exit_the_reader_cannot_read_as_one_plain_exit_leaves_slippage_as_the_only_knob()
    {
        var two = JobFixtures.Definition("""[{"policy":"FIXED_RR","parameters":[{"name":"rr","values":[2]}]},{"policy":"FIXED_RR","parameters":[{"name":"rr","values":[3]}]}]""");
        var unknownParameter = JobFixtures.Definition("""[{"policy":"FIXED_RR","parameters":[{"name":"rr","values":["missing"]}]}]""");
        var ladder = JobFixtures.Definition("""[{"policy":"FIXED_RR","parameters":[{"name":"rr","values":[2,3]}]}]""");
        var planner = Planner(exits => exits is null);

        foreach (var definition in new[] { two, unknownParameter, ladder })
        {
            (await Plan(planner, JobFixtures.Record(definition))).Value.Offers.ShouldAllBe(o => o.Kind == ExperimentKnobKind.Slippage);
        }
    }

    [Fact]
    public async Task A_time_stop_overlay_is_carried_into_the_candidate_exit_and_another_overlay_is_not_guessed()
    {
        var withStop = JobFixtures.Definition(overlays: """[{"kind":"time_stop","parameters":[{"name":"intraday_bars","values":[20]},{"name":"min_progress_rr","values":[0.5]}]}]""");
        var other = JobFixtures.Definition(overlays: """[{"kind":"event_exit","parameters":[{"name":"days_before_results","values":[2]}]}]""");
        var planner = Planner(exits => exits is null);

        var plan = (await Plan(planner, JobFixtures.Record(withStop))).Value;
        var knob = ReplayKnobCatalogue.Check("exit", "FIXED_RR.rr", 3, plan.Baseline.Exit, plan.Baseline.Fills.Slippage).Value;

        plan.Baseline.TimeStop.ShouldBe(new TimeStopChoice(0.5, 20, 0));
        plan.CandidateRequest(knob).Exits!.TimeStop.ShouldBe(new TimeStopChoice(0.5, 20, 0));
        (await Plan(planner, JobFixtures.Record(other))).Value.Offers.ShouldAllBe(o => o.Kind == ExperimentKnobKind.Slippage);
    }

    [Fact]
    public async Task The_candidate_request_differs_from_the_baseline_request_in_exactly_one_field_for_an_exit_knob_and_for_a_slippage_knob()
    {
        var plan = (await Plan(Planner(exits => exits is null, stored: "text"))).Value;
        var baseline = plan.BaselineRequest();
        var exit = plan.CandidateRequest(ReplayKnobCatalogue.Check("exit", "FIXED_RR.rr", 3, plan.Baseline.Exit, plan.Baseline.Fills.Slippage).Value);
        var slippage = plan.CandidateRequest(ReplayKnobCatalogue.Check("slippage", "slippage.ticks", 2, plan.Baseline.Exit, plan.Baseline.Fills.Slippage).Value);

        JobFixtures.Describe(exit).ShouldBe(JobFixtures.Describe(baseline with { Exits = new ExitChoice([new ExitCandidate("FIXED_RR", [new ExitParameterValue("rr", [3])])], null) }), "an exit knob changes the exits and nothing else");
        JobFixtures.Describe(slippage).ShouldBe(JobFixtures.Describe(baseline with { Fills = new FillRulesInput(0.1, 2, 0.05, 2) }), "a slippage knob changes the fills and nothing else");
        slippage.Exits.ShouldBeNull();
    }
}
