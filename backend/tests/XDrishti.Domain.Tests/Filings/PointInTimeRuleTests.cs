using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>Spec P15-02 criteria 5 to 7: the visibility boundary, the supersede chain, the revision decisions and the hand example.</summary>
public sealed class PointInTimeRuleTests
{
    private static readonly TimeSpan Lag = TimeSpan.FromMinutes(5);

    private static readonly DateOnly Q1 = new(2026, 6, 30);

    // Friday 2026-07-24 16:12 IST = 10:42 UTC.
    private static readonly DateTimeOffset Broadcast = new(2026, 7, 24, 10, 42, 0, TimeSpan.Zero);

    [Fact]
    public void A_filing_is_visible_from_broadcast_plus_lag_inclusive_and_not_a_second_before()
    {
        var filing = FilingSamples.Ok(1, Q1, Broadcast);

        PointInTimeRule.IsVisible(filing, Broadcast + Lag - TimeSpan.FromSeconds(1), Lag).ShouldBeFalse();
        PointInTimeRule.IsVisible(filing, Broadcast + Lag, Lag).ShouldBeTrue();
        PointInTimeRule.IsVisible(filing, Broadcast + Lag + TimeSpan.FromSeconds(1), Lag).ShouldBeTrue();
    }

    [Fact]
    public void A_zero_lag_makes_the_filing_visible_at_its_broadcast_instant()
    {
        PointInTimeRule.IsVisible(FilingSamples.Ok(1, Q1, Broadcast), Broadcast, TimeSpan.Zero).ShouldBeTrue();
    }

    [Fact]
    public void An_undated_filing_is_never_visible_whatever_the_instant()
    {
        PointInTimeRule.IsVisible(FilingSamples.Undated(1, Q1), DateTimeOffset.MaxValue, Lag).ShouldBeFalse();
    }

    [Fact]
    public void The_hand_example_shows_nothing_at_16_16_59_and_the_row_at_16_17_00_india_time()
    {
        var original = FilingSamples.Ok(1, Q1, Broadcast);
        var ist = TimeSpan.FromMinutes(330);

        PointInTimeRule.AsOf([original], new DateTimeOffset(2026, 7, 24, 16, 16, 59, ist), Lag).State.ShouldBe(AsOfResult.NoneVisible);
        var at = PointInTimeRule.AsOf([original], new DateTimeOffset(2026, 7, 24, 16, 17, 0, ist), Lag);

        at.State.ShouldBe(AsOfResult.Value);
        at.Filings.Select(f => f.Id).ShouldBe([original.Id]);
    }

    [Fact]
    public void A_restatement_is_invisible_before_its_own_time_and_replaces_the_original_from_then()
    {
        var original = FilingSamples.Ok(1, Q1, Broadcast);
        var restated = FilingSamples.Ok(2, Q1, new DateTimeOffset(2026, 8, 30, 4, 30, 0, TimeSpan.Zero), original.Id);
        var ist = TimeSpan.FromMinutes(330);

        var before = PointInTimeRule.AsOf([original, restated], new DateTimeOffset(2026, 8, 29, 12, 0, 0, ist), Lag);
        var justBefore = PointInTimeRule.AsOf([original, restated], new DateTimeOffset(2026, 8, 30, 10, 4, 59, ist), Lag);
        var after = PointInTimeRule.AsOf([original, restated], new DateTimeOffset(2026, 8, 30, 10, 5, 0, ist), Lag);

        before.Filings.Select(f => f.Id).ShouldBe([original.Id]);
        justBefore.Filings.Select(f => f.Id).ShouldBe([original.Id]);
        after.Filings.Select(f => f.Id).ShouldBe([restated.Id]);
    }

    [Fact]
    public void One_current_filing_per_period_newest_period_first()
    {
        var q1 = FilingSamples.Ok(1, new DateOnly(2026, 3, 31), Broadcast.AddDays(-90));
        var q2 = FilingSamples.Ok(2, Q1, Broadcast);

        var result = PointInTimeRule.AsOf([q1, q2], Broadcast.AddDays(1), Lag);

        result.Filings.Select(f => f.PeriodEnd).ShouldBe([Q1, new DateOnly(2026, 3, 31)]);
    }

