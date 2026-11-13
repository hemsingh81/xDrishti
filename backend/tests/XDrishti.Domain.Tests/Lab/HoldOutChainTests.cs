using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>HO-01 (ADR 0283): the chain arithmetic, the derived states, the auto-release rule and the label of released data.</summary>
public sealed class HoldOutChainTests
{
    [Fact]
    public void The_chain_gives_the_worked_dates_of_the_spec()
    {
        var block1End = new DateOnly(2026, 11, 4);

        var two = HoldOutChain.Next(block1End, 6);
        var three = HoldOutChain.Next(two.End, 6);

        two.ShouldBe((new DateOnly(2026, 11, 5), new DateOnly(2027, 5, 4)));
        three.ShouldBe((new DateOnly(2027, 5, 5), new DateOnly(2027, 11, 4)));
    }

    [Fact]
    public void A_block_after_a_change_of_the_length_uses_the_new_length_and_leaves_stored_blocks_alone()
    {
        var two = HoldOutChain.Next(new DateOnly(2026, 11, 4), 6);

        var three = HoldOutChain.Next(two.End, 3);

        three.ShouldBe((new DateOnly(2027, 5, 5), new DateOnly(2027, 8, 4)));
        two.ShouldBe((new DateOnly(2026, 11, 5), new DateOnly(2027, 5, 4)), "block 2 is a value that was computed once; nothing recomputes it");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(13)]
    public void A_length_outside_three_to_twelve_months_is_refused(int months) =>
        Should.Throw<ArgumentOutOfRangeException>(() => HoldOutChain.Next(new DateOnly(2026, 11, 4), months));

    [Fact]
    public void A_block_is_sealed_when_no_trading_day_is_left_up_to_its_end()
    {
        var calendar = new TradingCalendar();
        var tuesday = new DateOnly(2027, 5, 4);

        HoldOutChain.IsSealed(calendar, new DateOnly(2027, 5, 3), tuesday).ShouldBeFalse("the session of the last day has not closed");
        HoldOutChain.IsSealed(calendar, tuesday, tuesday).ShouldBeTrue();
        HoldOutChain.IsSealed(calendar, new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 8)).ShouldBeTrue("a block that ends on a Sunday seals at the close of the Friday");
    }

    [Theory]
    [InlineData(true, true, true, HoldOutBlockState.Released)]
    [InlineData(true, false, false, HoldOutBlockState.Released)]
    [InlineData(false, false, true, HoldOutBlockState.Filling)]
    [InlineData(false, true, true, HoldOutBlockState.Looked)]
    [InlineData(false, true, false, HoldOutBlockState.Sealed)]
    public void The_state_is_derived_from_the_release_the_seal_and_the_fresh_look(bool released, bool sealedBlock, bool fresh, HoldOutBlockState expected) =>
        HoldOutChain.StateOf(released, sealedBlock, fresh).ShouldBe(expected);

    [Fact]
    public void Every_state_has_its_wire_word() =>
        Enum.GetValues<HoldOutBlockState>().Select(HoldOutChain.Word).ShouldBe(["filling", "sealed", "looked", "released"]);

    [Theory]
    [InlineData(HoldOutBlockState.Looked, true)]
    [InlineData(HoldOutBlockState.Sealed, false)]
    [InlineData(HoldOutBlockState.Filling, false)]
    [InlineData(HoldOutBlockState.Released, false)]
    public void Only_a_sealed_block_with_a_fresh_look_and_no_look_available_is_released_by_itself(HoldOutBlockState state, bool eligible) =>
        HoldOutAutoRelease.Eligible(state, [LookAvailability.Used, LookAvailability.NoSubject], out _).ShouldBe(eligible);

    [Theory]
    [InlineData(LookAvailability.Available, "look_available")]
    [InlineData(LookAvailability.Unavailable, "unavailable")]
    public void A_look_still_available_or_an_unavailable_read_keeps_the_block(LookAvailability kind, string reason)
    {
        HoldOutAutoRelease.Eligible(HoldOutBlockState.Looked, [LookAvailability.Used, kind], out var why).ShouldBeFalse();

        why.ShouldBe(reason);
    }

    [Fact]
    public void A_block_with_a_fresh_look_and_no_registered_kind_is_released_and_an_unlooked_one_names_its_reason()
    {
        HoldOutAutoRelease.Eligible(HoldOutBlockState.Looked, [], out _).ShouldBeTrue();
        HoldOutAutoRelease.Eligible(HoldOutBlockState.Sealed, [LookAvailability.NoSubject], out var reason).ShouldBeFalse();

        reason.ShouldBe("unlooked");
    }

    [Fact]
    public void Released_blocks_that_a_window_reaches_give_a_seen_holdout_label_each()
    {
        (DateOnly, DateOnly)[] released = [(new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30)), (new DateOnly(2026, 10, 31), new DateOnly(2027, 4, 30))];

        SeenHoldOut.Labels(released, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)).ShouldBe(["seen_holdout:2026-05-01"]);
        SeenHoldOut.Labels(released, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 3)).ShouldBe(["seen_holdout:2026-05-01", "seen_holdout:2026-10-31"]);
        SeenHoldOut.Labels(released, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)).ShouldBeEmpty();
        SeenHoldOut.Labels([], new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)).ShouldBeEmpty("nothing released: no label, nothing changes");
    }

    [Fact]
    public void The_options_default_to_the_auto_options_on_and_a_six_month_block_and_the_snapshot_carries_them()
    {
        var defaults = new HoldOutOptions();

        (defaults.AutoUnlock, defaults.AutoRelease, defaults.BlockMonths, defaults.Enforce).ShouldBe((true, true, 6, true)); // Enforce defaults ON since HO-01b (ADR 0282)
        defaults.KeyText().ShouldContain("auto_release=on");
        defaults.KeyText().ShouldContain("block_months=6");
        defaults.SnapshotHash().ShouldNotBe((defaults with { AutoRelease = false }).SnapshotHash());
        defaults.Problems().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(13)]
    public void A_block_length_outside_three_to_twelve_is_a_problem(int months) =>
        (new HoldOutOptions(BlockMonths: months)).Problems().ShouldContain("HoldOut:BlockMonths must be 3 to 12");

    [Theory]
    [InlineData(2026, 5, 1, 30)]
    [InlineData(2026, 11, 5, 30)]
    [InlineData(2027, 1, 1, 45)]
    public void With_nothing_released_the_training_cut_is_the_old_inline_sum_for_any_start(int y, int m, int d, int embargo)
    {
        var start = new DateOnly(y, m, d);

        HoldOutOptions.TrainingCut(start, embargo).ShouldBe(start.AddDays(-1 - embargo));
    }
}
