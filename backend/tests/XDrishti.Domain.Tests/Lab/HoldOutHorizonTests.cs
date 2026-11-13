using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>HO-01 D5 (ADR 0283): a look is fresh only when its subject was frozen before the block's horizon.</summary>
public sealed class HoldOutHorizonTests
{
    private static readonly DateOnly Start = new(2026, 11, 5);

    [Theory]
    [InlineData("2026-10-01T05:00:00Z", true)]
    [InlineData("2026-11-04T18:00:00Z", true)] // 23:30 India time on 2026-11-04: still before the start date
    [InlineData("2026-11-04T19:00:00Z", false)] // 00:30 India time on the start date
    [InlineData("2026-12-15T05:00:00Z", false)]
    public void At_a_later_block_the_subject_must_be_frozen_before_the_start_date_in_India_time(string frozen, bool expected)
    {
        var predated = HoldOutHorizon.IsPredated("strategy", false, Start, DateTimeOffset.Parse(frozen, System.Globalization.CultureInfo.InvariantCulture), null);

        predated.ShouldBe(expected);
    }

    [Theory]
    [InlineData("strategy")]
    [InlineData("model")]
    [InlineData("system")]
    public void At_a_later_block_an_unknown_freeze_never_qualifies(string kind) =>
        HoldOutHorizon.IsPredated(kind, false, Start, null, null).ShouldBeFalse();

    [Fact]
    public void At_a_later_block_a_system_subject_frozen_before_the_start_qualifies()
    {
        HoldOutHorizon.IsPredated("system", false, Start, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), null).ShouldBeTrue();
    }

    [Fact]
    public void At_block_one_a_system_look_is_never_fresh()
    {
        HoldOutHorizon.IsPredated("system", true, Start, DateTimeOffset.UnixEpoch, null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("strategy")]
    [InlineData("model")]
    public void At_block_one_with_no_look_yet_any_subject_qualifies(string kind) =>
        HoldOutHorizon.IsPredated(kind, true, Start, null, null).ShouldBeTrue();

    [Fact]
    public void At_block_one_the_subject_must_be_frozen_before_the_first_stored_look()
    {
        var firstLook = new DateTimeOffset(2026, 11, 4, 5, 0, 0, TimeSpan.Zero);

        HoldOutHorizon.IsPredated("model", true, Start, firstLook.AddMinutes(-1), firstLook).ShouldBeTrue();
        HoldOutHorizon.IsPredated("model", true, Start, firstLook.AddMinutes(1), firstLook).ShouldBeFalse();
        HoldOutHorizon.IsPredated("model", true, Start, null, firstLook).ShouldBeFalse();
    }

    [Fact]
    public void The_reason_word_and_the_stall_threshold_are_fixed()
    {
        HoldOutHorizon.NotPredated.ShouldBe("not_predated");
        HoldOutStall.Sessions.ShouldBe(20);
    }
}
