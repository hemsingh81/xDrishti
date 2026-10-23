using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>Spec P7-14: the matcher of stored fills to approved tickets, on a hand-checked fixture day.</summary>
public sealed class JournalMatcherTests
{
    private static readonly DateOnly Day = new(2026, 10, 20);
    private static readonly Guid Paper = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Other = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly DateTimeOffset Approved = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static int _n;

    private static JournalTicket Ticket(long item, string symbol = "RELIANCE", Side side = Side.Buy, Guid? account = null, int rank = 1) =>
        new(item, 1, rank, account ?? Paper, symbol, side, Day, Approved);

    private static JournalFill Fill(string symbol = "RELIANCE", Side side = Side.Buy, Guid? account = null, DateOnly? date = null, int? hour = 10, long qty = 10, decimal price = 100m, decimal charges = 0m) =>
        new(new Guid(Interlocked.Increment(ref _n), 0, 0, new byte[8]), account ?? Paper, symbol, side, date ?? Day, hour is { } h ? new TimeOnly(h, 0) : null, qty, price, charges);

    [Fact]
    public void A_fixture_day_matches_by_account_instrument_side_and_day()
    {
        var tickets = new[] { Ticket(1), Ticket(2, "TCS", Side.Sell, rank: 2), Ticket(3, "INFY", rank: 3) };
        var buyReliance = Fill();
        var sellTcs = Fill("TCS", Side.Sell, hour: 11);
        var otherAccount = Fill(account: Other);
        var otherSymbol = Fill("HDFCBANK");
        var otherSide = Fill("INFY", Side.Sell, hour: 9);
        var otherDay = Fill(date: Day.AddDays(-1));

        var result = JournalMatcher.Match(Day, tickets, [buyReliance, sellTcs, otherAccount, otherSymbol, otherSide, otherDay]);

        Entries(result).ShouldBe([(buyReliance.TransactionId, 1L), (sellTcs.TransactionId, 2L)], ignoreOrder: true);
        Discretionary(result).ShouldBe([otherAccount.TransactionId, otherSymbol.TransactionId, otherSide.TransactionId], ignoreOrder: true);
        result.Links.Any(l => l.Fill.TransactionId == otherDay.TransactionId).ShouldBeFalse();
        result.Unmatched.ShouldBeEmpty();
    }

    [Fact]
    public void A_ticket_may_have_several_fills_and_a_fill_matches_one_ticket()
    {
        var a = Fill(hour: 10, qty: 4);
        var b = Fill(hour: 11, qty: 6);

        var result = JournalMatcher.Match(Day, [Ticket(1)], [a, b]);

        result.Links.Where(l => l.Role == JournalRole.Entry).Select(l => l.ItemId).ShouldBe([1L, 1L]);
        result.Links.Select(l => l.Fill.TransactionId).Distinct().Count().ShouldBe(result.Links.Count);
    }

    [Fact]
    public void An_ambiguous_fill_is_left_unmatched_with_a_reason_and_is_not_discretionary()
    {
        var fill = Fill();

        var result = JournalMatcher.Match(Day, [Ticket(1), Ticket(2, rank: 2)], [fill]);

        result.Links.ShouldBeEmpty();
        result.Unmatched.Count.ShouldBe(1);
        result.Unmatched[0].Reason.ShouldContain("ambiguous");
        result.Unmatched[0].Reason.ShouldContain("not guessed");
    }

    [Fact]
    public void The_same_symbol_on_two_accounts_is_not_ambiguous()
    {
        var mine = Fill(account: Paper);
        var theirs = Fill(account: Other);

        var result = JournalMatcher.Match(Day, [Ticket(1, account: Paper), Ticket(1, account: Other)], [mine, theirs]);

        result.Unmatched.ShouldBeEmpty();
        Entries(result).Count.ShouldBe(2);
    }

    [Fact]
    public void A_fill_before_the_approval_is_not_the_tickets_and_is_discretionary()
    {
        var early = Fill(hour: 5); var late = new JournalTicket(1, 1, 1, Paper, "RELIANCE", Side.Buy, Day, new DateTimeOffset(Day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromMinutes(330)));

        var result = JournalMatcher.Match(Day, [late], [early, Fill(hour: 10)]);

        Discretionary(result).ShouldBe([early.TransactionId]);
        Entries(result).Count.ShouldBe(1);
    }

