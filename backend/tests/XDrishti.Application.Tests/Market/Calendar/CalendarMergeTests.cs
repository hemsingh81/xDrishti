using XDrishti.Application.Market.Calendar;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Calendar;

/// <summary>How the sources meet: manual over observed over NSE, never a confirmation by the observed rule, the owner's dismissal sticks (ADR 0017 §1).</summary>
public sealed class CalendarMergeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly Date = new(2026, 10, 2);

    private readonly InMemoryCalendarData _data = new();

    private static ObservedProposal Holiday(DateOnly? date = null) => new(date ?? Date, CalendarDayKind.Holiday, null, null, "seen");

    private static ObservedProposal Special(DateOnly? date = null) => new(date ?? Date, CalendarDayKind.Special, 1095, 1155, "seen");

    private static TradingDay Row(CalendarDayKind kind, CalendarSource source, CalendarStatus status, DateOnly? date = null) =>
        kind == CalendarDayKind.Holiday
            ? TradingDay.Holiday(date ?? Date, source, status, "old", Now.AddDays(-1))
            : TradingDay.Special(date ?? Date, 1095, 1155, source, status, "old", Now.AddDays(-1));

    private MergeCounts Observed(IReadOnlyList<ObservedProposal> proposals, params TradingDay[] existing)
    {
        _data.Days.AddRange(existing);
        var counts = CalendarMerge.ApplyObserved(proposals, existing, _data, Now);
        _data.SaveAsync(TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult();
        return counts;
    }

    private MergeCounts Nse(IReadOnlyList<ListedHoliday> holidays, params TradingDay[] existing)
    {
        _data.Days.AddRange(existing);
        var counts = CalendarMerge.ApplyNse(holidays, existing, _data, Now);
        _data.SaveAsync(TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult();
        return counts;
    }

    [Fact]
    public void A_new_observed_day_is_proposed_never_confirmed()
    {
        var counts = Observed([Holiday(), Special(new DateOnly(2026, 11, 8))]);

        counts.ShouldBe(new MergeCounts(2, 0, 0, 0, 0));
        _data.Days.ShouldAllBe(d => d.Status == CalendarStatus.Proposed && d.Source == CalendarSource.Observed);
        _data.Days.Single(d => d.Kind == CalendarDayKind.Special).Window.ShouldBe((1095, 1155));
    }

    [Fact]
    public void An_observed_proposal_is_refreshed_when_the_evidence_changes_and_left_alone_when_it_does_not()
    {
        var existing = Row(CalendarDayKind.Holiday, CalendarSource.Observed, CalendarStatus.Proposed);

        var same = Observed([new ObservedProposal(Date, CalendarDayKind.Holiday, null, null, "old")], existing);
        var changed = Observed([Special()], existing);

        same.ShouldBe(new MergeCounts(0, 0, 0, 1, 0));
        changed.ShouldBe(new MergeCounts(0, 1, 0, 0, 0));
        (existing.Kind, existing.Window, existing.Status, existing.Note).ShouldBe((CalendarDayKind.Special, (1095, 1155), CalendarStatus.Proposed, "seen"));
    }

    [Fact]
    public void A_proposal_the_bars_no_longer_support_is_withdrawn()
    {
        var stale = Row(CalendarDayKind.Holiday, CalendarSource.Observed, CalendarStatus.Proposed);

        var counts = Observed([], stale);

        counts.Removed.ShouldBe(1);
        _data.Days.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(CalendarSource.Manual, CalendarStatus.Confirmed)]
    [InlineData(CalendarSource.Observed, CalendarStatus.Confirmed)] // the owner confirmed an observed day
    [InlineData(CalendarSource.Nse, CalendarStatus.Dismissed)]
    [InlineData(CalendarSource.Observed, CalendarStatus.Dismissed)]
    public void The_owners_decisions_are_never_touched_or_withdrawn(CalendarSource source, CalendarStatus status)
    {
        var existing = Row(CalendarDayKind.Special, source, status);

        var withProposal = Observed([Holiday()], existing);
        var withoutProposal = Observed([], existing);

        withProposal.ShouldBe(new MergeCounts(0, 0, 0, 0, 1));
        withoutProposal.Removed.ShouldBe(0);
        (existing.Kind, existing.Source, existing.Status, existing.Note).ShouldBe((CalendarDayKind.Special, source, status, "old"));
    }

    [Fact]
    public void The_observed_rule_does_not_withdraw_a_manual_row_even_when_it_has_no_evidence()
    {
        var manual = Row(CalendarDayKind.Holiday, CalendarSource.Manual, CalendarStatus.Confirmed);

        Observed([], manual).Removed.ShouldBe(0);

        _data.Days.ShouldContain(manual);
    }

    [Fact]
    public void An_nse_row_that_the_bars_agree_with_stays_and_one_they_contradict_stays_in_force_with_the_proposal_next_to_it()
    {
        var agreed = Row(CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed, new DateOnly(2026, 10, 2));
        var contradicted = Row(CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed, new DateOnly(2024, 11, 1));

        var counts = Observed([Holiday(new DateOnly(2026, 10, 2)), Special(new DateOnly(2024, 11, 1))], agreed, contradicted);

        counts.ShouldBe(new MergeCounts(0, 1, 0, 1, 0));
        (agreed.Source, agreed.Status, agreed.HasPendingProposal).ShouldBe((CalendarSource.Nse, CalendarStatus.Confirmed, false));
        (contradicted.Kind, contradicted.Source, contradicted.Status).ShouldBe((CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed));
        (contradicted.ProposedKind, contradicted.ProposedWindow, contradicted.HasPendingProposal).ShouldBe((CalendarDayKind.Special, (1095, 1155), true));
        new TradingCalendar(_data.Days).IsTradingDay(contradicted.Id).ShouldBeFalse(); // the listed holiday still decides
    }

    [Fact]
    public void A_moved_holiday_a_regular_session_on_a_listed_holiday_is_proposed_next_to_it_too()
    {
        var listed = Row(CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed, new DateOnly(2026, 10, 2));

        Observed([new ObservedProposal(listed.Id, CalendarDayKind.Special, 555, 930, "seen")], listed);

        (listed.Kind, listed.ProposedKind, listed.ProposedWindow).ShouldBe((CalendarDayKind.Holiday, CalendarDayKind.Special, (555, 930)));
    }

    [Fact]
    public void The_proposal_next_to_a_confirmed_day_is_refreshed_left_alone_and_withdrawn_when_the_bars_stop_supporting_it()
    {
        var listed = Row(CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed);

        var first = Observed([Special()], listed);
        var again = Observed([Special()], listed);
        var gone = Observed([], listed);

        (first.Updated, again.Unchanged, gone.Removed).ShouldBe((1, 1, 1));
        (listed.HasPendingProposal, listed.ProposalStatus, listed.Status, listed.Kind).ShouldBe((false, (CalendarStatus?)null, CalendarStatus.Confirmed, CalendarDayKind.Holiday));
    }

    [Fact]
    public void A_proposal_the_owner_dismissed_next_to_a_confirmed_day_is_not_made_again()
    {
        var listed = Row(CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed);
        listed.Propose(CalendarDayKind.Special, (1095, 1155), "seen", Now);
        listed.DismissProposal(Now);

        var counts = Observed([Special()], listed);
        var withdrawn = Observed([], listed);

        counts.ShouldBe(new MergeCounts(0, 0, 0, 0, 1));
        withdrawn.Removed.ShouldBe(0);
        listed.ProposalStatus.ShouldBe(CalendarStatus.Dismissed);
    }

    [Fact]
    public void A_proposal_next_to_a_confirmed_day_that_the_bars_now_agree_with_is_cleared()
    {
        var listed = Row(CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed);
        listed.Propose(CalendarDayKind.Special, (1095, 1155), "seen", Now);

        var counts = Observed([Holiday()], listed);

        counts.ShouldBe(new MergeCounts(0, 0, 1, 0, 0));
        listed.HasPendingProposal.ShouldBeFalse();
    }

    [Fact]
    public void A_new_nse_holiday_is_confirmed_with_its_description()
    {
        var counts = Nse([new ListedHoliday(Date, "Gandhi Jayanti"), new ListedHoliday(new DateOnly(2026, 11, 9), string.Empty)]);

        counts.ShouldBe(new MergeCounts(2, 0, 0, 0, 0));
        _data.Days.Select(d => (d.Id, d.Source, d.Status, d.Note)).ShouldBe([(Date, CalendarSource.Nse, CalendarStatus.Confirmed, "Gandhi Jayanti"), (new DateOnly(2026, 11, 9), CalendarSource.Nse, CalendarStatus.Confirmed, "Listed by NSE")], ignoreOrder: true);
    }

    [Fact]
    public void Nse_refreshes_its_own_rows_and_reports_the_unchanged_ones()
    {
        var existing = Row(CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed);
        var other = Row(CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Confirmed, new DateOnly(2026, 11, 9));

        var counts = Nse([new ListedHoliday(Date, "renamed"), new ListedHoliday(new DateOnly(2026, 11, 9), "old")], existing, other);

        counts.ShouldBe(new MergeCounts(0, 1, 0, 1, 0));
        existing.Note.ShouldBe("renamed");
    }

    [Fact]
    public void Nse_confirms_an_observed_holiday_proposal_it_agrees_with()
    {
        var proposal = Row(CalendarDayKind.Holiday, CalendarSource.Observed, CalendarStatus.Proposed);

        var counts = Nse([new ListedHoliday(Date, "Gandhi Jayanti")], proposal);

        counts.Updated.ShouldBe(1);
        (proposal.Source, proposal.Status, proposal.Note).ShouldBe((CalendarSource.Nse, CalendarStatus.Confirmed, "Gandhi Jayanti"));
    }

    [Theory]
    [InlineData(CalendarDayKind.Holiday, CalendarSource.Manual, CalendarStatus.Confirmed)]
    [InlineData(CalendarDayKind.Holiday, CalendarSource.Observed, CalendarStatus.Dismissed)]
    [InlineData(CalendarDayKind.Holiday, CalendarSource.Nse, CalendarStatus.Dismissed)]
    [InlineData(CalendarDayKind.Holiday, CalendarSource.Observed, CalendarStatus.Confirmed)]
    [InlineData(CalendarDayKind.Special, CalendarSource.Observed, CalendarStatus.Proposed)] // the bars saw a session on a day NSE lists as closed
    [InlineData(CalendarDayKind.Special, CalendarSource.Manual, CalendarStatus.Confirmed)]
    public void Nse_leaves_what_the_owner_or_the_bars_decided(CalendarDayKind kind, CalendarSource source, CalendarStatus status)
    {
        var existing = Row(kind, source, status);

        var counts = Nse([new ListedHoliday(Date, "Gandhi Jayanti")], existing);

        counts.ShouldBe(new MergeCounts(0, 0, 0, 0, 1));
        (existing.Kind, existing.Source, existing.Status, existing.Note).ShouldBe((kind, source, status, "old"));
    }
}
