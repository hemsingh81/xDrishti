using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>The deterministic suggestion rules (spec P7-09).</summary>
public sealed class SuggestionRulesTests
{
    private static readonly DateOnly Monday = new(2026, 10, 26);

    private static readonly DateOnly Fresh = new(2026, 10, 22);

    private static SuggestionItem Item(TicketGroup group, string symbol, params string[] reasons) => new(group, symbol, reasons);

    private static SuggestionInput Plan(string basis, DateOnly? statsAsOf, params SuggestionItem[] items) => new(Monday, basis, items, statsAsOf);

    [Fact]
    public void A_plan_flagged_for_the_gate_gets_one_gate_suggestion()
    {
        var result = SuggestionRules.Evaluate(Plan("x " + PlanWording.GateUnavailableMarker + " (vix not known)", Fresh, Item(TicketGroup.Primary, "AAA")));

        result.Select(s => (s.Kind, s.Subject)).ShouldBe([(SuggestionKinds.MarketGateUnavailable, "plan")]);
    }

    [Fact]
    public void A_plan_whose_open_book_was_unavailable_gets_one_open_book_suggestion()
    {
        var result = SuggestionRules.Evaluate(Plan("x " + PlanWording.OpenBookUnavailableMarker + ": no primary tickets.", Fresh, Item(TicketGroup.Primary, "AAA")));

        result.Select(s => (s.Kind, s.Subject)).ShouldBe([(SuggestionKinds.OpenBookUnavailable, "plan")]);
    }

    [Fact]
    public void A_plan_without_a_primary_ticket_says_so()
    {
        var result = SuggestionRules.Evaluate(Plan("b", Fresh, Item(TicketGroup.Reserve, "AAA", "sector:fail")));

        result.Select(s => s.Kind).ShouldBe([SuggestionKinds.NoPrimaryTickets]);
    }

    [Fact]
    public void Tickets_held_for_one_unavailable_input_make_one_suggestion_naming_them_sorted()
    {
        var result = SuggestionRules.Evaluate(Plan("b", Fresh, Item(TicketGroup.Primary, "ZZZ"), Item(TicketGroup.Rejected, "BBB", "cost:unavailable", "win_rate:fail"), Item(TicketGroup.Rejected, "AAA", "cost:unavailable", "fno_ban:unavailable")));

        result.Select(s => (s.Kind, s.Subject)).ShouldBe([(SuggestionKinds.TicketsHeldUnavailableInput, "cost"), (SuggestionKinds.TicketsHeldUnavailableInput, "fno_ban")]);
        result[0].Text.ShouldContain("2 ticket(s)");
        result[0].Text.ShouldContain("AAA, BBB");
    }

    [Fact]
    public void A_primary_ticket_is_never_named_as_held()
    {
        var result = SuggestionRules.Evaluate(Plan("b", Fresh, Item(TicketGroup.Primary, "AAA", "cost:unavailable")));

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Many_held_tickets_are_named_up_to_a_cap()
    {
        var items = Enumerable.Range(0, 12).Select(i => Item(TicketGroup.Rejected, "S" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), "cost:unavailable")).Append(Item(TicketGroup.Primary, "P")).ToArray();

        var text = SuggestionRules.Evaluate(Plan("b", Fresh, items)).Single().Text;

        text.ShouldContain("12 ticket(s)");
        text.ShouldContain("and 4 more");
    }

    [Theory]
    [InlineData(2026, 10, 19, false)] // 7 days before the plan date: the weekly refresh, normal
    [InlineData(2026, 10, 18, true)] // 8 days
    public void Statistics_older_than_seven_days_are_stale(int y, int m, int d, bool stale)
    {
        var result = SuggestionRules.Evaluate(Plan("b", new DateOnly(y, m, d), Item(TicketGroup.Primary, "AAA")));

        result.Any(s => s.Kind == SuggestionKinds.CellStatsStale).ShouldBe(stale);
    }

    [Fact]
    public void No_statistics_at_all_is_stale()
    {
        SuggestionRules.Evaluate(Plan("b", null, Item(TicketGroup.Primary, "AAA"))).Single().Kind.ShouldBe(SuggestionKinds.CellStatsStale);
    }

    [Fact]
    public void The_same_plan_gives_the_same_suggestions_in_any_ticket_order()
    {
        var items = new[] { Item(TicketGroup.Rejected, "B", "cost:unavailable"), Item(TicketGroup.Rejected, "A", "fno_ban:unavailable"), Item(TicketGroup.Reserve, "C", "cost:unavailable") };

        var a = SuggestionRules.Evaluate(Plan("b", null, items));
        var b = SuggestionRules.Evaluate(Plan("b", null, [.. items.Reverse()]));

        b.ShouldBe(a);
    }

    [Fact]
    public void The_first_plan_wording_is_pinned()
    {
        PlanWording.Proposed.ShouldBe("Proposed, provisional: no order exists, nothing here is approved.");
        PlanWording.GateUnavailableMarker.ShouldBe("market gate unavailable");
    }
}
