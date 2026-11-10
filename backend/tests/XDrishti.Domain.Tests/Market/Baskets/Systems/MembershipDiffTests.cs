using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Domain.Tests.Market.Baskets.Systems;

public sealed class MembershipDiffTests
{
    private static Guid[] Ids(int count) => [.. Enumerable.Range(1, count).Select(i => new Guid(i, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]))];

    [Fact]
    public void Two_out_and_two_in_is_a_change_of_two()
    {
        var all = Ids(52);
        var current = all.Take(50).ToList();
        var incoming = all.Skip(2).ToList();

        var change = MembershipDiff.Compute(current, incoming);

        change.Added.ShouldBe(all.Skip(50));
        change.Removed.ShouldBe(all.Take(2));
        change.Size.ShouldBe(2);
    }

    [Fact]
    public void An_identical_list_changes_nothing()
    {
        var ids = Ids(10);

        var change = MembershipDiff.Compute(ids, ids);

        (change.Added.Count, change.Removed.Count).ShouldBe((0, 0));
    }

    [Fact]
    public void Against_nothing_everything_is_new_in_the_lists_order()
    {
        var ids = Ids(5);

        var change = MembershipDiff.Compute([], ids);

        change.Added.ShouldBe(ids);
        change.Removed.ShouldBeEmpty();
    }
}
