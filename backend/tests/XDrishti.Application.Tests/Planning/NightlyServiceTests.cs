#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Planning;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Planning;

/// <summary>The nightly service (spec P7-08): its definition, its place after the end-of-day run and the portfolio sync, and how a pipeline result becomes a run outcome.</summary>
public sealed class NightlyServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Friday = new(2026, 10, 23);

    [Fact]
    public void It_runs_at_18_30_on_trading_days_and_starts_paused()
    {
        var d = NightlyService.Definition;

        d.Id.ShouldBe("nightly");
        d.Defaults.Enabled.ShouldBeFalse();
        d.Defaults.Schedule.ShouldBe(Schedule.Daily(new TimeOnly(18, 30)));
        d.Constraints.NotBeforeSessionEnd.ShouldBeTrue();
        d.Constraints.MustFollow.ShouldBe([EodService.Id, PortfolioBrokerHistoryService.Id]);
    }

    [Fact]
    public void Its_defaults_beside_the_services_it_follows_raise_no_schedule_issue()
    {
        var subjects = new[] { EodService.Definition, PortfolioBrokerHistoryService.Definition, NightlyService.Definition }.Select(d => new RuleSubject(d, d.Defaults)).ToList();

        ScheduleRules.Evaluate(subjects).ShouldBeEmpty();
    }

    private static async Task<ServiceOutcome> RunAsync(NightlyRunView view, ServiceRunTrigger trigger = ServiceRunTrigger.Scheduled)
    {
        var handler = Substitute.For<ICommandHandler<RunNightlyPlanCommand, NightlyRunView>>();
        handler.HandleAsync(Arg.Any<RunNightlyPlanCommand>(), Ct).Returns(Result.Success(view));
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<RunNightlyPlanCommand, NightlyRunView>)).Returns(handler);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        var context = new ServiceRunContext(Guid.NewGuid(), trigger, 1, "owner@x", new Dictionary<string, JsonElement>(), (_, _) => ValueTask.CompletedTask);

        var outcome = await new NightlyService(factory).RunAsync(context, Ct);

        return outcome;
    }

    [Fact]
    public async Task A_published_plan_is_a_success()
    {
        var outcome = await RunAsync(new NightlyRunView(Friday, Friday.AddDays(3), "published", false, false, 1, null, "Published plan 5.", 5, []));

        outcome.Status.ShouldBe(ServiceRunStatus.Succeeded);
    }

    [Fact]
    public async Task A_repeat_is_a_success_that_says_so()
    {
        var outcome = await RunAsync(new NightlyRunView(Friday, Friday.AddDays(3), "published", true, false, 1, null, "Published plan 5.", 5, []));

        outcome.Status.ShouldBe(ServiceRunStatus.Succeeded);
        outcome.Message.ShouldNotBeNull().ShouldStartWith("Already published");
    }

    [Fact]
    public async Task A_gate_that_did_not_pass_fails_the_run_with_the_gate_and_reason()
    {
        var outcome = await RunAsync(new NightlyRunView(Friday, Friday.AddDays(3), "failed_closed", false, false, 1, "eod", "no run", null, []));

        outcome.Status.ShouldBe(ServiceRunStatus.Failed);
        outcome.Message.ShouldNotBeNull().ShouldContain("eod");
        outcome.Message.ShouldContain("no run");
    }
}
