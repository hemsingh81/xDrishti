using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-02 criterion 7: the capacity score is a Beta posterior mean; a bucket with too few orders is insufficient and its shrunk value is not shown.</summary>
public sealed class CapacityScoreTests
{
    private static CapacityCounts Counts(int full, int notFull) => new(full, notFull, 0, 0, 0, 0);

    [Fact]
    public void Eighteen_of_twenty_is_Beta_19_3_with_mean_0_8636_and_an_interval()
    {
        var r = CapacityScore.ForAccount(Counts(18, 2), 10);
        r.Score.State.ShouldBe(MetricState.Value);
        r.Score.Number.ShouldNotBeNull().ShouldBe(19.0 / 22.0, 1e-12);
        r.Lo.ShouldNotBeNull().ShouldBeInRange(0.65, 0.75);
        r.Hi.ShouldNotBeNull().ShouldBeInRange(0.96, 1.0);
        r.Lo!.Value.ShouldBeLessThan(r.Score.Number!.Value);
    }

    [Fact]
    public void A_bucket_of_five_orders_is_insufficient_and_shows_no_score()
    {
        var r = CapacityScore.ForChild(Counts(3, 2), 19.0 / 22.0, 10, 10);
        r.Score.State.ShouldBe(MetricState.Insufficient);
        r.Score.Number.ShouldBeNull();
        r.Lo.ShouldBeNull();
        r.Score.Reason.ShouldNotBeNull().ShouldContain("n = 5");
    }

    [Fact]
    public void A_bucket_with_enough_orders_is_shrunk_toward_its_parent()
    {
        // Beta(10 * 0.8636 + 3, 10 * 0.1364 + 2) mean is 0.7757 for 5 orders; with 10 orders the same share is shown.
        var shrunk = CapacityScore.ForChild(Counts(6, 4), 0.8636, 10, 10);
        shrunk.Score.State.ShouldBe(MetricState.Value);
        shrunk.Score.Number.ShouldNotBeNull().ShouldBe(((10 * 0.8636) + 6) / (10 + 10), 1e-12);
        var low = CapacityScore.ForChild(Counts(3, 2), 19.0 / 22.0, 10, 5);
        low.Score.Number.ShouldNotBeNull().ShouldBe(0.7757, 1e-3);
    }

    [Fact]
    public void Fewer_than_ten_counted_orders_anywhere_is_insufficient_and_none_is_empty()
    {
        CapacityScore.ForAccount(Counts(8, 1), 10).Score.State.ShouldBe(MetricState.Insufficient);
        CapacityScore.ForAccount(Counts(0, 0), 10).Score.State.ShouldBe(MetricState.Empty);
        CapacityScore.ForChild(Counts(10, 0), null, 10, 10).Score.State.ShouldBe(MetricState.Unavailable);
    }

    [Fact]
    public void A_bucket_with_too_few_orders_is_insufficient_even_when_the_parent_rate_is_unavailable()
    {
        CapacityScore.ForChild(Counts(3, 2), null, 10, 10).Score.State.ShouldBe(MetricState.Insufficient);
        CapacityScore.ForChild(Counts(0, 0), null, 10, 10).Score.State.ShouldBe(MetricState.Empty);
    }
}
