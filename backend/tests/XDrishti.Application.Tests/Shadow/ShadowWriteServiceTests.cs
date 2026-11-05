using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Planning;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>CF-08: the service starts paused, follows the nightly plan, and reports what the handler did.</summary>
public sealed class ShadowWriteServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_service_starts_paused_runs_at_1900_after_the_nightly_plan_and_raises_no_schedule_issue()
    {
        var d = ShadowWriteService.Definition;

        d.Id.ShouldBe("shadow-write");
        d.Defaults.Enabled.ShouldBeFalse();
        d.Defaults.Schedule.ShouldBe(Schedule.Daily(new TimeOnly(19, 0)));
        d.Constraints.MustFollow.ShouldBe([NightlyService.Id]);
        d.Constraints.StartNotBefore.ShouldBe(new TimeOnly(18, 30));
        var subjects = new[] { NightlyService.Definition, ShadowWriteService.Definition }.Select(x => new RuleSubject(x, x.Defaults)).ToList();

        ScheduleRules.Evaluate(subjects).ShouldBeEmpty();
    }

    private static async Task<ServiceOutcome> RunAsync(Result<ShadowWriteOutcome> result)
    {
        var handler = Substitute.For<ICommandHandler<WriteShadowCommand, ShadowWriteOutcome>>();
        handler.HandleAsync(Arg.Any<WriteShadowCommand>(), Ct).Returns(result);
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<WriteShadowCommand, ShadowWriteOutcome>)).Returns(handler);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        var context = new ServiceRunContext(Guid.NewGuid(), ServiceRunTrigger.Scheduled, 1, "system", new Dictionary<string, JsonElement>(), (_, _) => ValueTask.CompletedTask);

        return await new ShadowWriteService(factory).RunAsync(context, Ct);
    }

    [Fact]
    public async Task A_run_reports_its_counts_and_a_switched_off_run_says_so_and_still_succeeds()
    {
        var written = await RunAsync(Result.Success(new ShadowWriteOutcome(true, 1, 7, 6, 0, 0)));
        var off = await RunAsync(Result.Success(new ShadowWriteOutcome(false, 0, 0, 0, 0, 0)));

        written.Status.ShouldBe(ServiceRunStatus.Succeeded);
        written.Message.ShouldNotBeNull().ShouldContain("7 row(s)");
        off.Status.ShouldBe(ServiceRunStatus.Succeeded);
        off.Message.ShouldNotBeNull().ShouldContain("Switched off");
    }

    [Fact]
    public async Task A_refused_run_fails_with_its_reason()
    {
        var failed = await RunAsync(Result.Failure<ShadowWriteOutcome>(Error.Validation("paper.invalid", "no")));

        failed.Status.ShouldBe(ServiceRunStatus.Failed);
    }

    [Fact]
    public void The_grading_service_starts_paused_follows_the_end_of_day_run_and_raises_no_schedule_issue()
    {
        var d = ShadowGradeService.Definition;

        d.Id.ShouldBe("shadow-grade");
        d.Defaults.Enabled.ShouldBeFalse();
        d.Defaults.Schedule.ShouldBe(Schedule.Daily(new TimeOnly(17, 30)));
        d.Constraints.MustFollow.ShouldBe([XDrishti.Application.Market.Bars.Eod.EodService.Id]);
        var subjects = new[] { XDrishti.Application.Market.Bars.Eod.EodService.Definition, ShadowGradeService.Definition }.Select(x => new RuleSubject(x, x.Defaults)).ToList();

        ScheduleRules.Evaluate(subjects).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_grading_run_reports_its_counts_and_fails_when_a_candidate_failed()
    {
        var handler = Substitute.For<ICommandHandler<GradeShadowCommand, ShadowGradeOutcome>>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<GradeShadowCommand, ShadowGradeOutcome>)).Returns(handler);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        var context = new ServiceRunContext(Guid.NewGuid(), ServiceRunTrigger.Scheduled, 1, "system", new Dictionary<string, JsonElement>(), (_, _) => ValueTask.CompletedTask);
        handler.HandleAsync(Arg.Any<GradeShadowCommand>(), Ct).Returns(Result.Success(new ShadowGradeOutcome(true, 5, 4, 0, 0, 1, 0)), Result.Success(new ShadowGradeOutcome(true, 5, 3, 0, 0, 1, 1)), Result.Success(new ShadowGradeOutcome(false, 0, 0, 0, 0, 0, 0)));
        var service = new ShadowGradeService(factory);

        var ok = await service.RunAsync(context, Ct);
        var failed = await service.RunAsync(context, Ct);
        var off = await service.RunAsync(context, Ct);

        ok.Status.ShouldBe(ServiceRunStatus.Succeeded);
        ok.Message.ShouldNotBeNull().ShouldContain("4 graded");
        failed.Status.ShouldBe(ServiceRunStatus.Failed);
        off.Status.ShouldBe(ServiceRunStatus.Succeeded);
        off.Message.ShouldNotBeNull().ShouldContain("Switched off");
    }
}
