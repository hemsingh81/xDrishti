using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-01 criteria 4 and 5: the fixture day is matched as hand-worked, the result does not depend on the input order, a fill that fits two tickets is left alone.</summary>
public sealed class LiveFillMatcherTests
{
    [Fact]
    public void The_fixture_day_has_two_entries_one_exit_and_one_discretionary_fill()
    {
        var links = LiveFillMatcher.Match(LiveFixtureDay.Day, LiveFixtureDay.Tickets, LiveFixtureDay.Fills);

        links.Count.ShouldBe(4);
        links.Where(l => l.Role == JournalRole.Entry).Select(l => l.ItemId).ShouldBe([1L, 1L]);
        links.Single(l => l.Role == JournalRole.Exit).Fill.Qty.ShouldBe(40);
        links.Single(l => l.Role == JournalRole.Discretionary).Fill.Symbol.ShouldBe("SBIN");
        links.ShouldAllBe(l => !l.IsUnmatched);
    }

    [Fact]
    public void The_same_links_come_from_shuffled_input()
    {
        var expected = LiveFillMatcher.Match(LiveFixtureDay.Day, LiveFixtureDay.Tickets, LiveFixtureDay.Fills).Select(l => (l.Fill.Id, l.Role, l.ItemId)).ToList();

        var shuffled = LiveFillMatcher.Match(LiveFixtureDay.Day, [.. LiveFixtureDay.Tickets.Reverse()], [.. LiveFixtureDay.Fills.Reverse()]).Select(l => (l.Fill.Id, l.Role, l.ItemId)).ToList();

        shuffled.ShouldBe(expected);
    }

    [Fact]
    public void A_fill_that_fits_two_approved_tickets_stays_unmatched_with_the_reason()
    {
        var two = new[] { LiveFixtureDay.Ticket(1, "RELIANCE"), LiveFixtureDay.Ticket(9, "RELIANCE") };

        var links = LiveFillMatcher.Match(LiveFixtureDay.Day, two, [LiveFixtureDay.Fills[0]]);

        links.Single().IsUnmatched.ShouldBeTrue();
        links.Single().Reason.ShouldStartWith("ambiguous");
    }

    [Fact]
    public void A_fill_before_the_approval_is_the_owners_own_trade()
    {
        var late = LiveFixtureDay.Ticket(1, "RELIANCE") with { ApprovedAt = LiveFixtureDay.At("10:00") };

        LiveFillMatcher.Match(LiveFixtureDay.Day, [late], [LiveFixtureDay.Fills[0]]).Single().Role.ShouldBe(JournalRole.Discretionary);
    }

    [Fact]
    public void A_fill_of_another_account_is_not_matched_to_the_ticket()
    {
        var other = LiveFixtureDay.Fills[0] with { AccountId = Guid.NewGuid() };

        LiveFillMatcher.Match(LiveFixtureDay.Day, LiveFixtureDay.Tickets, [other]).Single().Role.ShouldBe(JournalRole.Discretionary);
    }
}
