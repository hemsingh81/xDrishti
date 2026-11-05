using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-10 part 10c-1 (ADR 0216), spec AC7 and D5: sample uniqueness of overlapping labels.</summary>
public sealed class UniquenessWeightsTests
{
    [Fact]
    public void The_three_label_example_gives_the_hand_numbers()
    {
        // A over bars 1-2, B over 2-3, C over 3-4: c = (1, 2, 2, 1)
        var r = UniquenessWeights.Compute([(1, 2), (2, 3), (3, 4)]).Value;

        r.Uniqueness.ShouldBe([0.75, 0.5, 0.75]);
        r.EffectiveCount.ShouldBe(2.0);
        r.Weights.ShouldBe([1.125, 0.75, 1.125]);
    }

    [Fact]
    public void Labels_that_do_not_overlap_have_a_weight_of_one_each()
    {
        var r = UniquenessWeights.Compute([(1, 5), (6, 6), (10, 20), (30, 31)]).Value;

        r.Uniqueness.ShouldAllBe(u => u == 1.0);
        r.Weights.ShouldAllBe(w => w == 1.0);
        r.EffectiveCount.ShouldBe(4.0);
    }

    [Fact]
    public void Two_identical_labels_are_worth_one()
    {
        var r = UniquenessWeights.Compute([(5, 8), (5, 8)]).Value;

        r.Uniqueness.ShouldBe([0.5, 0.5]);
        r.EffectiveCount.ShouldBe(1.0);
        r.Weights.ShouldBe([1.0, 1.0]);
    }

    [Fact]
    public void A_short_label_inside_a_long_one_takes_the_long_ones_uniqueness_down()
    {
        // long over bars 1-4, short over bar 2: c = (1, 2, 1, 1); long: (1 + 0.5 + 1 + 1) / 4 = 0.875; short: 0.5
        var r = UniquenessWeights.Compute([(1, 4), (2, 2)]).Value;

        r.Uniqueness.ShouldBe([0.875, 0.5]);
    }

    [Fact]
    public void Shifting_every_bar_or_reordering_the_labels_changes_nothing_and_the_weights_have_mean_one()
    {
        (long, long)[] spans = [(100, 130), (120, 160), (150, 151), (400, 410), (405, 420), (125, 126)];
        var a = UniquenessWeights.Compute(spans).Value;
        var shifted = UniquenessWeights.Compute([.. spans.Select(s => (s.Item1 + 28_000_000L, s.Item2 + 28_000_000L))]).Value;
        var reversed = UniquenessWeights.Compute([.. spans.Reverse()]).Value;

        shifted.Uniqueness.ShouldBe(a.Uniqueness);
        reversed.Uniqueness.ShouldBe([.. a.Uniqueness.Reverse()]);
        a.Weights.Average().ShouldBe(1.0, 1e-12);
        a.Uniqueness.ShouldAllBe(u => u > 0 && u <= 1);
        a.EffectiveCount.ShouldBeLessThan(spans.Length);
    }

    [Fact]
    public void Property_the_uniqueness_matches_a_direct_count_of_the_labels_over_each_bar()
    {
        var random = new Random(7);
        (long, long)[] spans = [.. Enumerable.Range(0, 60).Select(_ => { var s = random.Next(0, 80); return ((long)s, (long)(s + random.Next(0, 15))); })];

        var r = UniquenessWeights.Compute(spans).Value;

        for (var i = 0; i < spans.Length; i++)
        {
            var (first, last) = spans[i];
            var direct = Enumerable.Range((int)first, (int)(last - first + 1)).Average(t => 1.0 / spans.Count(s => s.Item1 <= t && t <= s.Item2));
            r.Uniqueness[i].ShouldBe(direct, 1e-12);
        }
    }

    [Fact]
    public void No_labels_is_an_empty_result_and_a_bad_span_is_refused()
    {
        UniquenessWeights.Compute([]).Value.EffectiveCount.ShouldBe(0);
        UniquenessWeights.Compute([(5, 4)]).Error.Code.ShouldBe(UniquenessWeights.InvalidCode);
        UniquenessWeights.Compute([(1, long.MaxValue)]).Error.Code.ShouldBe(UniquenessWeights.InvalidCode);
    }
}
