using XDrishti.Domain.Market;

namespace XDrishti.Domain.Tests.Market;

public sealed class TrackedInstrumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);

    private static readonly Guid InstrumentId = Guid.CreateVersion7();

    private const string Owner = "owner@example.invalid";

    [Fact]
    public void A_new_tracking_is_active_asks_for_history_and_has_none_yet()
    {
        var tracked = TrackedInstrument.Track(InstrumentId, Owner, Now);

        tracked.Id.ShouldBe(InstrumentId);
        (tracked.Active, tracked.ActiveSince, tracked.DeactivatedAt, tracked.BackfillRequestedAt, tracked.TrackedBy).ShouldBe((true, Now, null, Now, Owner));
        (tracked.DataFrom, tracked.DataTo, tracked.LastUpdate, tracked.Note).ShouldBe((null, null, null, null));
    }

    [Fact]
    public void Untracking_switches_it_off_and_keeps_everything_else()
    {
        var tracked = TrackedInstrument.Track(InstrumentId, Owner, Now);

        tracked.Deactivate(Now.AddDays(1)).ShouldBeTrue();

        (tracked.Active, tracked.DeactivatedAt, tracked.ActiveSince, tracked.BackfillRequestedAt).ShouldBe((false, Now.AddDays(1), Now, Now));
    }

    [Fact]
    public void Untracking_what_is_not_tracked_changes_nothing()
    {
        var tracked = TrackedInstrument.Track(InstrumentId, Owner, Now);
        tracked.Deactivate(Now.AddDays(1));

        tracked.Deactivate(Now.AddDays(2)).ShouldBeFalse();

        tracked.DeactivatedAt.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void Tracking_again_starts_a_new_period_asks_for_history_again_and_clears_the_untrack_time()
    {
        var tracked = TrackedInstrument.Track(InstrumentId, Owner, Now);
        tracked.Deactivate(Now.AddDays(1));

        tracked.Reactivate("other@example.invalid", Now.AddDays(5)).ShouldBeTrue();

        (tracked.Active, tracked.ActiveSince, tracked.DeactivatedAt, tracked.BackfillRequestedAt, tracked.TrackedBy)
            .ShouldBe((true, Now.AddDays(5), null, Now.AddDays(5), "other@example.invalid"));
    }

    [Fact]
    public void Tracking_what_is_tracked_already_changes_nothing()
    {
        var tracked = TrackedInstrument.Track(InstrumentId, Owner, Now);

        tracked.Reactivate("other@example.invalid", Now.AddDays(5)).ShouldBeFalse();

        (tracked.ActiveSince, tracked.BackfillRequestedAt, tracked.TrackedBy).ShouldBe((Now, Now, Owner));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_actor_is_recorded_as_unknown(string? actor) =>
        TrackedInstrument.Track(InstrumentId, actor!, Now).TrackedBy.ShouldBe("unknown");

    [Fact]
    public void A_very_long_actor_is_cut_to_the_column_size() =>
        TrackedInstrument.Track(InstrumentId, new string('x', 300), Now).TrackedBy.Length.ShouldBe(TrackedInstrument.MaxTrackedByLength);
}
