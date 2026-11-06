#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>
/// CF-10 part 10d-1 (ADR 0220): the daily ML monitor. Every champion is evaluated once a day; one model that fails or throws does not stop the others and records nothing; with the switches OFF the run writes only proposals and evaluations; with <c>ml.auto_promote</c> ON the
/// challengers are offered to the promotion rule as <c>auto</c> and, with no source of evidence, none is promoted; the run is idempotent per model and day.
/// </summary>
public sealed class EvaluateMlModelsHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_the_switches_off_the_run_writes_proposals_and_evaluations_and_changes_no_model()
    {
        var f = new MlRollbackFixture { Inputs = MlRollbackFixture.FiringCalibration() };
        f.Add(7, ModelRole.Champion);
        f.Add(8, ModelRole.Challenger, 'b');

        var result = await f.Run.HandleAsync(new EvaluateMlModelsCommand(), Ct);

        result.Value.ShouldBe(new MlRunOutcome(1, 1, 0, 1, 0, 0, 0, 0));
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Champion);
        f.Registry.Models[8].Role.ShouldBe(ModelRole.Challenger);
        f.Registry.Designations.ShouldBeEmpty();
        result.Value.Summary.ShouldContain("1 rollback proposal(s)");
    }

    [Fact]
    public async Task With_the_rollback_setting_on_the_run_demotes_the_champion_and_a_second_run_the_same_day_does_nothing_more()
    {
        var f = new MlRollbackFixture { Inputs = MlRollbackFixture.FiringCalibration() };
        f.Add(7, ModelRole.Champion);
        f.Switches = MlRollbackFixture.On(rollback: true);

        var first = await f.Run.HandleAsync(new EvaluateMlModelsCommand(), Ct);
        f.CheckExists = true;
        var second = await f.Run.HandleAsync(new EvaluateMlModelsCommand(), Ct);

        first.Value.RolledBackAutomatically.ShouldBe(1);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
        second.Value.ShouldBe(new MlRunOutcome(0, 0, 0, 0, 0, 0, 0, 0), "the demoted model is a challenger now, so no champion is looked at");
        f.Registry.Designations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_champion_already_evaluated_today_is_counted_and_not_evaluated_again()
    {
        var f = new MlRollbackFixture { Inputs = MlRollbackFixture.FiringCalibration(), CheckExists = true };
        f.Add(7, ModelRole.Champion);

        var result = await f.Run.HandleAsync(new EvaluateMlModelsCommand(), Ct);

        result.Value.ShouldBe(new MlRunOutcome(1, 0, 1, 0, 0, 0, 0, 0));
        f.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_model_that_throws_or_fails_does_not_stop_the_others_and_records_nothing_for_itself()
    {
        var f = new MlRollbackFixture { Inputs = MlRollbackFixture.FiringCalibration() };
        f.Add(7, ModelRole.Champion);
        f.Add(8, ModelRole.Champion, 'b');
        f.Evidence.ReadAsync(Arg.Is<StoredModel>(m => m.Id == 8), Arg.Any<CancellationToken>()).Returns<ValueTask<RollbackInputs>>(_ => throw new InvalidOperationException("the store is down"));

        var result = await f.Run.HandleAsync(new EvaluateMlModelsCommand(), Ct);

        result.Value.Champions.ShouldBe(2);
        result.Value.Failed.ShouldBe(1);
        result.Value.Proposed.ShouldBe(1);
        f.Store.Rows.Select(r => r.Proposal.ModelId).ShouldBe([7L]);
        await f.Checks.Received(1).InsertAsync(Arg.Any<NewMlMonitorCheck>(), Arg.Any<DateOnly>(), Ct);
    }

    [Fact]
    public async Task A_proposal_that_cannot_be_applied_is_counted_as_a_failure_stays_open_and_does_not_stop_the_run()
    {
        var f = new MlRollbackFixture { Inputs = MlRollbackFixture.FiringCalibration() };
        f.Add(7, ModelRole.Champion);
        f.Switches = MlRollbackFixture.On(rollback: true);
        f.Store.BeforeDecide = _ => f.Switches = MlRollbackFixture.On(); // turned off between the check and the update

        var result = await f.Run.HandleAsync(new EvaluateMlModelsCommand(), Ct);

        result.Value.Failed.ShouldBe(1);
        result.Value.RolledBackAutomatically.ShouldBe(0);
        f.Store.Rows[0].State.ShouldBe("proposed");
    }

    [Fact]
    public async Task With_the_promotion_setting_on_each_challenger_is_offered_as_auto_and_with_no_source_of_evidence_none_is_promoted()
    {
        var f = new MlRollbackFixture();
        f.Add(8, ModelRole.Challenger, 'b');
        f.Add(9, ModelRole.Challenger, 'c');
        f.Switches = MlRollbackFixture.On(promote: true);

        var result = await f.Run.HandleAsync(new EvaluateMlModelsCommand(), Ct);

        result.Value.PromotedAutomatically.ShouldBe(0);
        result.Value.NotPromoted.ShouldBe(2);
        result.Value.Failed.ShouldBe(0, "a refusal is the rule's answer");
        f.Registry.Designations.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_promotion_setting_off_the_challengers_are_not_even_offered()
    {
        var f = new MlRollbackFixture { Figures = new ChallengerFigures(30, 4, 0.99, CounterfactualOutcome.RetrainingHelps) };
        f.Add(8, ModelRole.Challenger, 'b');

        var result = await f.Run.HandleAsync(new EvaluateMlModelsCommand(), Ct);

        result.Value.NotPromoted.ShouldBe(0);
        f.Registry.Models[8].Role.ShouldBe(ModelRole.Challenger);
        await f.Challenger.DidNotReceiveWithAnyArgs().ReadAsync(default!, default, Ct);
    }

    [Fact]
    public async Task A_challenger_that_meets_the_rule_and_the_predicate_is_promoted_automatically_when_the_source_exists_and_a_challenger_that_throws_does_not_stop_the_run()
    {
        var f = new MlRollbackFixture { Figures = new ChallengerFigures(30, 4, 0.99, CounterfactualOutcome.RetrainingHelps) };
        var good = f.Add(8, ModelRole.Challenger, 'b');
        f.Add(9, ModelRole.Challenger, 'c');
        await f.MakePredicateHoldAsync(good);
        f.Switches = MlRollbackFixture.On(promote: true);

        // model 9 owns no look at the hold-out (the family's look is model 8's), so the predicate fails for it; model 8 qualifies
        var result = await f.Run.HandleAsync(new EvaluateMlModelsCommand(), Ct);

        result.Value.PromotedAutomatically.ShouldBe(1);
        result.Value.NotPromoted.ShouldBe(1);
        f.Registry.Models[8].Role.ShouldBe(ModelRole.Champion);
        f.Registry.Models[9].Role.ShouldBe(ModelRole.Challenger);
        f.Log.Rows.Single(r => r.Entry.ToState == "champion").Entry.Actor.ShouldBe("auto");
    }

    [Fact]
    public async Task A_command_handler_that_throws_in_the_promotion_loop_is_counted_and_the_run_goes_on()
    {
        var f = new MlRollbackFixture();
        f.Add(8, ModelRole.Challenger, 'b');
        f.Add(9, ModelRole.Challenger, 'c');
        f.Switches = MlRollbackFixture.On(promote: true);
        var promote = Substitute.For<ICommandHandler<PromoteChallengerCommand, PromotionOutcome>>();
        promote.HandleAsync(Arg.Is<PromoteChallengerCommand>(c => c.ModelId == 9), Ct).Returns<ValueTask<Result<PromotionOutcome>>>(_ => throw new InvalidOperationException("boom"));
        promote.HandleAsync(Arg.Is<PromoteChallengerCommand>(c => c.ModelId == 8), Ct).Returns(Result.Failure<PromotionOutcome>(MlSafetyErrors.PromotionRefused(8, [])));
        var run = new EvaluateMlModelsHandler(f.Registry, f.Settings, f.Propose, promote, NullLogger<EvaluateMlModelsHandler>.Instance);

        var result = await run.HandleAsync(new EvaluateMlModelsCommand(), Ct);

        result.Value.Failed.ShouldBe(1);
        result.Value.NotPromoted.ShouldBe(1);
    }
}
