#pragma warning disable CA2012 // NSubstitute setup and verification calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Planning;

/// <summary>The fail-closed nightly pipeline (spec P7-08) over fake gates and a fake store: a gate that does not pass publishes nothing and records one failure row, a repeat is a no-op, a dry run writes nothing.</summary>
public sealed class NightlyPipelineTests
{
    // Friday 2026-10-23, 18:30 India time; the next session is Monday 2026-10-26.
    private static readonly DateTimeOffset Evening = new(2026, 10, 23, 13, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Friday = new(2026, 10, 23);

    private static readonly DateOnly Monday = new(2026, 10, 26);

    private readonly string[] _ids = [NightlyGateIds.Calendar, NightlyGateIds.Eod, NightlyGateIds.PortfolioSync, NightlyGateIds.FreshData, NightlyGateIds.Quality, NightlyGateIds.SelectionInputs];

    private readonly List<PlanRunRow> _runs = [];

    private readonly List<NewPlan> _published = [];

    private readonly IPlanStore _store = Substitute.For<IPlanStore>();

    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private readonly FakeTimeProvider _time = new(Evening);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public NightlyPipelineTests()
    {
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        _store.ListRunsAsync(Arg.Any<DateOnly?>(), Arg.Any<int>(), Ct).Returns(_ => ValueTask.FromResult<IReadOnlyList<PlanRunRow>>([.. _runs.AsEnumerable().Reverse()]));
        _store.RecordRunAsync(Arg.Any<NewPlanRun>(), Ct).Returns(call =>
        {
            _runs.Add(new PlanRunRow(_runs.Count + 1, _runs.Count(r => r.Run.ForDate == call.Arg<NewPlanRun>().ForDate) + 1, call.Arg<NewPlanRun>()));
            return ValueTask.CompletedTask;
        });
        _store.PublishAsync(Arg.Any<NewPlan>(), Ct).Returns(call =>
        {
            _published.Add(call.Arg<NewPlan>());
            return ValueTask.FromResult(new PlanPublished(100 + _published.Count, true));
        });
    }

    private sealed class FakeGate(string id, GateStatus status, bool throws = false) : INightlyGate
    {
        public int Reads { get; private set; }

        public string Id => id;

        public ValueTask<GateResult> CheckAsync(GateContext context, CancellationToken cancellationToken)
        {
            Reads++;
            return throws ? throw new InvalidOperationException("secret detail") : ValueTask.FromResult(new GateResult(id, status, "because"));
        }
    }

    private List<FakeGate> Gates(int failAt = -1, GateStatus status = GateStatus.Fail, bool throws = false) =>
        [.. _ids.Select((id, i) => new FakeGate(id, i == failAt ? status : GateStatus.Pass, i == failAt && throws))];

    private RunNightlyPlanHandler Handler(IEnumerable<INightlyGate> gates, IPlanBuilder? builder = null) =>
        new(_calendar, gates, builder ?? new NullPlanBuilder(), _store, _time, NullLogger<RunNightlyPlanHandler>.Instance);

    private static RunNightlyPlanCommand Run(bool dry = false, bool manual = false) => new(null, dry, manual, "test");

    [Fact]
    public async Task All_gates_green_publishes_an_empty_skeleton_plan_and_records_one_published_row()
    {
        var result = await Handler(Gates()).HandleAsync(Run(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Outcome.ShouldBe("published");
        (result.Value.Session, result.Value.ForDate, result.Value.Attempt).ShouldBe((Friday, Monday, 1));
        var plan = _published.ShouldHaveSingleItem();
        plan.Items.ShouldBeEmpty();
        plan.Basis.ShouldStartWith("skeleton");
        plan.Provisional.ShouldBeTrue();
        plan.ReviewCutoff.ShouldBe(new DateTimeOffset(2026, 10, 26, 9, 5, 0, TimeSpan.FromMinutes(330)));
        var row = _runs.ShouldHaveSingleItem().Run;
        (row.Outcome, row.FailedGate, row.PlanId, row.Manual).ShouldBe(("published", null, 101L, false));
    }

    [Theory]
    [InlineData(0, GateStatus.Fail)]
    [InlineData(1, GateStatus.Fail)]
    [InlineData(2, GateStatus.Fail)]
    [InlineData(3, GateStatus.Fail)]
    [InlineData(4, GateStatus.Fail)]
    [InlineData(5, GateStatus.Fail)]
    [InlineData(0, GateStatus.Unavailable)]
    [InlineData(3, GateStatus.Unavailable)]
    [InlineData(5, GateStatus.Unavailable)]
    public async Task A_gate_that_does_not_pass_publishes_no_plan_and_records_exactly_one_failure_row_naming_it(int failAt, GateStatus status)
    {
        var gates = Gates(failAt, status);

        var result = await Handler(gates).HandleAsync(Run(), Ct);

        result.Value.Outcome.ShouldBe("failed_closed");
        result.Value.FailedGate.ShouldBe(_ids[failAt]);
        _published.ShouldBeEmpty();
        await _store.DidNotReceive().PublishAsync(Arg.Any<NewPlan>(), Ct);
        var row = _runs.ShouldHaveSingleItem().Run;
        (row.Outcome, row.FailedGate, row.PlanId, row.Reason).ShouldBe(("failed_closed", _ids[failAt], null, "because"));
        gates.Skip(failAt + 1).ShouldAllBe(g => g.Reads == 0); // a real run stops at the first gate that does not pass
    }

    [Fact]
    public async Task A_gate_that_throws_is_unavailable_and_blocks_without_leaking_the_error_text()
    {
        var result = await Handler(Gates(2, throws: true)).HandleAsync(Run(), Ct);

        result.Value.FailedGate.ShouldBe(NightlyGateIds.PortfolioSync);
        _published.ShouldBeEmpty();
        var row = _runs.ShouldHaveSingleItem().Run;
        row.Reason.ShouldContain("could not be read");
        row.Reason.ShouldNotContain("secret");
        result.Value.Gates[^1].Status.ShouldBe("unavailable");
    }

    [Fact]
    public async Task A_second_run_after_a_published_plan_is_a_no_op_returning_the_first_result()
    {
        var first = await Handler(Gates()).HandleAsync(Run(), Ct);
        var again = Gates();

        var second = await Handler(again).HandleAsync(Run(manual: true), Ct);

        second.Value.Repeated.ShouldBeTrue();
        (second.Value.Outcome, second.Value.PlanId, second.Value.Attempt).ShouldBe((first.Value.Outcome, first.Value.PlanId, first.Value.Attempt));
        _runs.Count.ShouldBe(1);
        _published.Count.ShouldBe(1);
        again.ShouldAllBe(g => g.Reads == 0);
    }

    [Fact]
    public async Task A_run_after_a_failed_one_is_a_new_attempt_that_can_publish()
    {
        await Handler(Gates(1)).HandleAsync(Run(), Ct);

        var retry = await Handler(Gates()).HandleAsync(Run(manual: true), Ct);

        retry.Value.Outcome.ShouldBe("published");
        retry.Value.Attempt.ShouldBe(2);
        _runs.Select(r => (r.Attempt, r.Run.Outcome, r.Run.Manual)).ShouldBe([(1, "failed_closed", false), (2, "published", true)]);
        (await Handler(Gates(1)).HandleAsync(Run(), Ct)).Value.Repeated.ShouldBeTrue(); // and now the session is done
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task A_dry_run_writes_nothing_and_reads_every_gate(int failAt)
    {
        var gates = Gates(failAt);

        var result = await Handler(gates).HandleAsync(Run(dry: true), Ct);

        result.Value.DryRun.ShouldBeTrue();
        result.Value.Outcome.ShouldBe(failAt < 0 ? "would_publish" : "failed_closed");
        result.Value.Gates.Count.ShouldBe(6);
        gates.ShouldAllBe(g => g.Reads == 1);
        _runs.ShouldBeEmpty();
        _published.ShouldBeEmpty();
        await _store.DidNotReceive().RecordRunAsync(Arg.Any<NewPlanRun>(), Ct);
        await _store.DidNotReceive().PublishAsync(Arg.Any<NewPlan>(), Ct);
    }

    [Fact]
    public async Task A_builder_that_refuses_fails_closed_under_its_own_id_and_publishes_nothing()
    {
        var builder = Substitute.For<IPlanBuilder>();
        builder.BuildAsync(Arg.Any<PlanBuildRequest>(), Ct).Returns(Result.Failure<NewPlan>(Error.Validation("x", "no candidates source")));

        var result = await Handler(Gates(), builder).HandleAsync(Run(), Ct);

        result.Value.FailedGate.ShouldBe(NightlyGateIds.Builder);
        _published.ShouldBeEmpty();
        _runs.ShouldHaveSingleItem().Run.FailedGate.ShouldBe(NightlyGateIds.Builder);
    }

    [Fact]
    public async Task A_builder_that_throws_fails_closed()
    {
        var builder = Substitute.For<IPlanBuilder>();
        builder.BuildAsync(Arg.Any<PlanBuildRequest>(), Ct).Returns<ValueTask<Result<NewPlan>>>(_ => throw new InvalidOperationException("boom"));

        var result = await Handler(Gates(), builder).HandleAsync(Run(), Ct);

        result.Value.FailedGate.ShouldBe(NightlyGateIds.Builder);
        _published.ShouldBeEmpty();
        _runs.ShouldHaveSingleItem().Run.Reason.ShouldNotContain("boom");
    }

    [Fact]
    public async Task A_store_that_fails_to_publish_is_recorded_as_a_failed_closed_run()
    {
        _store.PublishAsync(Arg.Any<NewPlan>(), Ct).Returns<ValueTask<PlanPublished>>(_ => throw new InvalidOperationException("db down"));

        var result = await Handler(Gates()).HandleAsync(Run(), Ct);

        result.Value.FailedGate.ShouldBe(NightlyGateIds.Publish);
        _runs.ShouldHaveSingleItem().Run.PlanId.ShouldBeNull();
    }

    [Fact]
    public async Task A_manual_run_is_marked_manual_in_its_row()
    {
        await Handler(Gates(0)).HandleAsync(Run(manual: true), Ct);

        _runs.ShouldHaveSingleItem().Run.Manual.ShouldBeTrue();
    }

    [Fact]
    public async Task An_explicit_date_plans_from_that_session()
    {
        var result = await Handler(Gates()).HandleAsync(new RunNightlyPlanCommand(new DateOnly(2026, 10, 22), false, true, "test"), Ct);

        (result.Value.Session, result.Value.ForDate).ShouldBe((new DateOnly(2026, 10, 22), Friday));
    }
}
