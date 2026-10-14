using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Live;
using static XDrishti.Application.Tests.Live.FeedTestData;

namespace XDrishti.Application.Tests.Live;

/// <summary>The pure builder of the active set (P2-10 criteria 4 and 7): merge, order, cut, difference.</summary>
public sealed class ActiveSetBuilderTests
{
    [Fact]
    public void Candidates_of_the_same_security_are_merged_with_their_reasons_and_the_richer_mode()
    {
        var set = ActiveSetBuilder.Build([Alert(Reliance, "2885"), Holding(Reliance, "2885", 5000m)], 200);

        var member = set.Members.ShouldHaveSingleItem();
        member.Reasons.ShouldBe(ActiveSetReason.Alert | ActiveSetReason.Holding);
        member.Mode.ShouldBe(FeedMode.Quote);
        set.Cut.ShouldBe(0);
    }

    [Fact]
    public void The_same_security_id_in_two_segments_is_two_members()
    {
        var set = ActiveSetBuilder.Build([Holding(Reliance, "13", 100m), Index(Nifty, "13")], 200);

        set.Members.Count.ShouldBe(2);
    }

    [Fact]
    public void Members_are_ordered_alerts_then_indices_then_holdings_by_value()
    {
        var small = Guid.NewGuid();
        var large = Guid.NewGuid();
        var alert = Guid.NewGuid();

        var set = ActiveSetBuilder.Build([Holding(small, "10", 100m), Holding(large, "11", 9000m), Index(Nifty, "13"), Alert(alert, "12")], 200);

        set.Members.Select(m => m.InstrumentId).ShouldBe([alert, Nifty, large, small]);
    }

    [Fact]
    public void An_index_that_is_also_an_alert_ranks_as_an_alert()
    {
        var holding = Guid.NewGuid();

        var set = ActiveSetBuilder.Build([Holding(holding, "10", 1m), Index(Nifty, "13"), new ActiveSetCandidate(Vix, FeedSegments.Index, "21", FeedMode.Ticker, ActiveSetReason.Index | ActiveSetReason.Alert)], 200);

        set.Members.Select(m => m.InstrumentId).ShouldBe([Vix, Nifty, holding]);
    }

    [Fact]
    public void The_cap_cuts_holdings_by_value_last_and_says_how_many_were_cut()
    {
        var alert = Guid.NewGuid();
        var small = Guid.NewGuid();
        var large = Guid.NewGuid();

        var set = ActiveSetBuilder.Build([Holding(small, "10", 100m), Holding(large, "11", 9000m), Index(Nifty, "13"), Alert(alert, "12")], 3);

        set.Members.Select(m => m.InstrumentId).ShouldBe([alert, Nifty, large]);
        set.Cut.ShouldBe(1);
    }

    [Fact]
    public void An_empty_set_is_a_set()
    {
        var set = ActiveSetBuilder.Build([], 200);

        set.Members.ShouldBeEmpty();
        set.Cut.ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("12.5")]
    public void A_candidate_whose_security_id_is_not_a_positive_number_is_left_out(string securityId)
    {
        var set = ActiveSetBuilder.Build([Holding(Reliance, securityId, 100m)], 200);

        set.Members.ShouldBeEmpty();
    }

    [Fact]
    public void Equal_values_are_ordered_by_security_id_so_the_same_input_gives_the_same_set()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var first = ActiveSetBuilder.Build([Holding(a, "20", 100m), Holding(b, "10", 100m)], 200);
        var second = ActiveSetBuilder.Build([Holding(b, "10", 100m), Holding(a, "20", 100m)], 200);

        first.Members.Select(m => m.SecurityId).ShouldBe(["10", "20"]);
        second.Members.ShouldBe(first.Members);
    }

    [Fact]
    public void The_difference_from_nothing_subscribes_everything()
    {
        var next = ActiveSetBuilder.Build([Holding(Reliance, "2885", 1m), Index(Nifty, "13")], 200);

        var diff = ActiveSetBuilder.Diff(null, next);

        diff.Subscribe.Count.ShouldBe(2);
        diff.Unsubscribe.ShouldBeEmpty();
    }

    [Fact]
    public void The_difference_between_two_sets_is_what_was_added_and_what_was_removed()
    {
        var before = ActiveSetBuilder.Build([Holding(Reliance, "2885", 1m), Holding(Infosys, "1594", 1m)], 200);
        var after = ActiveSetBuilder.Build([Holding(Reliance, "2885", 1m), Index(Nifty, "13")], 200);

        var diff = ActiveSetBuilder.Diff(before, after);

        diff.Subscribe.Select(m => m.InstrumentId).ShouldBe([Nifty]);
        diff.Unsubscribe.Select(m => m.InstrumentId).ShouldBe([Infosys]);
    }

    [Fact]
    public void A_change_of_mode_unsubscribes_the_old_mode_and_subscribes_the_new_one()
    {
        var before = ActiveSetBuilder.Build([Alert(Reliance, "2885")], 200);
        var after = ActiveSetBuilder.Build([Alert(Reliance, "2885"), Holding(Reliance, "2885", 1m)], 200);

        var diff = ActiveSetBuilder.Diff(before, after);

        diff.Unsubscribe.ShouldHaveSingleItem().Mode.ShouldBe(FeedMode.Ticker);
        diff.Subscribe.ShouldHaveSingleItem().Mode.ShouldBe(FeedMode.Quote);
    }

    [Fact]
    public void An_unchanged_set_has_an_empty_difference_even_when_the_reasons_changed()
    {
        var before = ActiveSetBuilder.Build([Holding(Reliance, "2885", 1m)], 200);
        var after = ActiveSetBuilder.Build([Holding(Reliance, "2885", 9m)], 200);

        ActiveSetBuilder.Diff(before, after).IsEmpty.ShouldBeTrue();
    }
}
