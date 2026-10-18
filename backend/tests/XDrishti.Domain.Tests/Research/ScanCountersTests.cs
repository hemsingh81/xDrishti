using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>The counters of the entry scanner are pure sums (spec P3-03a, D10): figures add, conditions merge by path in the order first seen, two sides never add.</summary>
public sealed class ScanCountersTests
{
    private static ScanCounters Counters(Side side, long k, params ConditionCounter[] conditions) =>
        new(side, k, k + 1, k + 2, k + 3, k + 4, k + 5, k + 6, k + 7, k + 8, k + 9, k + 10, k + 11, k + 12, k + 13, k + 14, conditions);

    [Fact]
    public void Empty_is_all_zero_with_no_conditions_and_is_the_identity_of_add()
    {
        var some = Counters(Side.Buy, 10, new ConditionCounter("buy.trigger[0]", 1, 2, 3, 4));

        ScanCounters.Empty(Side.Buy).Conditions.ShouldBeEmpty();
        ScanCounters.Empty(Side.Buy).Add(some).ShouldBe(some);
        some.Add(ScanCounters.Empty(Side.Buy)).ShouldBe(some);
    }

    [Fact]
    public void Every_figure_is_added()
    {
        var sum = Counters(Side.Sell, 1).Add(Counters(Side.Sell, 100));

        sum.ShouldBe(Counters(Side.Sell, 101) with
        {
            Bars = 101,
            IgnoredBars = 103,
            IncompleteBarsConsumed = 105,
            DecisionSteps = 107,
            LostDecisions = 109,
            StaleBucketSteps = 111,
            OutOfWindowSteps = 113,
            WindowSteps = 115,
            FullyReadySteps = 117,
            Candidates = 119,
            LaterTriggers = 121,
            BlockedByInvalidation = 123,
            InvalidatedDays = 125,
            StopNotReady = 127,
            UnknownInvalidation = 129,
        });
    }

    [Fact]
    public void Conditions_are_merged_by_path_and_keep_the_order_first_seen()
    {
        var a = Counters(Side.Buy, 0, new ConditionCounter("buy.setup[0]", 1, 1, 1, 1), new ConditionCounter("buy.trigger[0]", 2, 2, 2, 2));
        var b = Counters(Side.Buy, 0, new ConditionCounter("buy.trigger[0]", 10, 20, 30, 40), new ConditionCounter("buy.invalidate[0]", 5, 5, 5, 5));

        var merged = a.Add(b).Conditions;

        merged.Select(c => c.Path).ShouldBe(["buy.setup[0]", "buy.trigger[0]", "buy.invalidate[0]"]);
        merged[1].ShouldBe(new ConditionCounter("buy.trigger[0]", 12, 22, 32, 42));
        a.Conditions.Count.ShouldBe(2, "the operands are not changed");
    }

    [Fact]
    public void Counters_of_two_sides_do_not_add()
    {
        Should.Throw<ArgumentException>(() => ScanCounters.Empty(Side.Buy).Add(ScanCounters.Empty(Side.Sell)));
    }

    [Fact]
    public void Counters_are_equal_when_every_figure_and_every_condition_in_order_is_equal()
    {
        var one = Counters(Side.Buy, 3, new ConditionCounter("a", 1, 2, 3, 4));
        var same = Counters(Side.Buy, 3, new ConditionCounter("a", 1, 2, 3, 4));

        one.ShouldBe(same);
        one.GetHashCode().ShouldBe(same.GetHashCode());
        one.ShouldNotBe(same with { Candidates = 0 });
        one.ShouldNotBe(Counters(Side.Buy, 3, new ConditionCounter("a", 1, 2, 3, 5)));
    }

    [Fact]
    public void A_null_operand_is_a_programming_error_and_the_eligible_steps_are_the_sum_of_the_fates()
    {
        Should.Throw<ArgumentNullException>(() => ScanCounters.Empty(Side.Buy).Add(null!));
        (Counters(Side.Buy, 0) with { Candidates = 1, LaterTriggers = 2, BlockedByInvalidation = 3, UnknownInvalidation = 4, StopNotReady = 5 }).EligibleSteps.ShouldBe(15);
    }
}