    [Fact]
    public void An_out_of_order_filing_is_visible_by_its_own_time_and_does_not_hide_the_later_one()
    {
        var late = FilingSamples.Ok(1, Q1, Broadcast);
        var early = FilingSamples.Ok(2, Q1, Broadcast.AddDays(-3));

        PointInTimeRule.AsOf([late, early], Broadcast.AddDays(-2), Lag).Filings.Select(f => f.Id).ShouldBe([early.Id]);
        PointInTimeRule.AsOf([late, early], Broadcast.AddDays(1), Lag).Filings.Select(f => f.Id).ShouldBe([late.Id]);
    }

    [Fact]
    public void Filings_without_a_period_each_stand_alone()
    {
        var a = FilingSamples.Ok(1, null, Broadcast, kind: FilingKind.Announcement);
        var b = FilingSamples.Ok(2, null, Broadcast.AddHours(1), kind: FilingKind.Announcement);

        PointInTimeRule.AsOf([a, b], Broadcast.AddDays(1), Lag).Filings.Count.ShouldBe(2);
    }

    [Fact]
    public void An_undated_filing_alone_makes_the_state_unavailable_with_a_count_and_is_never_listed()
    {
        var result = PointInTimeRule.AsOf([FilingSamples.Undated(1, Q1), FilingSamples.Undated(2, Q1, "no_offset")], Broadcast, Lag);

        (result.State, result.UnavailableCount, result.Filings.Count).ShouldBe((AsOfResult.Unavailable, 2, 0));
    }

    [Fact]
    public void An_undated_filing_next_to_a_visible_one_is_counted_and_the_state_is_value()
    {
        var result = PointInTimeRule.AsOf([FilingSamples.Ok(1, Q1, Broadcast), FilingSamples.Undated(2, new DateOnly(2026, 3, 31))], Broadcast.AddDays(1), Lag);

        (result.State, result.UnavailableCount, result.Filings.Count).ShouldBe((AsOfResult.Value, 1, 1));
    }

    [Fact]
    public void Nothing_stored_is_none_visible()
    {
        FilingSamples.Serialize(PointInTimeRule.AsOf([], Broadcast, Lag)).ShouldBe(FilingSamples.Serialize(new AsOfResult(AsOfResult.NoneVisible, [], 0)));
    }

    [Fact]
    public void A_new_file_with_no_head_a_missing_time_or_no_period_stands_alone()
    {
        var head = FilingSamples.Ok(1, Q1, Broadcast);

        PointInTimeRule.Decide(FilingSamples.New(2, Q1, Broadcast.AddDays(1)), []).ShouldBe(new RevisionDecision(RevisionOutcome.Store, null));
        PointInTimeRule.Decide(FilingSamples.New(2, Q1, null), [head]).ShouldBe(new RevisionDecision(RevisionOutcome.Store, null));
        PointInTimeRule.Decide(FilingSamples.New(2, null, Broadcast.AddDays(1), FilingKind.Announcement), [head]).ShouldBe(new RevisionDecision(RevisionOutcome.Store, null));
    }

    [Fact]
    public void A_later_file_of_the_group_supersedes_the_head()
    {
        var head = FilingSamples.Ok(1, Q1, Broadcast);

        PointInTimeRule.Decide(FilingSamples.New(2, Q1, Broadcast.AddMinutes(1)), [head]).ShouldBe(new RevisionDecision(RevisionOutcome.Store, head.Id));
    }

    [Fact]
    public void An_earlier_file_is_stored_unlinked_and_flagged_out_of_order()
    {
        var head = FilingSamples.Ok(1, Q1, Broadcast);

        PointInTimeRule.Decide(FilingSamples.New(2, Q1, Broadcast.AddMinutes(-1)), [head]).ShouldBe(new RevisionDecision(RevisionOutcome.StoreOutOfOrder, null));
    }

