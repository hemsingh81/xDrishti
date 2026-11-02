#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Planning;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Tests.Planning;

/// <summary>
/// P15-06 criteria 1, 5, 6 and 10 through the real selection builder with fake ports: with the switch OFF (or no filter registered) the plan is what it was and the calendar is not asked; with it ON, <c>ExcludedByEvent</c> and the checklist days come from the calendar, an unavailable answer is
/// held (never passed), the output prints N and the coverage, and a calendar that cannot be read makes every answer unavailable instead of throwing into the plan.
/// </summary>
public sealed class EventFilterPlanTests
{
    private static readonly SelectionRules EventRequired = new(OptionalFilters: [FilterCodes.Cost, FilterCodes.FnoBan, FilterCodes.Surveillance]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ResultsAnswer Days(int days) => new(ResultsAnswer.Value, days, null, null, null, null);

    private static ResultsAnswer None => ResultsAnswer.Missing(ResultsAnswer.NoEntry);

    private static (SelectionPlanBuilder Builder, IResultsCalendar Calendar, IReadOnlyList<TicketCandidate> Found) Build(EventFilterState state, Dictionary<string, ResultsAnswer>? answers = null, bool withFilter = true, bool calendarThrows = false, SelectionRules? rules = null)
    {
        var found = WiredPlanFixture.Candidates();
        var source = Substitute.For<ITicketSource>();
        source.ReadAsync(WiredPlanFixture.Session, WiredPlanFixture.ForDate, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new TicketSourceResult(found, 1, 2, ["history not ready at the close"]))));
        var context = Substitute.For<IPlanContextSource>();
        context.ReadAsync(Arg.Any<PlanContextRequest>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(WiredPlanFixture.Context())));
        var calendar = Substitute.For<IResultsCalendar>();
        if (calendarThrows)
        {
            calendar.DaysToResults(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("the store is away"));
        }
        else
        {
            calendar.DaysToResults(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(call => ValueTask.FromResult<IReadOnlyDictionary<Guid, ResultsAnswer>>(found.Where(c => answers is not null && answers.ContainsKey(c.Draft.Symbol)).ToDictionary(c => c.InstrumentId!.Value, c => answers![c.Draft.Symbol])));
        }

        var switchState = Substitute.For<IEventFilterSwitch>();
        switchState.CurrentAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(state));
        var filter = withFilter ? new PlanEventFilter(switchState, calendar, NullLogger<PlanEventFilter>.Instance) : null;
        return (new SelectionPlanBuilder(source, context, NullLogger<SelectionPlanBuilder>.Instance, rules ?? EventRequired, null, null, filter), calendar, found);
    }

    private static Dictionary<string, ResultsAnswer> Table(params (string Symbol, ResultsAnswer Answer)[] rows) => rows.ToDictionary(r => r.Symbol, r => r.Answer);

    [Fact]
    public async Task With_the_switch_off_the_plan_is_exactly_the_plan_of_a_builder_with_no_filter_and_the_calendar_is_not_asked()
    {
        var off = Build(EventFilterState.Off);
        var none = Build(EventFilterState.Off, withFilter: false);

        var withSwitch = (await off.Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;
        var without = (await none.Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        withSwitch.InputHash.ShouldBe(without.InputHash);
        withSwitch.Basis.ShouldBe(without.Basis);
        withSwitch.Items.SelectMany(i => i.Evidence.Unavailable).ShouldContain("events");
        withSwitch.Items.SelectMany(i => i.Reasons).ShouldAllBe(r => !r.StartsWith("event", StringComparison.Ordinal) || r == "event:unavailable");
        await off.Calendar.DidNotReceiveWithAnyArgs().DaysToResults(default(IReadOnlyCollection<Guid>)!, default, default);
    }

    [Fact]
    public async Task With_the_switch_on_a_candidate_inside_the_window_is_excluded_one_outside_is_kept_and_one_with_no_entry_is_held_not_passed()
    {
        var table = Table(("AAA", Days(1)), ("BBB", Days(3)), ("CCC", Days(2)), ("DDD", Days(0)), ("FFF", None), ("EEE", Days(9)));
        var built = Build(new EventFilterState(true, 2, "manual", "owner"), table);

        var plan = (await built.Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        string[] Reasons(string symbol) => [.. plan.Items.Single(i => i.Symbol == symbol).Reasons];
        Reasons("AAA").ShouldContain("event:fail");
        Reasons("CCC").ShouldContain("event:fail", "the window is inclusive: 2 sessions is inside N=2");
        Reasons("DDD").ShouldContain("event:fail");
        Reasons("FFF").ShouldContain("event:unavailable");
        plan.Items.Single(i => i.Symbol == "FFF").Group.ShouldBe(TicketGroup.Rejected);
        plan.Items.Single(i => i.Symbol == "BBB").Reasons.ShouldNotContain(r => r.StartsWith("event", StringComparison.Ordinal));
        plan.Items.Where(i => i.Group == TicketGroup.Primary).Select(i => i.Symbol).ShouldBe(["BBB"]);
        plan.Items.SelectMany(i => i.Evidence.Unavailable).ShouldNotContain("events", "the filter ran, so it is not listed as not applied");
    }

    [Fact]
    public async Task A_candidate_with_no_entry_stays_a_printed_caveat_only_when_the_owner_listed_event_as_optional()
    {
        var table = Table(("AAA", None), ("BBB", Days(8)), ("CCC", Days(8)), ("DDD", Days(8)), ("FFF", Days(8)), ("EEE", Days(8)));
        var optional = new SelectionRules(OptionalFilters: [FilterCodes.Cost, FilterCodes.Event, FilterCodes.FnoBan, FilterCodes.Surveillance]);

        var plan = (await Build(new EventFilterState(true, 2, "manual", "owner"), table, rules: optional).Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        plan.Items.Single(i => i.Symbol == "AAA").Group.ShouldNotBe(TicketGroup.Rejected, "optional: the unavailable answer is a caveat, not a hold");
    }

    [Fact]
    public async Task The_output_prints_n_and_the_coverage_and_the_unavailable_count()
    {
        var table = Table(("AAA", Days(5)), ("BBB", Days(6)), ("CCC", None), ("DDD", Days(7)));

        var plan = (await Build(new EventFilterState(true, 2, "manual", "owner"), table).Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        plan.Basis.ShouldContain("events: N=2, 3 of 6 instruments with an entry, 3 unavailable.");
        plan.Basis.Length.ShouldBeLessThanOrEqualTo(SelectionPlanBuilder.MaxBasisLength);
    }

    [Fact]
    public async Task An_instrument_whose_last_meeting_has_passed_and_whose_next_is_not_announced_is_held_and_counted_on_its_own_never_guessed()
    {
        var table = Table(("AAA", Days(5)), ("BBB", Days(6)), ("CCC", ResultsAnswer.Missing(ResultsAnswer.NoUpcomingEntry)), ("DDD", ResultsAnswer.Missing(ResultsAnswer.NoUpcomingEntry)), ("FFF", None));

        var plan = (await Build(new EventFilterState(true, 2, "manual", "owner"), table).Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        plan.Basis.ShouldContain("events: N=2, 2 of 6 instruments with an entry, 4 unavailable (2 of them with no upcoming meeting announced yet).");
        plan.Items.Single(i => i.Symbol == "CCC").Reasons.ShouldContain("event:unavailable");
        plan.Items.Single(i => i.Symbol == "CCC").Group.ShouldBe(TicketGroup.Rejected);
    }

    [Fact]
    public async Task A_rebuild_after_the_plan_date_asks_at_the_last_instant_of_that_date_not_later()
    {
        var built = Build(new EventFilterState(true, 2, "manual", "owner"));
        DateTimeOffset? asked = null;
        built.Calendar.DaysToResults(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Do<DateTimeOffset>(t => asked = t), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyDictionary<Guid, ResultsAnswer>>(new Dictionary<Guid, ResultsAnswer>()));
        var late = WiredPlanFixture.Request with { Now = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero) };

        await built.Builder.BuildAsync(late, Ct);

        asked.ShouldBe(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.FromMinutes(330)).AddTicks(-10));
    }

    [Fact]
    public async Task The_plan_check_gets_the_days_from_the_calendar_and_the_rule_and_the_limit_are_unchanged()
    {
        var table = Table(("AAA", Days(3)), ("BBB", Days(4)), ("CCC", None), ("DDD", Days(20)), ("FFF", Days(20)), ("EEE", Days(20)));
        var window0 = new EventFilterState(true, 0, "manual", "owner");   // N = 0 so that AAA (3 days) is not excluded and reaches the checklist

        var plan = (await Build(window0, table).Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        var aaa = plan.Items.Single(i => i.Symbol == "AAA").Evidence.Unavailable;
        var bbb = plan.Items.Single(i => i.Symbol == "BBB").Evidence.Unavailable;
        aaa.ShouldContain("checklist:" + ChecklistCodes.ResultsWithinDays + ":fail");     // 3 is not above the limit of 3
        bbb.ShouldNotContain("checklist:" + ChecklistCodes.ResultsWithinDays + ":fail");   // 4 passes
        bbb.ShouldNotContain("checklist:" + ChecklistCodes.ResultsWithinDays + ":unavailable");
    }

    [Fact]
    public async Task With_the_switch_off_the_plan_check_stays_unavailable_as_before()
    {
        var plan = (await Build(EventFilterState.Off, rules: WiredPlanFixture.Loose).Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        plan.Items.First(i => i.Group == TicketGroup.Primary).Evidence.Unavailable.ShouldContain("checklist:" + ChecklistCodes.ResultsWithinDays + ":unavailable");
    }

    [Fact]
    public async Task A_calendar_that_cannot_be_read_makes_every_answer_unavailable_and_throws_nothing_into_the_plan()
    {
        var built = Build(new EventFilterState(true, 2, "manual", "owner"), calendarThrows: true);

        var result = await built.Builder.BuildAsync(WiredPlanFixture.Request, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldAllBe(i => i.Group == TicketGroup.Rejected, "every required event filter is unavailable: all held, none passed");
        result.Value.Items.SelectMany(i => i.Reasons).ShouldContain("event:unavailable");
        result.Value.Basis.ShouldContain("events: the results calendar could not be read");
    }

    [Fact]
    public async Task A_cancelled_build_is_not_swallowed_by_the_calendar_guard()
    {
        var built = Build(new EventFilterState(true, 2, "manual", "owner"));
        built.Calendar.DaysToResults(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(async () => await built.Builder.BuildAsync(WiredPlanFixture.Request, Ct));
    }

    [Fact]
    public async Task The_question_is_asked_at_the_build_instant_or_the_start_of_the_plan_date_whichever_is_later()
    {
        var built = Build(new EventFilterState(true, 2, "manual", "owner"));
        DateTimeOffset? asked = null;
        built.Calendar.DaysToResults(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Do<DateTimeOffset>(t => asked = t), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyDictionary<Guid, ResultsAnswer>>(new Dictionary<Guid, ResultsAnswer>()));

        await built.Builder.BuildAsync(WiredPlanFixture.Request, Ct);

        asked.ShouldBe(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromMinutes(330)), "the plan is built on the Friday evening for Monday: sessions are counted from Monday's start");
    }
}
