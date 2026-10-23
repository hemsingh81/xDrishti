using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec P7-08 on a real database: a published skeleton plan and its run row, a failed gate that leaves no plan and one failure row, a repeat that changes nothing, a dry run that writes nothing, the real gates failing closed on an empty database, and the read endpoint.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class NightlyPlanApiTests(ApiFactory factory) : DataTestBase(factory)
{
    // Friday 2099-01-02 evening; the plan is for Monday 2099-01-05.
    private static readonly DateTimeOffset Evening = new(2099, 1, 2, 13, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Friday = new(2099, 1, 2);

    private sealed class FixedGate(string id, GateStatus status) : INightlyGate
    {
        public string Id => id;

        public ValueTask<GateResult> CheckAsync(GateContext context, CancellationToken cancellationToken) => ValueTask.FromResult(new GateResult(id, status, "fixed"));
    }

    private async Task<NightlyRunView> RunAsync(RunNightlyPlanCommand command, GateStatus second = GateStatus.Pass)
    {
        using var scope = Factory.Services.CreateScope();
        var p = scope.ServiceProvider;
        var handler = new RunNightlyPlanHandler(
            p.GetRequiredService<ITradingCalendar>(),
            [new FixedGate(NightlyGateIds.Calendar, GateStatus.Pass), new FixedGate(NightlyGateIds.Eod, second)],
            new NullPlanBuilder(),
            p.GetRequiredService<IPlanStore>(),
            new FakeTimeProvider(Evening),
            NullLogger<RunNightlyPlanHandler>.Instance);
        var result = await handler.HandleAsync(command, Ct);
        return result.Value;
    }

    [Fact]
    public async Task A_green_run_stores_one_empty_skeleton_plan_in_state_published_and_one_run_row()
    {
        var view = await RunAsync(new RunNightlyPlanCommand(Friday, false, false, "test"));

        view.Outcome.ShouldBe("published", view.FailedGate + ": " + view.Reason);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plans")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_items")).ShouldBe(0);
        (await ScalarAsync<string>("SELECT basis FROM trading.plans")).ShouldStartWith("skeleton");
        (await ScalarAsync<string>("SELECT to_state FROM trading.plan_events")).ShouldBe("published");
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.approvals")).ShouldBe(0);
        (await ScalarAsync<string>("SELECT outcome FROM trading.plan_runs")).ShouldBe("published");
        (await ScalarAsync<long>("SELECT plan_id FROM trading.plan_runs")).ShouldBe(view.PlanId!.Value);
    }

    [Fact]
    public async Task A_second_run_for_the_session_changes_nothing()
    {
        var first = await RunAsync(new RunNightlyPlanCommand(Friday, false, false, "test"));

        var second = await RunAsync(new RunNightlyPlanCommand(Friday, false, true, "test"), GateStatus.Fail);

        second.Repeated.ShouldBeTrue();
        second.PlanId.ShouldBe(first.PlanId);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_runs")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plans")).ShouldBe(1);
    }

    [Theory]
    [InlineData(GateStatus.Fail)]
    [InlineData(GateStatus.Unavailable)]
    public async Task A_gate_that_does_not_pass_leaves_no_plan_and_one_failure_row(GateStatus status)
    {
        var view = await RunAsync(new RunNightlyPlanCommand(Friday, false, true, "test"), status);

        view.FailedGate.ShouldBe(NightlyGateIds.Eod);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plans")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_runs")).ShouldBe(1);
        (await ScalarAsync<string>("SELECT failed_gate FROM trading.plan_runs")).ShouldBe("eod");
        (await ScalarAsync<bool>("SELECT manual FROM trading.plan_runs")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_dry_run_writes_nothing()
    {
        var view = await RunAsync(new RunNightlyPlanCommand(Friday, true, true, "test"));

        view.Outcome.ShouldBe("would_publish");
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plans")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_runs")).ShouldBe(0);
    }

    [Fact]
    public async Task The_real_gates_fail_closed_on_an_empty_database_and_each_attempt_is_one_row()
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunNightlyPlanCommand, NightlyRunView>>();

        var first = await handler.HandleAsync(new RunNightlyPlanCommand(null, false, true, "test"), Ct);
        var second = await handler.HandleAsync(new RunNightlyPlanCommand(null, false, true, "test"), Ct);

        first.Value.Outcome.ShouldBe("failed_closed");
        second.Value.Attempt.ShouldBe(2);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plans")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_runs")).ShouldBe(2);
    }

    [Fact]
    public async Task The_runs_are_readable_over_the_api_with_their_attempt_numbers()
    {
        await RunAsync(new RunNightlyPlanCommand(Friday, false, false, "test"), GateStatus.Fail);
        await RunAsync(new RunNightlyPlanCommand(Friday, false, true, "test"));

        using var response = await Client.GetAsync(new Uri("/api/plan-runs?date=2099-01-05", UriKind.Relative), Ct);
        var runs = await response.ReadAsync<List<PlanRunView>>(Ct);

        runs.Select(r => (r.Attempt, r.Outcome)).ShouldBe([(2, "published"), (1, "failed_closed")]);
        using var bad = await Client.GetAsync(new Uri("/api/plan-runs?limit=0", UriKind.Relative), Ct);
        ((int)bad.StatusCode).ShouldBe(422);
    }
}