    [Fact]
    public void An_equal_time_with_other_bytes_is_ambiguous_and_not_stored()
    {
        var head = FilingSamples.Ok(1, Q1, Broadcast);

        PointInTimeRule.Decide(FilingSamples.New(2, Q1, Broadcast), [head]).Outcome.ShouldBe(RevisionOutcome.AmbiguousOrder);
    }

    [Fact]
    public void A_time_equal_to_any_stored_time_of_the_group_is_ambiguous_not_just_the_latest()
    {
        var a = FilingSamples.Ok(1, Q1, Broadcast);
        var b = FilingSamples.Ok(2, Q1, Broadcast.AddHours(1), a.Id);

        PointInTimeRule.Decide(FilingSamples.New(3, Q1, Broadcast), [a, b]).Outcome.ShouldBe(RevisionOutcome.AmbiguousOrder);
        PointInTimeRule.Decide(FilingSamples.New(3, Q1, Broadcast.AddMinutes(1)), [a, b]).Outcome.ShouldBe(RevisionOutcome.StoreOutOfOrder);
    }

    [Fact]
    public void A_sidecar_time_is_cut_to_whole_microseconds()
    {
        var seven = new DateTimeOffset(2026, 7, 24, 10, 42, 0, TimeSpan.Zero).AddTicks(1234567);

        NewFiling.ToMicroseconds(seven)!.Value.UtcTicks.ShouldBe(seven.UtcTicks - 7);
        NewFiling.ToMicroseconds(null).ShouldBeNull();
    }

    [Fact]
    public void A_supersede_across_instrument_kind_or_period_or_not_strictly_later_is_refused()
    {
        var head = FilingSamples.Ok(1, Q1, Broadcast);

        PointInTimeRule.CanSupersede(head, FilingSamples.New(2, Q1, Broadcast.AddMinutes(1))).ShouldBeTrue();
        PointInTimeRule.CanSupersede(head, FilingSamples.New(2, Q1, Broadcast)).ShouldBeFalse();
        PointInTimeRule.CanSupersede(head, FilingSamples.New(2, new DateOnly(2026, 3, 31), Broadcast.AddMinutes(1))).ShouldBeFalse();
        PointInTimeRule.CanSupersede(head, FilingSamples.New(2, Q1, Broadcast.AddMinutes(1), FilingKind.Shareholding)).ShouldBeFalse();
        PointInTimeRule.CanSupersede(head, FilingSamples.New(2, Q1, Broadcast.AddMinutes(1)) with { InstrumentId = Guid.NewGuid() }).ShouldBeFalse();
        PointInTimeRule.CanSupersede(head, FilingSamples.New(2, Q1, null)).ShouldBeFalse();
    }

    [Fact]
    public void A_supersede_of_an_undated_filing_is_refused()
    {
        PointInTimeRule.CanSupersede(FilingSamples.Undated(1, Q1), FilingSamples.New(2, Q1, Broadcast)).ShouldBeFalse();
    }

    [Fact]
    public void The_same_sidecar_ignores_the_file_name_and_the_ingest_time_but_not_the_time_the_symbol_the_kind_or_the_period()
    {
        var stored = FilingSamples.Ok(1, Q1, Broadcast);
        var same = FilingSamples.New(1, Q1, Broadcast) with { FileName = "other.xml", IngestedAt = Broadcast.AddYears(1) };

        same.SameSidecar(stored).ShouldBeTrue();
        (same with { BroadcastTs = Broadcast.AddMinutes(1) }).SameSidecar(stored).ShouldBeFalse();
        (same with { InstrumentId = Guid.NewGuid() }).SameSidecar(stored).ShouldBeFalse();
        (same with { Kind = FilingKind.Shareholding }).SameSidecar(stored).ShouldBeFalse();
        (same with { PeriodEnd = new DateOnly(2026, 3, 31) }).SameSidecar(stored).ShouldBeFalse();
        (same with { BroadcastTs = null, State = BroadcastState.Unavailable }).SameSidecar(stored).ShouldBeFalse();
    }
}
