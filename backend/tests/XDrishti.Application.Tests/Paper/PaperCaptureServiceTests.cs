#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Paper;

/// <summary>Spec P10-01 criterion 12: the service starts paused, follows the end-of-day run, starts not before 16:30 and reports a failed capture with its reason.</summary>
public sealed class PaperCaptureServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_service_starts_paused_runs_at_1700_after_eod_and_not_before_1630()
    {
        var d = PaperCaptureService.Definition;

        d.Id.ShouldBe("paper-capture");
        d.Defaults.Enabled.ShouldBeFalse();
        d.Defaults.Schedule.ShouldBe(Schedule.Daily(new TimeOnly(17, 0)));
        d.Constraints.MustFollow.ShouldBe([EodService.Id]);
        d.Constraints.StartNotBefore.ShouldBe(new TimeOnly(16, 30));
    }

    [Fact]
    public void Its_defaults_beside_the_end_of_day_run_raise_no_schedule_issue()
    {
        var subjects = new[] { EodService.Definition, PaperCaptureService.Definition }.Select(d => new RuleSubject(d, d.Defaults)).ToList();

        ScheduleRules.Evaluate(subjects).ShouldBeEmpty();
    }

    private static async Task<ServiceOutcome> RunAsync(PaperCaptureOutcome outcome)
    {
        var handler = Substitute.For<ICommandHandler<CapturePaperFillsCommand, PaperCaptureOutcome>>();
        handler.HandleAsync(Arg.Any<CapturePaperFillsCommand>(), Ct).Returns(Result.Success(outcome));
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<CapturePaperFillsCommand, PaperCaptureOutcome>)).Returns(handler);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        var context = new ServiceRunContext(Guid.NewGuid(), ServiceRunTrigger.Scheduled, 1, "system", new Dictionary<string, JsonElement>(), (_, _) => ValueTask.CompletedTask);

        return await new PaperCaptureService(factory).RunAsync(context, Ct);
    }

    [Fact]
    public async Task A_session_that_failed_makes_the_run_fail_and_a_clean_capture_succeeds()
    {
        (await RunAsync(new PaperCaptureOutcome(1, 0, 0, 0, 0, 0, 0, 1))).Status.ShouldBe(ServiceRunStatus.Failed);
        (await RunAsync(new PaperCaptureOutcome(1, 1, 1, 1, 0, 0, 0, 0))).Status.ShouldBe(ServiceRunStatus.Succeeded);
    }
}
