#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Confidence;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>CF-11 part 1 (ADR 0221): the read handlers, the owner's switch, the daily snapshot run and the service that carries it out.</summary>
public sealed class ReadinessHandlersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- the scorecard queries ----

    [Fact]
    public async Task The_scorecard_of_a_version_carries_the_switch_and_the_daily_trend_of_that_version_only()
    {
        var f = new ReadinessFixture();
        f.Store.Switch = new ReadinessSwitch(3, true);
        await f.Snapshot.HandleAsync(new SnapshotScorecardsCommand(), Ct);

        var result = await f.Get.HandleAsync(new GetScorecardQuery("ORB", 1), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Enforce.ShouldBeTrue();
        result.Value.History.ShouldHaveSingleItem().Score.ShouldBe(100);
        result.Value.History[0].SettingsHash.ShouldBe(f.Options.SnapshotHash());
    }

    [Theory]
    [InlineData("", 1, "confidence.readiness_invalid")]
    [InlineData("A|B", 1, "confidence.readiness_invalid")]
    [InlineData("ORB", 0, "confidence.readiness_invalid")]
    [InlineData("NOPE", 1, "confidence.scorecard_not_found")]
    [InlineData("ORB", 2, "confidence.scorecard_not_found")]
    public async Task A_bad_or_unknown_strategy_or_version_is_refused_with_its_code(string strategy, int version, string code)
    {
        var f = new ReadinessFixture();

        var result = await f.Get.HandleAsync(new GetScorecardQuery(strategy, version), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
    }

    [Fact]
    public async Task The_list_has_the_system_card_and_the_strategies_weakest_first()
    {
        var f = new ReadinessFixture();
        f.StrategyList = [new StoredStrategy("ORB", "intraday", false, DateTimeOffset.UnixEpoch, 1, 1), new StoredStrategy("ABC", "intraday", false, DateTimeOffset.UnixEpoch, 1, 1)];

        var result = await f.List.HandleAsync(new ListScorecardsQuery(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Strategies.Select(s => s.Strategy).ShouldBe(["ABC", "ORB"], "ABC has no forward series or verdict in the fixture's Lab (its run is ORB's), so it scores lower");
        result.Value.System.Strategies.ShouldBe(2);
        result.Value.System.EqualWeights.ShouldBeFalse("the passing gaps give a share of 1 to each");
        result.Value.Settings.Enforce.ShouldBeFalse();
        result.Value.Settings.SettingsHash.ShouldBe(f.Options.SnapshotHash());
        result.Value.Statements.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task With_no_strategy_the_list_is_empty_and_the_system_card_says_so()
    {
        var f = new ReadinessFixture { StrategyList = [] };

        var result = await f.List.HandleAsync(new ListScorecardsQuery(), Ct);

        result.Value.Strategies.ShouldBeEmpty();
        result.Value.System.Reason.ShouldBe("no strategy yet");
        result.Value.System.Score.ShouldBe(0);
    }

    [Fact]
    public async Task With_the_real_gaps_the_system_card_is_zero_because_the_integrity_cannot_be_read_and_the_weights_are_equal()
    {
        var f = new ReadinessFixture { Gaps = new UnavailableReadinessGaps() };

        var result = await f.List.HandleAsync(new ListScorecardsQuery(), Ct);

        result.Value.System.Score.ShouldBe(0);
        result.Value.System.Integrity.ShouldBe(0);
        result.Value.System.EqualWeights.ShouldBeTrue();
        result.Value.Statements.ShouldContain(s => s.Contains("equally", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_settings_view_shows_the_switch_and_the_hash()
    {
        var f = new ReadinessFixture();
        f.Store.Switch = new ReadinessSwitch(9, true);
        var handler = new GetReadinessSettingsHandler(f.Store, new ReadinessOptionsView(f.Options));

        var view = await handler.HandleAsync(new GetReadinessSettingsQuery(), Ct);

        view.ShouldBe(new ReadinessSettingsView(true, 9, f.Options.SnapshotHash(), f.Options.KeyText()));
    }

    // ---- the owner's switch ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_owner_switch_appends_one_row_with_the_owner_as_actor(bool enforce)
    {
        var store = Substitute.For<IReadinessSwitchStore>();
        store.AppendAsync(Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(7L));
        var handler = new ChangeReadinessSwitchHandler(store, NullLogger<ChangeReadinessSwitchHandler>.Instance);

        var result = await handler.HandleAsync(new ChangeReadinessSwitchCommand("owner:me@x.test", enforce, "  watched gate R pass  "), Ct);

        result.Value.ShouldBe(new ReadinessSwitched(7, enforce));
        await store.Received(1).AppendAsync(enforce, "owner:me@x.test", "watched gate R pass", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("auto", "why")]
    [InlineData("cli:xd", "why")]
    [InlineData("owner:", "why")]
    [InlineData("", "why")]
    public async Task A_non_owner_actor_is_refused_before_anything_is_written(string actor, string reason)
    {
        var store = Substitute.For<IReadinessSwitchStore>();
        var handler = new ChangeReadinessSwitchHandler(store, NullLogger<ChangeReadinessSwitchHandler>.Instance);

        var result = await handler.HandleAsync(new ChangeReadinessSwitchCommand(actor, true, reason), Ct);

        result.Error.Code.ShouldBe(KillErrors.OwnerOnlyCode);
        await store.DidNotReceiveWithAnyArgs().AppendAsync(default, default!, default!, Ct);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_reason_is_refused_and_so_is_one_over_500_characters(string reason)
    {
        var store = Substitute.For<IReadinessSwitchStore>();
        var handler = new ChangeReadinessSwitchHandler(store, NullLogger<ChangeReadinessSwitchHandler>.Instance);

        (await handler.HandleAsync(new ChangeReadinessSwitchCommand("owner:me@x.test", true, reason), Ct)).Error.Code.ShouldBe(KillErrors.ReasonInvalidCode);
        (await handler.HandleAsync(new ChangeReadinessSwitchCommand("owner:me@x.test", true, new string('x', 501)), Ct)).Error.Code.ShouldBe(KillErrors.ReasonInvalidCode);
        await store.DidNotReceiveWithAnyArgs().AppendAsync(default, default!, default!, Ct);
    }

    // ---- the daily snapshots ----

    [Fact]
    public async Task The_daily_run_stores_one_snapshot_per_strategy_and_one_for_the_system_and_is_idempotent()
    {
        var f = new ReadinessFixture();

        var first = await f.Snapshot.HandleAsync(new SnapshotScorecardsCommand(), Ct);
        var second = await f.Snapshot.HandleAsync(new SnapshotScorecardsCommand(), Ct);

        first.Value.ShouldBe(new SnapshotRunOutcome(1, 2, 0, 0));
        second.Value.ShouldBe(new SnapshotRunOutcome(1, 0, 2, 0));
        f.Store.Snapshots.Count.ShouldBe(2);
        f.Store.Snapshots.Select(s => s.Subject).ShouldBe(["strategy", "system"]);
        f.Store.Snapshots.ShouldAllBe(s => s.Scope == "daily" && s.SettingsHash == f.Options.SnapshotHash() && s.TakenOn == new DateOnly(2026, 11, 6));
        f.Store.Snapshots[0].Body.ShouldContain("Rows");
        first.Value.Summary.ShouldContain("2 snapshot(s) stored");
    }

    [Fact]
    public async Task A_strategy_that_throws_is_counted_and_the_others_still_get_their_snapshot()
    {
        var f = new ReadinessFixture();
        f.StrategyList = [new StoredStrategy("ORB", "intraday", false, DateTimeOffset.UnixEpoch, 1, 1), new StoredStrategy("BAD", "intraday", false, DateTimeOffset.UnixEpoch, 1, 1)];
        f.Runs.HandleAsync(Arg.Is<ListLabRunsQuery>(q => q.StrategyId == "BAD"), Arg.Any<CancellationToken>()).Returns<ValueTask<Result<IReadOnlyList<LabRunView>>>>(_ => throw new InvalidOperationException("down"));

        var result = await f.Snapshot.HandleAsync(new SnapshotScorecardsCommand(), Ct);

        result.Value.Failed.ShouldBe(1);
        f.Store.Snapshots.Select(s => s.StrategyId).ShouldBe(["ORB", null]);
    }

    [Fact]
    public async Task A_strategy_whose_scorecard_cannot_be_built_counts_as_zero_in_the_system_score_and_is_named_not_dropped()
    {
        var f = new ReadinessFixture();
        f.StrategyList = [new StoredStrategy("ORB", "intraday", false, DateTimeOffset.UnixEpoch, 1, 1), new StoredStrategy("BAD", "intraday", false, DateTimeOffset.UnixEpoch, 1, 1)];
        f.Runs.HandleAsync(Arg.Is<ListLabRunsQuery>(q => q.StrategyId == "BAD"), Arg.Any<CancellationToken>()).Returns<ValueTask<Result<IReadOnlyList<LabRunView>>>>(_ => throw new InvalidOperationException("down"));

        await f.Snapshot.HandleAsync(new SnapshotScorecardsCommand(), Ct);

        var system = f.Store.Snapshots.Single(s => s.Subject == "system");
        system.Score.ShouldBe(50, 1e-9, "ORB 100 and the unbuilt BAD 0, equal risk shares of 1");
        system.Body.ShouldContain("\"WeakestStrategy\":\"BAD\"");
        f.Store.Snapshots.ShouldNotContain(s => s.StrategyId == "BAD", "an unbuilt card is never stored as the strategy's snapshot");
    }

    [Fact]
    public async Task One_refused_insert_does_not_abort_the_others_and_the_strategy_still_counts_with_its_real_score()
    {
        var f = new ReadinessFixture();
        f.StrategyList = [new StoredStrategy("ORB", "intraday", false, DateTimeOffset.UnixEpoch, 1, 1), new StoredStrategy("ABC", "intraday", false, DateTimeOffset.UnixEpoch, 1, 1)];
        f.Store.FailInsertWhen = s => s.StrategyId == "ORB";

        var result = await f.Snapshot.HandleAsync(new SnapshotScorecardsCommand(), Ct);

        result.Value.ShouldBe(new SnapshotRunOutcome(2, 2, 0, 1));
        f.Store.Snapshots.Select(s => s.StrategyId).ShouldBe(["ABC", null]);
        // ORB scores 100 (it has a forward series) and ABC 0 (it has none): 50 means ORB counted with its real card; an unbuilt ORB would have made it 0
        f.Store.Snapshots.Single(s => s.Subject == "system").Score.ShouldBe(50, 1e-9, "the card of ORB was built; only its insert failed");
    }

    [Fact]
    public async Task A_cancelled_run_is_not_counted_as_a_failure()
    {
        var f = new ReadinessFixture();
        f.Runs.HandleAsync(Arg.Any<ListLabRunsQuery>(), Arg.Any<CancellationToken>()).Returns<ValueTask<Result<IReadOnlyList<LabRunView>>>>(_ => throw new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(async () => await f.Snapshot.HandleAsync(new SnapshotScorecardsCommand(), Ct));
    }

    // ---- the service ----

    [Fact]
    public void The_service_starts_paused_runs_daily_at_18_30_after_the_decay_monitor_and_raises_no_schedule_issue()
    {
        var d = ReadinessSnapshotService.Definition;

        d.Id.ShouldBe("readiness-snapshot");
        d.Defaults.Enabled.ShouldBeFalse("starts paused");
        d.Defaults.Schedule.ShouldBe(Schedule.Daily(new TimeOnly(18, 30)));
        d.Constraints.MustFollow.ShouldBe([DecayMonitorService.Id]);
        d.Description.ShouldContain("moves no cell");
        var subjects = new[] { XDrishti.Application.Market.Bars.Eod.EodService.Definition, XDrishti.Application.Shadow.ShadowGradeService.Definition, DecayMonitorService.Definition, d }.Select(x => new RuleSubject(x, x.Defaults)).ToList();

        ScheduleRules.Evaluate(subjects).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_reports_its_counts_and_fails_when_a_strategy_failed_or_the_run_was_refused()
    {
        var handler = Substitute.For<ICommandHandler<SnapshotScorecardsCommand, SnapshotRunOutcome>>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<SnapshotScorecardsCommand, SnapshotRunOutcome>)).Returns(handler);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        var context = new ServiceRunContext(Guid.NewGuid(), ServiceRunTrigger.Scheduled, 1, "system", new Dictionary<string, JsonElement>(), (_, _) => ValueTask.CompletedTask);
        handler.HandleAsync(Arg.Any<SnapshotScorecardsCommand>(), Ct).Returns(
            Result.Success(new SnapshotRunOutcome(2, 3, 0, 0)),
            Result.Success(new SnapshotRunOutcome(2, 2, 0, 1)),
            Result.Failure<SnapshotRunOutcome>(Error.Validation("x", "refused")));
        var service = new ReadinessSnapshotService(factory);

        var ok = await service.RunAsync(context, Ct);
        var partly = await service.RunAsync(context, Ct);
        var refused = await service.RunAsync(context, Ct);

        ok.Status.ShouldBe(ServiceRunStatus.Succeeded);
        ok.Message.ShouldNotBeNull().ShouldContain("3 snapshot(s) stored");
        partly.Status.ShouldBe(ServiceRunStatus.Failed);
        refused.Status.ShouldBe(ServiceRunStatus.Failed);
    }
}
