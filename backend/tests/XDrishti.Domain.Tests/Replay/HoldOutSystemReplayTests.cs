using XDrishti.Domain.Lab;
using XDrishti.Domain.Replay.HoldOut;

namespace XDrishti.Domain.Tests.Replay;

/// <summary>SR-03: the pass rule (AC 6) and the target block (AC 1).</summary>
public sealed class HoldOutSystemReplayTests
{
    private static readonly SystemReplayPassRule Rule = SystemReplayPassRule.Default;

    private static string Verdict(int? trades, double? cagr, decimal? drawdown, string? answer, bool net = true) =>
        SystemReplayVerdict.Evaluate(Rule, new SystemReplayFigures(trades, net, cagr, drawdown, answer)).Status;

    [Fact]
    public void Enough_trades_a_positive_CAGR_a_drawdown_within_the_limit_and_no_baseline_win_is_a_pass()
    {
        Verdict(41, 6.3, 7.4m, "no_measurable_difference").ShouldBe(HoldOutStatus.Pass);
        Verdict(41, 6.3, 15m, "selection_better").ShouldBe(HoldOutStatus.Pass);
    }

    [Fact]
    public void A_drawdown_over_the_limit_a_non_positive_CAGR_or_a_baseline_win_is_a_fail()
    {
        Verdict(41, 6.3, 16.0m, "no_measurable_difference").ShouldBe(HoldOutStatus.Fail);
        Verdict(41, 0, 5m, "no_measurable_difference").ShouldBe(HoldOutStatus.Fail);
        Verdict(41, -2, 5m, "selection_better").ShouldBe(HoldOutStatus.Fail);
        Verdict(41, 6.3, 7.4m, "baseline_better").ShouldBe(HoldOutStatus.Fail);
    }

    [Fact]
    public void Fewer_trades_than_the_rule_needs_is_inconclusive_even_when_everything_else_fails()
    {
        Verdict(22, 6.3, 7.4m, "no_measurable_difference").ShouldBe(HoldOutStatus.Inconclusive);
        Verdict(22, -9, 40m, "baseline_better").ShouldBe(HoldOutStatus.Inconclusive);
    }

    [Theory]
    [InlineData(null, 6.3, 7.4, "no_measurable_difference", true)]
    [InlineData(41, null, 7.4, "no_measurable_difference", true)]
    [InlineData(41, 6.3, null, "no_measurable_difference", true)]
    [InlineData(41, 6.3, 7.4, null, true)]
    [InlineData(41, 6.3, 7.4, "too_little_history", true)]
    [InlineData(41, 6.3, 7.4, "no_measurable_difference", false)]
    public void Any_unavailable_figure_or_a_gross_basis_is_never_a_pass(int? trades, double? cagr, double? drawdown, string? answer, bool net)
    {
        Verdict(trades, cagr, drawdown is { } d ? (decimal)d : null, answer, net).ShouldBe(HoldOutStatus.Inconclusive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void A_drawdown_limit_out_of_range_is_a_problem(double limit) => new SystemReplayPassRule(MaxDrawdownPct: limit).Problems().ShouldNotBeEmpty();

    private static readonly (DateOnly Start, DateOnly End)[] Stored =
    [
        (new DateOnly(2026, 5, 1), new DateOnly(2026, 11, 4)),
        (new DateOnly(2026, 11, 5), new DateOnly(2027, 5, 4)),
    ];

    [Fact]
    public void Before_block_three_begins_it_is_the_target_even_though_it_is_not_stored_yet()
    {
        SystemTarget.Pick(Stored, 6, new DateOnly(2027, 4, 20))!.Value.Start.ShouldBe(new DateOnly(2027, 5, 5));
        SystemTarget.Pick(Stored, 6, new DateOnly(2027, 5, 4))!.Value.Start.ShouldBe(new DateOnly(2027, 5, 5));
        SystemTarget.Pick(Stored, 6, new DateOnly(2026, 12, 1))!.Value.End.ShouldBe(new DateOnly(2027, 11, 4));
    }

    [Fact]
    public void Once_the_next_block_has_begun_there_is_no_future_block_and_block_one_and_two_are_never_targets()
    {
        SystemTarget.Pick(Stored, 6, new DateOnly(2027, 5, 5)).ShouldBeNull();
        SystemTarget.Pick(Stored, 6, new DateOnly(2026, 11, 4))!.Value.Start.ShouldBe(new DateOnly(2026, 11, 5));
        SystemTarget.Pick([], 6, new DateOnly(2026, 1, 1)).ShouldBeNull();
    }
}
