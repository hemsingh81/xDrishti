using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>The count of one condition (spec P3-03a, D10) adds by pure sums and only with its own path.</summary>
public sealed class ConditionCounterTests
{
    [Fact]
    public void Counts_add_and_the_total_is_their_sum()
    {
        var sum = new ConditionCounter("buy.trigger[0]", 1, 2, 3, 4).Add(new ConditionCounter("buy.trigger[0]", 10, 20, 30, 40));

        sum.ShouldBe(new ConditionCounter("buy.trigger[0]", 11, 22, 33, 44));
        sum.Total.ShouldBe(110);
    }

    [Fact]
    public void Counters_of_two_paths_do_not_add()
    {
        Should.Throw<ArgumentException>(() => new ConditionCounter("a", 0, 0, 0, 0).Add(new ConditionCounter("b", 0, 0, 0, 0)));
        Should.Throw<ArgumentNullException>(() => new ConditionCounter("a", 0, 0, 0, 0).Add(null!));
    }
}
