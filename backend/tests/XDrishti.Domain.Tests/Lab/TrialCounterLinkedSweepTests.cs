using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>AU-13 (ADR 0243 point 5): a training sweep of a version a family descends from is a look at the family and is in the honest count; a sweep of an unrelated version is not.</summary>
public sealed class TrialCounterLinkedSweepTests
{
    [Fact]
    public void Linked_sweeps_are_added_to_the_total_and_unlinked_ones_are_not()
    {
        var before = new TrialCounts(2, 1, 3, 2, 4, TrainingSweeps: 5, TrainingSweepTrials: 0);
        var linked = before with { LinkedSweeps = 2 };

        before.Total.ShouldBe(12);
        linked.Total.ShouldBe(14);
        linked.DistinctHashes.ShouldBe(before.DistinctHashes, "the distinct-hash count of the old judgement does not move");
    }

    [Fact]
    public void The_honest_judged_count_includes_the_linked_sweeps_and_the_old_one_does_not()
    {
        var parts = new TrialCounts(3, 0, 0, 0, 0, 4, 0, LinkedSweeps: 4);

        TrialCounter.Judged(parts, honest: true).ShouldBe(7);
        TrialCounter.Judged(parts, honest: false).ShouldBe(3);
    }

    [Fact]
    public void A_count_made_before_the_link_existed_is_unchanged()
    {
        new TrialCounts(2, 0, 3, 2, 4).Total.ShouldBe(11);
        new TrialCounts(2, 0, 3, 2, 4, 9, 9).Total.ShouldBe(11);
    }
}
