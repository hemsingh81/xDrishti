using System.Reflection;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>AC6: the candidate knows nothing of exits, so the same entries can be walked under several exit rules (how P4 compares exit policies).</summary>
public sealed class SeamTests
{
    [Fact]
    public void An_entry_candidate_has_no_member_about_exits()
    {
        var names = typeof(EntryCandidate).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();

        names.ShouldNotContain(n => n.Contains("Exit", StringComparison.Ordinal) || n.Contains("Target", StringComparison.Ordinal) || n.Contains("Reward", StringComparison.Ordinal) || n.Contains("Square", StringComparison.Ordinal));
        names.ShouldContain("StopLevel", "the stop is an entry fact: it sets the size of R");
    }

    [Fact]
    public void The_walker_takes_the_candidate_the_bars_and_the_rules_and_nothing_else()
    {
        var walk = typeof(TradeWalker).GetMethod(nameof(TradeWalker.Walk))!;

        walk.GetParameters().Select(p => p.ParameterType).ShouldBe([typeof(EntryCandidate), typeof(IReadOnlyList<XDrishti.Domain.Market.Bars.Bar>), typeof(WalkRules)]);
    }

    [Fact]
    public void The_candidate_of_F1_walked_under_a_reward_risk_of_one_and_a_half_targets_104_25()
    {
        var outcome = WalkF1(1.5);

        var trade = outcome.Trade.ShouldNotBeNull();
        trade.TargetPrice.ShouldBe(104.25);
        trade.ExitReason.ShouldBe(ExitReason.Target);
        trade.ExitEpochSeconds.ShouldBe(DayBuilder.Epoch(DayBuilder.Monday, "10:30"));
        trade.R.ShouldBe(1.5);
        trade.MfeR.ShouldBe(1.5);
        outcome.BarsExamined.ShouldBe(46);
    }

    [Fact]
    public void The_candidate_of_F1_walked_under_a_reward_risk_of_two_is_F1()
    {
        var f1 = Cases.F("F1");

        OutcomeFormatter.Render(Result(WalkF1(2.0))).ShouldBe(OutcomeFormatter.Render(WalkAssert.Walk(f1, f1.Items[0])));
    }

    [Fact]
    public void The_candidate_of_F1_walked_under_a_reward_risk_of_three_never_reaches_108_and_runs_to_the_square_off()
    {
        var outcome = WalkF1(3.0);

        var trade = outcome.Trade.ShouldNotBeNull();
        trade.TargetPrice.ShouldBe(108.00);
        trade.ExitReason.ShouldBe(ExitReason.SquareOff);
        trade.ExitPrice.ShouldBe(105.00);
        trade.ExitEpochSeconds.ShouldBe(DayBuilder.Epoch(DayBuilder.Monday, "15:15"));
        trade.R.ShouldBe(1.8);
        trade.MaeR.ShouldBe(0.4);
        trade.MfeR.ShouldBe(2.0);
        outcome.BarsExamined.ShouldBe(331);
    }

    [Fact]
    public void Without_a_reward_risk_the_result_is_that_of_three_with_no_target_price()
    {
        var three = WalkF1(3.0).Trade.ShouldNotBeNull();

        var none = WalkF1(null).Trade.ShouldNotBeNull();

        none.TargetPrice.ShouldBeNull();
        (none with { TargetPrice = three.TargetPrice }).ShouldBe(three);
    }

    private static WalkOutcome WalkF1(double? rewardRisk)
    {
        var f1 = Cases.F("F1");
        return TradeWalker.Walk(f1.Items[0].Candidate, f1.Bars, f1.Rules with { RewardRisk = rewardRisk }).Value;
    }

    private static XDrishti.Domain.Common.Result<WalkOutcome> Result(WalkOutcome outcome) => XDrishti.Domain.Common.Result.Success(outcome);
}
