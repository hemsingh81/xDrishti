using XDrishti.Domain.Paper;

namespace XDrishti.Domain.Tests.Paper;

/// <summary>Spec P10-04 criterion 1: the counted set is closed, primary, measured, once per ticket, in exit order; the rest is counted apart.</summary>
public sealed class CountedSetTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 4, 0, 0, TimeSpan.Zero);

    private static PaperCompareRow Row(long id, string key, double? r = 1, int exitMinute = 60, string group = "primary", string status = "measured", string cell = "C", long item = 0, int entryMinute = 0) =>
        new(id, item == 0 ? id : item, group, status, cell, key, T0.AddMinutes(entryMinute), r is null ? null : T0.AddMinutes(exitMinute), r, r is null ? null : "net", r is null ? null : "flat-10");

    [Fact]
    public void Reserve_straddle_and_open_tickets_never_enter_and_are_counted_apart()
    {
        var set = CountedSet.Select(
            [Row(1, "a"), Row(2, "b", group: "reserve"), Row(3, "c", status: "straddle"), Row(4, "d", r: null), Row(5, "e")], otherEpochs: 7);

        set.Trades.Count.ShouldBe(2);
        set.Counts.ShouldBe(new CompareCounts(2, 1, 1, 1, 7));
    }

    [Fact]
    public void A_ticket_held_by_two_accounts_counts_once()
    {
        var set = CountedSet.Select([Row(1, "same", r: 2), Row(2, "same", r: 2), Row(3, "other", r: -1)], 0);

        set.Trades.Select(t => t.R).Order().ShouldBe([-1, 2]);
        set.Counts.Counted.ShouldBe(2);
    }

    [Fact]
    public void The_order_is_exit_then_entry_then_ticket_then_entry_id()
    {
        var set = CountedSet.Select(
            [
                Row(1, "a", r: 1, exitMinute: 90),
                Row(2, "b", r: 2, exitMinute: 60, entryMinute: 10),
                Row(3, "c", r: 3, exitMinute: 60, entryMinute: 5, item: 9),
                Row(4, "d", r: 4, exitMinute: 60, entryMinute: 5, item: 8),
            ], 0);

        set.Trades.Select(t => t.R).ShouldBe([4, 3, 2, 1]);
    }
}
