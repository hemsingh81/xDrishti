#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>P8-04 over fake stores: the join of approved lines, fill grades, plan grades and approval rows, the auto count and the range refusals.</summary>
public sealed class GetPlanVsActualHandlerTests
{
    private static readonly DateOnly Day = new(2026, 3, 10);
    private static readonly Guid Account = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At = new(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly IPlanStore _plans = Substitute.For<IPlanStore>();
    private readonly IJournalStore _journal = Substitute.For<IJournalStore>();
    private readonly IGradeStore _grades = Substitute.For<IGradeStore>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GetPlanVsActualHandlerTests()
    {
        var plan = new PlanRow(1, new NewPlan("k", Day, At, 1, new string('b', 64), "cell statistics only", false, "test", At, []), PlanState.Published, 2, 0, 0);
        _plans.ListAsync(null, null, Arg.Any<int>(), Ct).Returns([plan]);
        _plans.ItemsAsync(1, Ct).Returns([Item(11, TicketState.Closed), Item(12, TicketState.Approved)]);
        _plans.ApprovalsAsync(1, Ct).Returns(
        [
            new ApprovalRow(1, 11, "owner", "approve", "owner:me", "ok", null, null, At),
            new ApprovalRow(2, 12, "auto", "approve", "rule:autonomy", "auto", null, null, At),
        ]);
        var graded = new FillGrade(FillGrade.Graded, 100, 500.1, 510, 500, 490, 2.0, 0.01, 0.10, 1000m, 40m, 1.8, "d");
        _journal.EntriesAsync(1, Ct).Returns([new StoredEntry(1, 1, 11, Account, Day, [], graded)]);
        var triggered = new TicketGrade(GradeStatus.Graded, 1.0, null, null, "target", 500, 510, "gross", "d");
        _grades.ForPlansAsync(Arg.Any<IReadOnlyList<long>>(), Ct).Returns(new Dictionary<long, StoredGrade>
        {
            [11] = new(1, new NewGrade(1, 11, Day, triggered, null, At)),
            [12] = new(2, new NewGrade(1, 12, Day, triggered, null, At)),
        });
    }

    private static PlanItemRow Item(long id, TicketState state) =>
        new(id, 1, new NewPlanItem((int)id, TicketGroup.Primary, "NSE:X", "BUY", "ORB", new string('a', 64), "t", "s", "g", "v", [], new PlanEvidence("c", 30, "r", "b", null, null, [], null), [new NewAllocation(Account, "paper", 10)]), state);

    private GetPlanVsActualHandler Handler(int min = 15, int maxDays = 366) =>
        new(_plans, _journal, _grades, new PlanVsActualSettings { MinTickets = min, MaxRangeDays = maxDays }, NullLogger<GetPlanVsActualHandler>.Instance);

    [Fact]
    public async Task A_graded_line_and_a_line_without_fills_are_counted_and_the_missed_trigger_is_summed()
    {
        var result = await Handler(1).HandleAsync(new GetPlanVsActualQuery(null, null), Ct);

        var v = result.Value;
        v.Counts.ApprovedLines.ShouldBe(2);
        v.Counts.GradedOnFills.ShouldBe(1);
        v.Counts.WithoutFills.ShouldBe(1);
        v.Missed.Count.ShouldBe(1);
        v.Missed.TotalPlanR.Number.ShouldBe(1.0);
        v.Chain[4].Metric.Number.ShouldBe(1.8);
        v.Evidence.Basis.ShouldContain("realised fills, not simulated");
    }

    [Fact]
    public async Task An_automatic_approval_is_counted_under_auto_never_under_the_owner()
    {
        var result = await Handler().HandleAsync(new GetPlanVsActualQuery(null, null), Ct);

        result.Value.Approvals.ShouldBe([new ApprovalCountView("owner", 1), new ApprovalCountView("auto", 1)]);
        result.Value.Chain.ShouldAllBe(s => s.Metric.State == "insufficient");
    }

    [Fact]
    public async Task A_range_outside_the_plans_reads_none()
    {
        var result = await Handler().HandleAsync(new GetPlanVsActualQuery(Day.AddDays(1), Day.AddDays(5)), Ct);

        result.Value.Plans.ShouldBe(0);
        result.Value.Chain.ShouldAllBe(s => s.Metric.State == "empty");
    }

    [Fact]
    public async Task From_after_to_and_a_range_over_the_limit_are_refused()
    {
        var reversed = await Handler().HandleAsync(new GetPlanVsActualQuery(Day, Day.AddDays(-1)), Ct);
        var wide = await Handler().HandleAsync(new GetPlanVsActualQuery(Day, Day.AddDays(367)), Ct);

        reversed.Error.Code.ShouldBe("reports.invalid");
        wide.Error.Code.ShouldBe("reports.invalid");
    }
}