    [Fact]
    public void An_exit_is_the_other_side_strictly_after_the_entry_up_to_the_entered_quantity()
    {
        var entry = Fill(hour: 10, qty: 10);
        var tooEarly = Fill(side: Side.Sell, hour: 9, qty: 10);
        var sameTime = Fill(side: Side.Sell, hour: 10, qty: 10);
        var exitA = Fill(side: Side.Sell, hour: 12, qty: 6, price: 103m);
        var exitB = Fill(side: Side.Sell, hour: 13, qty: 8, price: 104m);

        var result = JournalMatcher.Match(Day, [Ticket(1)], [entry, tooEarly, sameTime, exitA, exitB]);

        var exits = result.Links.Where(l => l.Role == JournalRole.Exit).ToList();
        exits.Select(l => (l.Fill.TransactionId, l.Qty)).ShouldBe([(exitA.TransactionId, 6L), (exitB.TransactionId, 4L)]);
        Discretionary(result).ShouldBe([tooEarly.TransactionId, sameTime.TransactionId], ignoreOrder: true);
    }

    [Fact]
    public void An_exit_on_a_later_day_counts_and_a_fill_without_a_time_cannot_be_ordered_on_the_entry_day()
    {
        var entry = Fill(hour: 10, qty: 5);
        var noTime = Fill(side: Side.Sell, hour: null, qty: 5);
        var later = Fill(side: Side.Sell, date: Day.AddDays(2), hour: 9, qty: 5);

        var result = JournalMatcher.Match(Day, [Ticket(1)], [entry, noTime, later]);

        result.Links.Where(l => l.Role == JournalRole.Exit).Select(l => l.Fill.TransactionId).ShouldBe([later.TransactionId]);
        Discretionary(result).ShouldBe([noTime.TransactionId]);
    }

    [Fact]
    public void An_exit_is_never_shared_between_two_tickets_beyond_its_quantity()
    {
        var e1 = Fill("RELIANCE", hour: 10, qty: 5);
        var e2 = Fill("TCS", hour: 10, qty: 5);
        var exit = Fill("RELIANCE", Side.Sell, hour: 12, qty: 5);

        var result = JournalMatcher.Match(Day, [Ticket(1), Ticket(2, "TCS", rank: 2)], [e1, e2, exit]);

        result.Links.Count(l => l.Role == JournalRole.Exit).ShouldBe(1);
        result.Links.Single(l => l.Role == JournalRole.Exit).ItemId.ShouldBe(1L);
    }

    [Fact]
    public void The_result_does_not_depend_on_the_order_of_the_input()
    {
        var fills = new[] { Fill(hour: 10, qty: 4), Fill(hour: 11, qty: 6), Fill(side: Side.Sell, hour: 13, qty: 10), Fill("TCS"), Fill(account: Other) };
        var tickets = new[] { Ticket(1), Ticket(2, "TCS", rank: 2) };

        var forward = JournalMatcher.Match(Day, tickets, fills);
        var backward = JournalMatcher.Match(Day, [.. tickets.Reverse()], [.. fills.Reverse()]);

        backward.Links.ShouldBe(forward.Links);
        backward.Unmatched.ShouldBe(forward.Unmatched);
    }

    [Fact]
    public void No_ticket_means_every_fill_of_the_day_is_discretionary()
    {
        var result = JournalMatcher.Match(Day, [], [Fill(), Fill(side: Side.Sell)]);

        result.Links.ShouldAllBe(l => l.Role == JournalRole.Discretionary && l.ItemId == null);
    }

    private static List<(Guid Fill, long Item)> Entries(JournalResult r) => [.. r.Links.Where(l => l.Role == JournalRole.Entry).Select(l => (l.Fill.TransactionId, l.ItemId!.Value))];

    private static List<Guid> Discretionary(JournalResult r) => [.. r.Links.Where(l => l.Role == JournalRole.Discretionary).Select(l => l.Fill.TransactionId)];
}
