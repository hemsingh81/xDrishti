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
    public async Task An_old_automatic_tightening_that_still_stands_is_named_but_an_owner_decision_an_undone_or_replaced_one_is_not()
    {
        var store = new FakeLearnedStore { Now = Now.AddDays(-90) };
        store.Seed(FakeLearnedConfig.BpsKey, "4", "4.2", "tighten", "applied", "auto"); // 1: old, still the newest of its key
        store.Seed("Confidence:Decay:Sigmas", "1", "0.9", "tighten", "applied", "owner:me@x.test"); // 2: an owner decision
        store.Seed(FakeLearnedConfig.NoDirectionKey, "50", "55", "tighten", "undone", "auto"); // 3: undone
        store.Seed(FakeLearnedConfig.LoopLevel, "0.95", "0.96", "tighten", "applied", "auto"); // 4: auto, then replaced by an owner row
        store.Seed(FakeLearnedConfig.LoopLevel, "0.96", "0.95", "loosen", "applied", "owner:me@x.test"); // 5

        var notices = await new LearnedTighteningNotices(store, Probe(6), new FakeLearnedConfig()).ListAsync(Now, Ct);

        notices.Select(n => n.Id).ShouldBe(["learned-empty:1:6"]);
    }

    [Fact]
    public async Task The_stored_probe_counts_the_newest_version_of_each_session_while_the_primary_list_is_empty()
    {
        var plans = Substitute.For<XDrishti.Application.Abstractions.Planning.IPlanStore>();
        XDrishti.Application.Abstractions.Planning.PlanRow Row(long id, int day, int version, int primary) =>
            new(id, new XDrishti.Application.Abstractions.Planning.NewPlan("k", new DateOnly(2026, 11, day), Now, version, "h", "b", false, "t", Now, []), XDrishti.Domain.Planning.PlanState.Published, primary, 0, 0);
        plans.ListAsync(null, null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new[] { Row(5, 9, 1, 0), Row(4, 8, 2, 0), Row(3, 8, 1, 3), Row(2, 7, 1, 0), Row(1, 6, 1, 2) });

        (await new StoredEmptyPlanProbe(plans).ConsecutiveEmptySessionsAsync(Ct)).ShouldBe(3); // 9, 8 (version 2 is empty), 7; then 6 has a primary
    }

    [Fact]
    public async Task With_no_stored_plan_the_stored_probe_cannot_tell()
    {
        var plans = Substitute.For<XDrishti.Application.Abstractions.Planning.IPlanStore>();
        plans.ListAsync(null, null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<XDrishti.Application.Abstractions.Planning.PlanRow>());

        (await new StoredEmptyPlanProbe(plans).ConsecutiveEmptySessionsAsync(Ct)).ShouldBeNull();
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
