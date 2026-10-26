#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Learning;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Paper;

/// <summary>Spec P10-03: the paper-review service starts paused, runs Saturday 10:30 after the weekly digest and only builds and stores the review.</summary>
public sealed class PaperReviewServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void It_starts_paused_runs_on_saturday_at_1030_and_follows_the_weekly_digest()
    {
        var d = PaperReviewService.Definition;

        d.Id.ShouldBe("paper-review");
        d.Defaults.Enabled.ShouldBeFalse();
        d.Defaults.Schedule.ShouldBe(Schedule.Weekly(DayOfWeek.Saturday, new TimeOnly(10, 30)));
        d.Constraints.MustFollow.ShouldBe([WeeklyService.Id]);
        new PaperReviewService(Substitute.For<IServiceScopeFactory>()).FirstStartSettings().ShouldBeNull();
    }

    [Fact]
    public async Task A_run_builds_and_stores_the_current_review_as_the_service_and_nothing_else()
    {
        var handler = Substitute.For<ICommandHandler<BuildPaperReviewCommand, PaperReviewView>>();
        var view = new PaperReviewView(5, "2026-W48", 7, DateTimeOffset.UnixEpoch, "system:paper-review", true, false, "t", []);
        handler.HandleAsync(Arg.Any<BuildPaperReviewCommand>(), Ct).Returns(Result.Success(view));
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<BuildPaperReviewCommand, PaperReviewView>)).Returns(handler);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        var context = new ServiceRunContext(Guid.NewGuid(), ServiceRunTrigger.Scheduled, 1, "system", new Dictionary<string, JsonElement>(), (_, _) => ValueTask.CompletedTask);

        var outcome = await new PaperReviewService(factory).RunAsync(context, Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Succeeded);
        outcome.Message.ShouldNotBeNull().ShouldContain("nothing applied");
        await handler.Received(1).HandleAsync(Arg.Is<BuildPaperReviewCommand>(c => c.Week == null && c.Store && c.Actor == "system:paper-review"), Ct);
        provider.ReceivedCalls().Count().ShouldBe(1);
    }
}
