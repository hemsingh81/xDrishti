using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>ISO-week grouping of graded tickets for the Journey (spec P8-12, criterion 7).</summary>
public sealed class JourneyWeeksTests
{
    private static GradedItem Item(DateOnly day, double? r, TicketGroup group = TicketGroup.Primary) =>
        new(1, day, 1, group, TicketState.Closed, new TicketGrade(r is null ? GradeStatus.NotTriggered : GradeStatus.Graded, r, null, null, null, null, null, TicketGrade.Basis, "t"));

    [Fact]
    public void Five_graded_tickets_give_a_mean_and_fewer_are_insufficient_with_n_shown()
    {
        var full = Enumerable.Range(0, 5).Select(i => Item(new DateOnly(2026, 10, 19), i % 2 == 0 ? 1.0 : -0.5));
        var thin = Enumerable.Range(0, 4).Select(_ => Item(new DateOnly(2026, 10, 26), 1.0));

        var weeks = JourneyWeeks.Build([.. full, .. thin]);

        weeks.Count.ShouldBe(2);
        weeks[0].IsoWeek.ShouldBe("2026-W43");
        weeks[0].State.ShouldBe("ok");
        weeks[0].TotalR.ShouldBe(2.0, 1e-9);
        weeks[0].MeanR.ShouldNotBeNull().ShouldBe(0.4, 1e-9);
        weeks[1].State.ShouldBe("insufficient");
        weeks[1].Graded.ShouldBe(4);
        weeks[1].MeanR.ShouldBeNull();
    }

    [Fact]
    public void Weeks_follow_ISO_numbering_across_a_year_end_and_ungraded_and_rejected_tickets_do_not_count()
    {
        var weeks = JourneyWeeks.Build([Item(new DateOnly(2026, 12, 31), 1.0), Item(new DateOnly(2027, 1, 1), 1.0), Item(new DateOnly(2027, 1, 4), 1.0), Item(new DateOnly(2027, 1, 4), null), Item(new DateOnly(2027, 1, 4), 9.0, TicketGroup.Rejected)]);

        weeks.Select(w => w.IsoWeek).ShouldBe(["2026-W53", "2027-W01"]);
        weeks[0].Graded.ShouldBe(2);
        weeks[1].Graded.ShouldBe(1);
    }

    [Fact]
    public void No_items_give_no_weeks()
    {
        JourneyWeeks.Build([]).ShouldBeEmpty();
    }
}
