using XDrishti.Domain.Options.Quality;
using static XDrishti.Domain.Tests.Options.Quality.ChainBuilder;

namespace XDrishti.Domain.Tests.Options.Quality;

public sealed class OiVolumeCheckTests
{
    private static readonly OptionQualityThresholds T = new();

    private static DateTimeOffset At(int index) => Slot.AddMinutes(5 * index);

    private static ChainPoint Point(int index, long? oi, long? volume, long contract = 1) => Quote(10_000, true, 500, ts: At(index), id: contract, oi: oi, volume: volume);

    [Fact]
    public void A_contract_whose_volume_and_oi_move_gently_passes()
    {
        var check = OiVolumeCheck.Evaluate([Point(0, 1_000, 10), Point(1, 1_100, 20), Point(2, 1_150, 30)], 75, T);

        check.Status.ShouldBe(QualityStatus.Pass);
        check.N.ShouldBe(2);
    }

    [Fact]
    public void A_fall_in_cumulative_volume_is_a_fail()
    {
        var check = OiVolumeCheck.Evaluate([Point(0, 1_000, 50), Point(1, 1_000, 40)], 75, T);

        check.Status.ShouldBe(QualityStatus.Fail);
        check.Detail.ShouldNotBeNull().ShouldContain("\"volume_falls\":1");
    }

    [Fact]
    public void An_oi_jump_below_ten_lots_is_not_counted_and_one_above_is()
    {
        // lot 100: 10 lots = 1,000 contracts. 1,000 -> 2,000 is +100 % but exactly 10 lots; 1,000 -> 2,001 is above.
        var below = OiVolumeCheck.Evaluate([Point(0, 1_000, 1), Point(1, 2_000, 2)], 100, T);
        var above = OiVolumeCheck.Evaluate([Point(0, 1_000, 1), Point(1, 2_001, 2)], 100, T);

        below.Status.ShouldBe(QualityStatus.Pass);
        above.Status.ShouldBe(QualityStatus.Warn); // one pair of one is 100 %, above the 1 % warn line
        above.Detail.ShouldNotBeNull().ShouldContain("\"oi_jumps\":1");
    }

    [Fact]
    public void With_no_lot_size_the_percent_alone_decides()
    {
        OiVolumeCheck.Evaluate([Point(0, 100, 1), Point(1, 200, 2)], null, T).Status.ShouldBe(QualityStatus.Warn);
        OiVolumeCheck.Evaluate([Point(0, 100, 1), Point(1, 140, 2)], null, T).Status.ShouldBe(QualityStatus.Pass);
    }

    [Fact]
    public void A_jump_is_never_a_fail_however_many_there_are()
    {
        var points = Enumerable.Range(0, 20).Select(i => Point(i, i % 2 == 0 ? 1_000 : 5_000, i)).ToList();

        OiVolumeCheck.Evaluate(points, 75, T).Status.ShouldBe(QualityStatus.Warn);
    }

    [Fact]
    public void One_jump_among_a_hundred_and_fifty_pairs_is_inside_the_warn_share()
    {
        var points = Enumerable.Range(0, 151).Select(i => Point(i, i == 100 ? 9_000 : 1_000, i)).ToList();

        // pairs 99->100 and 100->101 both jump (2 of 150 = 1.33 %), so the share is above 1 %
        OiVolumeCheck.Evaluate(points, 75, T).Status.ShouldBe(QualityStatus.Warn);
        OiVolumeCheck.Evaluate([.. Enumerable.Range(0, 301).Select(i => Point(i, i == 100 ? 9_000 : 1_000, i))], 75, T).Status.ShouldBe(QualityStatus.Pass); // 2 of 300 = 0.67 %
    }

    [Fact]
    public void Contracts_are_followed_separately_and_a_null_oi_or_volume_is_skipped()
    {
        var check = OiVolumeCheck.Evaluate([Point(0, 1_000, 10, 1), Point(0, 9_000, 99, 2), Point(1, null, null, 1), Point(1, 9_100, 100, 2)], 75, T);

        check.Status.ShouldBe(QualityStatus.Pass);
        check.N.ShouldBe(2);
    }

    [Fact]
    public void A_single_snapshot_has_no_pair_and_is_unavailable()
    {
        var check = OiVolumeCheck.Evaluate([Point(0, 1_000, 10)], 75, T);

        check.Status.ShouldBe(QualityStatus.Unavailable);
        check.Reason.ShouldBe("too_few_points");
    }

    [Fact]
    public void A_jump_from_zero_open_interest_has_no_percent_and_is_not_counted()
    {
        OiVolumeCheck.Evaluate([Point(0, 0, 1), Point(1, 50_000, 2)], 75, T).Status.ShouldBe(QualityStatus.Pass);
    }
}
