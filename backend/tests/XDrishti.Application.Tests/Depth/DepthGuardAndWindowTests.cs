using XDrishti.Application.Depth;

namespace XDrishti.Application.Tests.Depth;

/// <summary>P7-16 criteria 3 and 7: the recorded part of the day and the disk guard's thresholds.</summary>
public sealed class DepthGuardAndWindowTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    [Theory]
    [InlineData(9, 14, 59, 999, false)]
    [InlineData(9, 15, 0, 0, true)]
    [InlineData(12, 0, 0, 0, true)]
    [InlineData(15, 29, 59, 999, true)]
    [InlineData(15, 30, 0, 0, false)]
    [InlineData(15, 31, 0, 0, false)]
    public void Only_09_15_00_up_to_15_30_00_india_time_is_recorded(int hour, int minute, int second, int millisecond, bool recorded) =>
        DepthWindow.Contains(new DateTimeOffset(2026, 10, 12, hour, minute, second, millisecond, Ist)).ShouldBe(recorded);

    [Fact]
    public void The_window_is_read_in_india_time_whatever_the_offset_of_the_instant() =>
        DepthWindow.Contains(new DateTimeOffset(2026, 10, 12, 4, 0, 0, TimeSpan.Zero)).ShouldBeTrue(); // 09:30 IST

    [Fact]
    public void Under_both_limits_the_guard_is_ok() =>
        DepthDiskGuard.Check(10 * Gb, 100 * Gb, 40 * Gb, 20 * Gb).ShouldBe(DepthDiskVerdict.Ok);

    [Fact]
    public void At_eighty_percent_of_the_cap_the_guard_warns_and_goes_on() =>
        DepthDiskGuard.Check(32 * Gb, 100 * Gb, 40 * Gb, 20 * Gb).ShouldBe(DepthDiskVerdict.Warning);

    [Fact]
    public void Above_the_cap_the_guard_stops_for_the_budget_and_exactly_at_it_does_not()
    {
        DepthDiskGuard.Check((40 * Gb) + 1, 100 * Gb, 40 * Gb, 20 * Gb).ShouldBe(DepthDiskVerdict.OverBudget);
        DepthDiskGuard.Check(40 * Gb, 100 * Gb, 40 * Gb, 20 * Gb).ShouldBe(DepthDiskVerdict.Warning);
    }

    [Fact]
    public void Below_the_free_floor_the_guard_stops_for_the_disk_and_exactly_at_it_does_not()
    {
        DepthDiskGuard.Check(1 * Gb, (20 * Gb) - 1, 40 * Gb, 20 * Gb).ShouldBe(DepthDiskVerdict.LowFree);
        DepthDiskGuard.Check(1 * Gb, 20 * Gb, 40 * Gb, 20 * Gb).ShouldBe(DepthDiskVerdict.Ok);
    }

    [Fact]
    public void The_budget_is_checked_before_the_free_space() =>
        DepthDiskGuard.Check(50 * Gb, 1 * Gb, 40 * Gb, 20 * Gb).ShouldBe(DepthDiskVerdict.OverBudget);

    [Fact]
    public void Unreadable_free_space_leaves_the_size_cap_alone_in_force()
    {
        DepthDiskGuard.Check(1 * Gb, null, 40 * Gb, 20 * Gb).ShouldBe(DepthDiskVerdict.Ok);
        DepthDiskGuard.Check(41 * Gb, null, 40 * Gb, 20 * Gb).ShouldBe(DepthDiskVerdict.OverBudget);
    }
}
