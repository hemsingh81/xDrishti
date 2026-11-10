using XDrishti.Domain.Baskets.Derived;

namespace XDrishti.Domain.Tests.Baskets.Derived;

/// <summary>Buffer and dwell (spec BK-02 acceptance criteria 14 and 15). Symbols are "R1".."Rn" so the symbol is the rank.</summary>
public sealed class RankSelectorTests
{
    private static List<string> Ranked(int n) => [.. Enumerable.Range(1, n).Select(i => $"R{i}")];

    private static Dictionary<string, int> Held(params int[] ranks) => ranks.ToDictionary(r => $"R{r}", _ => 99);

    [Fact]
    public void Incumbents_inside_the_buffer_stay_and_the_best_non_members_fill_the_free_slots()
    {
        var incumbents = Held([.. Enumerable.Range(1, 27), 44, 46, 50]);

        var members = RankSelector.Select(Ranked(60), 30, 1.5, incumbents, 0);

        members.Count.ShouldBe(30);
        members.ShouldContain("R44");
        members.ShouldNotContain("R46");
        members.ShouldNotContain("R50");
        members.ShouldContain("R28");
        members.ShouldContain("R29");
        members.ShouldNotContain("R31");
    }

    [Fact]
    public void A_new_instrument_at_rank_31_does_not_displace_an_incumbent_at_rank_44()
    {
        var incumbents = Held([.. Enumerable.Range(1, 29), 44]);

        var members = RankSelector.Select(Ranked(60), 30, 1.5, incumbents, 0);

        members.ShouldContain("R44");
        members.ShouldNotContain("R30");
        members.ShouldNotContain("R31");
    }

    [Fact]
    public void When_more_than_k_incumbents_are_inside_the_buffer_the_best_k_by_rank_stay()
    {
        var incumbents = Held([.. Enumerable.Range(1, 40)]);

        var members = RankSelector.Select(Ranked(60), 30, 1.5, incumbents, 0);

        members.ShouldBe([.. Enumerable.Range(1, 30).Select(i => $"R{i}")]);
    }

    [Fact]
    public void Without_incumbents_the_top_k_are_taken_in_rank_order()
    {
        RankSelector.Select(Ranked(10), 3, 1.5, null, 0).ShouldBe(["R1", "R2", "R3"]);
        RankSelector.Select(Ranked(2), 3, 1.5, null, 0).ShouldBe(["R1", "R2"]);
        RankSelector.Select([], 3, 1.5, null, 0).ShouldBeEmpty();
    }

    [Fact]
    public void A_member_added_3_sessions_ago_at_rank_60_stays_until_5_sessions_have_passed()
    {
        var ranked = Ranked(100);

        RankSelector.Select(ranked, 30, 1.5, new Dictionary<string, int> { ["R60"] = 3 }, 5).ShouldContain("R60");
        RankSelector.Select(ranked, 30, 1.5, new Dictionary<string, int> { ["R60"] = 5 }, 5).ShouldNotContain("R60");
    }

    [Fact]
    public void A_dwelling_member_is_dropped_first_when_the_basket_is_full()
    {
        var incumbents = new Dictionary<string, int> { ["R60"] = 1 };
        foreach (var r in Enumerable.Range(1, 30))
        {
            incumbents[$"R{r}"] = 9;
        }

        RankSelector.Select(Ranked(100), 30, 1.5, incumbents, 5).ShouldNotContain("R60");
    }

    [Theory]
    [InlineData(0, 0.1, 0)]
    [InlineData(100, 0.1, 10)]
    [InlineData(5, 0.1, 1)]
    public void Quantile_k_is_the_top_fraction_and_at_least_one_when_any_pass(int count, double fraction, int expected)
    {
        RankSelector.QuantileK(count, fraction).ShouldBe(expected);
    }
}
