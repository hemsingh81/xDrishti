#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Learning;

/// <summary>Spec P7-15: the weekly service starts paused, runs Saturday 10:00 and only builds and stores the digest.</summary>
public sealed class WeeklyServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void It_starts_paused_and_runs_on_saturday_at_10()
    {
        var d = WeeklyService.Definition;

        d.Id.ShouldBe("weekly");
        d.Defaults.Enabled.ShouldBeFalse();
        d.Defaults.Schedule.ShouldBe(Schedule.Weekly(DayOfWeek.Saturday, new TimeOnly(10, 0)));
        new WeeklyService(Substitute.For<IServiceScopeFactory>()).FirstStartSettings().ShouldBeNull();
    }

    [Fact]
    public async Task A_run_builds_the_current_digest_as_the_weekly_requester_and_nothing_else()
    {
        var handler = Substitute.For<ICommandHandler<BuildDigestCommand, DigestView>>();
        var view = new DigestView(5, "2026-W43", new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25), [], "t", "system:weekly", DateTimeOffset.UnixEpoch, false);
        handler.HandleAsync(Arg.Any<BuildDigestCommand>(), Ct).Returns(Result.Success(view));
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<BuildDigestCommand, DigestView>)).Returns(handler);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        var context = new ServiceRunContext(Guid.NewGuid(), ServiceRunTrigger.Scheduled, 1, "system", new Dictionary<string, JsonElement>(), (_, _) => ValueTask.CompletedTask);

        var outcome = await new WeeklyService(factory).RunAsync(context, Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Succeeded);
        outcome.Message.ShouldNotBeNull().ShouldContain("nothing applied");
        await handler.Received(1).HandleAsync(Arg.Is<BuildDigestCommand>(c => c.Week == null && !c.DryRun && c.Actor == "system:weekly"), Ct);
        provider.ReceivedCalls().Count().ShouldBe(1);
    }
}
