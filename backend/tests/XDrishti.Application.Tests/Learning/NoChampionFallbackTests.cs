#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Learning;
using XDrishti.Application.Planning;
using XDrishti.Application.Tests.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Tests.Learning;

/// <summary>
/// CF-10 part 10d-1, AC13 (ADR 0220): after a rollback with no eligible previous champion there is no champion; the gate hands out no score for any model, and the plan the real builder makes shows the cell rate only: no model figure on any item (<c>AdvisoryPWin</c> null, no advisory
/// note). Interpretation: the plan builder asks no gate in this version (the consumers of the gate are pinned empty and a model's figure is attached to no candidate), so "a plan with a fake gate" is evidenced as the real gate's answer after the real rollback beside the real builder's
/// output over the same state.
/// </summary>
public sealed class NoChampionFallbackTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Session = new(2026, 10, 23);
    private static readonly DateOnly ForDate = new(2026, 10, 26);
    private static readonly PlanBuildRequest Request = new(Session, ForDate, NightlyRules.ReviewCutoff(ForDate), new DateTimeOffset(2026, 10, 23, 13, 0, 0, TimeSpan.Zero));

    private static TicketCandidate Candidate(string symbol)
    {
        var stats = new CellStatsRow(
            new CellId("S1", "1D", "BUY", "swing", "fixed"), CellWindow.All, new DateOnly(2026, 10, 22), true, 40, 20, 40, 0.5, 0.5, 0.55, 0.50, 0.6, 2.0, -1.0, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);
        var draft = new TicketDraft(
            TicketVerdict.Conditional, symbol, "BUY", "S1", new string('a', 64), Session, SetupTruth.True, SetupTruth.Unknown, SetupTruth.False, SetupTruth.Unknown, [],
            "reason", "watch the breakout", "stop below the low", "target 2 x risk", "valid for the day", 98, 100, 2.0, 50_000_000, ["setup"]);
        return new TicketCandidate(draft, stats.Cell, stats, CellState.Active);
    }

    private static async Task<PlanItemView> PlanAsync()
    {
        var source = Substitute.For<ITicketSource>();
        source.ReadAsync(Session, ForDate, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new TicketSourceResult([Candidate("AAA")], 1, 1, []))));
        var context = Substitute.For<IPlanContextSource>();
        context.ReadAsync(Arg.Any<PlanContextRequest>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(WiredPlanFixture.Context())));
        var loose = new SelectionRules(OptionalFilters: [FilterCodes.Cost, FilterCodes.Event, FilterCodes.FnoBan, FilterCodes.Surveillance]);
        var plan = await new SelectionPlanBuilder(source, context, NullLogger<SelectionPlanBuilder>.Instance, loose).BuildAsync(Request, Ct);
        plan.IsSuccess.ShouldBeTrue(plan.IsFailure ? plan.Error.Message : string.Empty);
        return new PlanItemView(plan.Value.Items.Single());
    }

    /// <summary>The evidence of the one item, read through the same record the plan stores.</summary>
    private sealed class PlanItemView(NewPlanItem item)
    {
        public PlanEvidence Evidence => item.Evidence;
    }

    [Fact]
    public async Task After_a_rollback_with_no_eligible_previous_champion_no_model_has_a_score_and_the_plan_shows_the_cell_rate_only()
    {
        var f = new MlRollbackFixture { Inputs = MlRollbackFixture.FiringCalibration() };
        f.Add(6, ModelRole.Champion, 'a');
        f.Add(7, ModelRole.Challenger, 'b');
        (await f.Designate.HandleAsync(new DesignateModelCommand(7, ModelRole.Champion, "better", "owner:me@x.test"), Ct)).IsSuccess.ShouldBeTrue();
        f.Switches = MlRollbackFixture.On(influence: true, rollback: true);
        f.Influence.IsOnAsync(Ct).Returns(true); // the owner's influence switch is ON and the evidence is good: only the champion could pass
        await f.MakePredicateHoldAsync(f.Registry.Models[7]);

        var rollback = await f.ProposeAsync(7);
        var scorer = Substitute.For<IQueryHandler<ScoreWithModelQuery, Result<ModelScoreView>>>();
        scorer.HandleAsync(Arg.Any<ScoreWithModelQuery>(), Ct).Returns(Result.Success(new ModelScoreView(7, 0.8, [])));
        var gate = new MlInfluenceGate(f.Evaluator, Substitute.For<IMlInfluenceChecks>(), scorer, f.Time);

        rollback.AppliedAutomatically.ShouldBe(1);
        f.Registry.Models.Values.Count(m => m.Role == ModelRole.Champion).ShouldBe(0);
        foreach (var id in new long[] { 6, 7 })
        {
            var answer = await gate.ScoreAsync(id, [0.1, 0.2, 0.3], Ct);
            answer.PWin.ShouldBeNull($"model {id}");
            answer.Answer.MayInfluence.ShouldBeFalse();
            answer.Answer.Reasons.Select(r => r.Code).ShouldContain("not_champion");
        }

        await scorer.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);

        var item = await PlanAsync();
        item.Evidence.AdvisoryNote.ShouldBeNull();
        item.Evidence.PWinBasis.ShouldBe("shrunk posterior win rate of the cell");
        item.Evidence.PWin.ShouldNotBeNull();
        SelectionPlanBuilder.TryReadAdvisoryNote(item.Evidence.AdvisoryNote, out _).ShouldBeFalse();
    }
}
