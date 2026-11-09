using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.LearnedSettings;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.LearnedSettings;

/// <summary>AU-10 criterion 9 (the notice names the setting and does not loosen), and the service definition (paused, after the weekly digest, no order).</summary>
public sealed class LearnedNoticesAndServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IEmptyPlanProbe Probe(int? empty)
    {
        var probe = Substitute.For<IEmptyPlanProbe>();
        probe.ConsecutiveEmptySessionsAsync(Arg.Any<CancellationToken>()).Returns(empty);
        return probe;
    }

    [Fact]
    public async Task Five_empty_sessions_after_an_automatic_tightening_raise_a_notice_that_names_the_setting()
    {
        var store = new FakeLearnedStore { Now = Now.AddDays(-2) };
        store.Seed(FakeLearnedConfig.BpsKey, "4", "4.2", "tighten", "applied", "auto");

        var notices = await new LearnedTighteningNotices(store, Probe(5), new FakeLearnedConfig()).ListAsync(Now, Ct);

        var notice = notices.ShouldHaveSingleItem();
        notice.Text.ShouldContain("Tier A bps");
        notice.Text.ShouldContain("Nothing is loosened by itself");
        store.Rows.Single().NewValue.ShouldBe("4.2"); // the notice changed nothing
    }

    [Theory]
    [InlineData(4)]
    [InlineData(null)] // cannot tell: no notice, no invented number
    public async Task Fewer_than_five_empty_sessions_or_an_unknown_count_raise_nothing(int? empty)
    {
        var store = new FakeLearnedStore { Now = Now.AddDays(-2) };
        store.Seed(FakeLearnedConfig.BpsKey, "4", "4.2", "tighten", "applied", "auto");

        (await new LearnedTighteningNotices(store, Probe(empty), new FakeLearnedConfig()).ListAsync(Now, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_owner_decision_or_an_old_change_is_not_named()
    {
        var store = new FakeLearnedStore { Now = Now.AddDays(-30) };
        store.Seed(FakeLearnedConfig.BpsKey, "4", "4.2", "tighten", "applied", "auto");
        store.Seed("Confidence:Decay:Sigmas", "1", "0.9", "tighten", "applied", "owner:me@x.test");

        (await new LearnedTighteningNotices(store, Probe(9), new FakeLearnedConfig()).ListAsync(Now, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_default_probe_and_the_default_evidence_know_nothing()
    {
        (await new NoEmptyPlanProbe().ConsecutiveEmptySessionsAsync(Ct)).ShouldBeNull();
        (await new NoLearnedEvidence().ReadAsync(Ct)).Available.ShouldBeFalse();
    }

    [Fact]
    public void The_service_starts_paused_after_the_weekly_digest()
    {
        var d = LearnedSettingsService.Definition;

        d.Id.ShouldBe("learned-settings");
        d.Defaults.Enabled.ShouldBeFalse();
        d.Defaults.Schedule.ShouldBe(Schedule.AfterService("weekly"));
        d.Constraints.MustFollow.ShouldBe(["weekly"]);
        new LearnedSettingsService(Substitute.For<IServiceScopeFactory>()).FirstStartSettings().ShouldBeNull();
    }

    [Fact]
    public async Task The_service_run_reports_the_outcome_of_the_handler()
    {
        var handler = Substitute.For<ICommandHandler<RunLearnedSettingsCommand, LearnedRunView>>();
        handler.HandleAsync(Arg.Any<RunLearnedSettingsCommand>(), Ct).Returns(XDrishti.Domain.Common.Result.Success(new LearnedRunView("0 proposed", 0, 0, [])));
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<RunLearnedSettingsCommand, LearnedRunView>)).Returns(handler);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        var context = new XDrishti.Application.Abstractions.Platform.ServiceRunContext(Guid.NewGuid(), ServiceRunTrigger.Scheduled, 1, "system", new Dictionary<string, System.Text.Json.JsonElement>(), (_, _) => ValueTask.CompletedTask);

        var outcome = await new LearnedSettingsService(factory).RunAsync(context, Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Succeeded);
        await handler.Received(1).HandleAsync(Arg.Is<RunLearnedSettingsCommand>(c => c.Requester == "system:learned-settings"), Ct);
    }
}
