using XDrishti.Domain.Market.History;

namespace XDrishti.Domain.Tests.Market.History;

/// <summary>CF-04 Part B criteria 4 and 5: the sizing of the backfill and the disk guard.</summary>
public sealed class BackfillEstimateTests
{
    [Fact]
    public void Two_hundred_fourteen_instruments_of_a_2_9_year_gap_are_58_million_rows_and_2568_requests()
    {
        var estimate = BackfillEstimate.ForEqual(214, 2.9, 12, 4);

        BackfillEstimate.RowsOfGap(2.9).ShouldBe(271_875);
        estimate.Rows.ShouldBe(58_181_250);
        estimate.Requests.ShouldBe(2_568);
        estimate.MinutesAtRate.ShouldBe(10.7, 0.01);
        BackfillEstimate.ForEqual(214, 2.9, 12, 1).MinutesAtRate.ShouldBe(42.8, 0.01);
    }

    [Fact]
    public void The_range_of_the_gap_is_50_to_64_million_rows_and_500_instruments_are_135_9_million_rows_and_6000_requests()
    {
        BackfillEstimate.ForEqual(214, 2.5, 12, 4).Rows.ShouldBe(50_156_250);
        BackfillEstimate.ForEqual(214, 3.2, 12, 4).Rows.ShouldBe(64_200_000);
        var big = BackfillEstimate.ForEqual(500, 2.9, 12, 4);

        big.Rows.ShouldBe(135_937_500);
        big.Requests.ShouldBe(6_000);
        big.MinutesAtRate.ShouldBe(25, 0.01);
    }

    [Fact]
    public void The_disk_guard_needs_twice_the_estimated_bytes_and_unknown_free_space_is_not_enough()
    {
        var rows = 58_181_250L;

        BackfillDiskGuard.BytesOf(rows, 60).ShouldBe(3_490_875_000);
        BackfillDiskGuard.RequiredBytes(rows, 60, 2).ShouldBe(6_981_750_000);
        BackfillDiskGuard.Enough(6_981_749_999, rows, 60, 2).ShouldBeFalse();
        BackfillDiskGuard.Enough(6_981_750_000, rows, 60, 2).ShouldBeTrue();
        BackfillDiskGuard.Enough(null, rows, 60, 2).ShouldBeFalse();
    }
}
