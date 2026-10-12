using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Domain.Tests.Market.Baskets;

public sealed class BasketMemberTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);

    private const string Owner = "owner@example.invalid";

    private static BasketMember NewMember() => BasketMember.Add(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, "why", Owner, Now);

    [Fact]
    public void A_new_member_is_active_from_now_with_its_note_and_who_added_it()
    {
        var member = NewMember();

        (member.IsActive, member.ValidFrom, member.ValidTo, member.Note, member.AddedBy, member.RemovedBy).ShouldBe((true, Now, null, "why", Owner, null));
    }

    [Fact]
    public void Removing_ends_the_stay_once_and_records_who_removed_it()
    {
        var member = NewMember();

        member.Remove("other@example.invalid", Now.AddDays(1)).ShouldBeTrue();
        member.Remove(Owner, Now.AddDays(2)).ShouldBeFalse();

        (member.IsActive, member.ValidTo, member.RemovedBy).ShouldBe((false, Now.AddDays(1), "other@example.invalid"));
    }

    [Fact]
    public void A_blank_actor_is_recorded_as_unknown_and_a_long_one_is_cut()
    {
        BasketMember.Add(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, null, " ", Now).AddedBy.ShouldBe("unknown");
        BasketMember.Add(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, null, new string('a', 300), Now).AddedBy.Length.ShouldBe(256);
    }

    [Fact]
    public void Moving_to_the_same_position_changes_nothing()
    {
        var member = NewMember();

        member.MoveTo(0).ShouldBeFalse();
        member.MoveTo(4).ShouldBeTrue();

        member.Position.ShouldBe(4);
    }

    [Fact]
    public void A_member_is_a_member_at_the_end_of_the_day_it_was_added_but_not_of_the_day_it_was_removed()
    {
        // Added on the 11th at 09:30 India time, removed on the 15th.
        var member = BasketMember.Add(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, null, Owner, Now);
        member.Remove(Owner, Now.AddDays(4));

        member.WasMemberAt(BasketMember.CutoffAfter(new DateOnly(2026, 10, 10))).ShouldBeFalse();
        member.WasMemberAt(BasketMember.CutoffAfter(new DateOnly(2026, 10, 11))).ShouldBeTrue();
        member.WasMemberAt(BasketMember.CutoffAfter(new DateOnly(2026, 10, 14))).ShouldBeTrue();
        member.WasMemberAt(BasketMember.CutoffAfter(new DateOnly(2026, 10, 15))).ShouldBeFalse();
        member.WasMemberAt(BasketMember.CutoffAfter(new DateOnly(2030, 1, 1))).ShouldBeFalse();
    }

    [Fact]
    public void A_member_that_was_not_removed_is_a_member_on_every_date_from_its_start()
    {
        var member = NewMember();

        member.WasMemberAt(BasketMember.CutoffAfter(new DateOnly(2026, 10, 11))).ShouldBeTrue();
        member.WasMemberAt(BasketMember.CutoffAfter(new DateOnly(2040, 1, 1))).ShouldBeTrue();
    }

    [Fact]
    public void The_cutoff_is_midnight_in_India()
    {
        var cutoff = BasketMember.CutoffAfter(new DateOnly(2026, 10, 11));

        cutoff.ShouldBe(new DateTimeOffset(2026, 10, 11, 18, 30, 0, TimeSpan.Zero));
        cutoff.Offset.ShouldBe(TimeSpan.Zero, "the database takes UTC only");
    }

    [Fact]
    public void A_note_is_trimmed_blank_means_none_and_it_has_at_most_five_hundred_characters()
    {
        BasketMember.CheckNote("  hi ").Value.ShouldBe("hi");
        BasketMember.CheckNote("   ").Value.ShouldBeNull();
        BasketMember.CheckNote(null).Value.ShouldBeNull();
        BasketMember.CheckNote(new string('a', 500)).IsSuccess.ShouldBeTrue();
        BasketMember.CheckNote(new string('a', 501)).Error.Fields!.Single().Field.ShouldBe("note");
    }
}
