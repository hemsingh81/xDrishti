using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.History;

namespace XDrishti.Domain.Tests.Market.History;

/// <summary>CF-04 Part B criteria 9 (the overlap probe), 11 (the seam continuity) and 7 (the writer's guard, pure part).</summary>
public sealed class SeamProbeTests
{
    private static readonly SeamProbeLimits Limits = SeamProbeLimits.Default;

    [Fact]
    public void Three_bars_with_a_0_05_percent_difference_pass()
    {
        var result = SeamProbe.Evaluate([100.00, 100.10, 99.95], [100.00, 100.10, 99.90], Limits);

        result.Verdict.ShouldBe(SeamProbeVerdict.Passed);
        result.Compared.ShouldBe(3);
        result.MedianBps.ShouldBe(0);
        result.WithinMaxPct.ShouldBe(100);
    }

    [Fact]
    public void A_series_at_exactly_one_fifth_of_the_csv_has_a_median_of_8000_bps_and_fails_unadjusted_and_passes_once_the_factor_is_applied()
    {
        var csv = Enumerable.Repeat(100.0, 50).ToList();
        var raw = Enumerable.Repeat(500.0, 50).ToList();
        var oneFifth = Enumerable.Repeat(20.0, 50).ToList();

        SeamProbe.Evaluate(oneFifth, csv, Limits).MedianBps.ShouldBe(8_000, 1e-6);
        SeamProbe.Evaluate(raw, csv, Limits).Verdict.ShouldBe(SeamProbeVerdict.Failed);
        SeamProbe.Evaluate([.. raw.Select(p => p * 0.2)], csv, Limits).Verdict.ShouldBe(SeamProbeVerdict.Passed);
    }

    [Theory]
    [InlineData(100, SeamProbeVerdict.Passed)]
    [InlineData(101, SeamProbeVerdict.Failed)]
    public void A_hundred_of_ten_thousand_bars_off_by_more_than_50_bps_is_exactly_at_the_limit_and_a_hundred_and_one_fails(int off, SeamProbeVerdict expected)
    {
        var csv = Enumerable.Repeat(100.0, 10_000).ToList();
        var dhan = csv.Select((p, i) => i < off ? p * 1.01 : p).ToList();

        SeamProbe.Evaluate(dhan, csv, Limits).Verdict.ShouldBe(expected);
    }

    [Fact]
    public void No_paired_bar_proves_nothing()
    {
        SeamProbe.Evaluate([], [], Limits).Verdict.ShouldBe(SeamProbeVerdict.NoOverlap);
        Should.Throw<ArgumentException>(() => SeamProbe.Evaluate([1.0], [], Limits));
    }

    [Fact]
    public void A_small_move_at_the_seam_passes_and_a_32_percent_move_is_a_seam_jump_and_exactly_20_percent_passes()
    {
        SeamContinuity.IsJump(250.00, 252.10).ShouldBeFalse();
        SeamContinuity.Move(250.00, 252.10).ShouldBe(0.0084, 1e-6);
        SeamContinuity.IsJump(250.00, 330.00).ShouldBeTrue();
        SeamContinuity.IsJump(250.00, 300.00).ShouldBeFalse();
        SeamContinuity.IsJump(250.00, 199.00).ShouldBeTrue();
    }

    [Fact]
    public void The_guard_drops_every_bar_dated_on_or_after_the_seam_date_and_counts_it()
    {
        var seam = new DateTimeOffset(2024, 4, 2, 9, 15, 0, TimeSpan.FromMinutes(330));
        var lastDay = new Bar(new DateTimeOffset(2024, 4, 1, 15, 29, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds(), 1, 1, 1, 1, 1);
        var seamDayBeforeCsv = new Bar(new DateTimeOffset(2024, 4, 2, 9, 14, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds(), 1, 1, 1, 1, 1);
        var atSeam = new Bar(seam.ToUnixTimeSeconds(), 1, 1, 1, 1, 1);
        var after = new Bar(seam.ToUnixTimeSeconds() + 60, 1, 1, 1, 1, 1);

        var (allowed, beyond) = SeamGuard.Split([lastDay, seamDayBeforeCsv, atSeam, after], seam);

        allowed.ShouldBe([lastDay]);
        beyond.ShouldBe(3);
    }

    [Theory]
    [InlineData(0, 0, 0, ProbeStatus.None, false, BackfillStatus.None)]
    [InlineData(10, 0, 0, ProbeStatus.None, false, BackfillStatus.Planned)]
    [InlineData(10, 4, 0, ProbeStatus.None, false, BackfillStatus.Partial)]
    [InlineData(10, 0, 1, ProbeStatus.None, false, BackfillStatus.Partial)]
    [InlineData(10, 10, 0, ProbeStatus.Passed, false, BackfillStatus.Done)]
    [InlineData(10, 10, 0, ProbeStatus.Unresolved, false, BackfillStatus.SeamUnresolved)]
    [InlineData(10, 10, 0, ProbeStatus.None, true, BackfillStatus.SeamJump)]
    public void The_status_is_derived_from_the_chunks_the_probe_and_the_jump(int total, int finished, int failed, string probe, bool jump, string expected) =>
        BackfillStatus.Of(total, finished, failed, probe, jump).ShouldBe(expected);
}
