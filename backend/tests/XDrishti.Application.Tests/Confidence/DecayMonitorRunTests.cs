#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Confidence;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>CF-12 part 12b: the daily run over the strategies, the service that starts paused after the shadow grading, the proposals read model and the kill thresholds that are only ever tightened.</summary>
public sealed class DecayMonitorRunTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ShadowResultRow Row(string key, long? epoch = 1, bool counts = true) => new(new DateOnly(2026, 11, 3), key, "primary", counts, "graded", 0.5, "net", null, 0.6, epoch);

    private static (EvaluateDecayHandler Handler, ICommandHandler<ProposeKillCommand, ProposeKillOutcome> Propose) Arrange(params ShadowResultRow[] rows)
    {
        var shadow = Substitute.For<IShadowResultReader>();
        shadow.ResultsAsync(Ct).Returns(rows);
        var ledger = Substitute.For<IPaperLedgerData>();
        ledger.EpochAsync(null, Ct).Returns(new EpochRow(1, new DateOnly(2026, 11, 2), DateTimeOffset.UnixEpoch, "first", "default", "system"));
        var propose = Substitute.For<ICommandHandler<ProposeKillCommand, ProposeKillOutcome>>();
        propose.HandleAsync(Arg.Any<ProposeKillCommand>(), Ct).Returns(c => ValueTask.FromResult(Result.Success(new ProposeKillOutcome(c.Arg<ProposeKillCommand>().Strategy!, false, [1L], 0, 0))));
        return (new EvaluateDecayHandler(shadow, ledger, propose, NullLogger<EvaluateDecayHandler>.Instance), propose);
    }

    [Fact]
    public async Task Each_strategy_of_the_current_epoch_with_an_edge_candidate_is_evaluated_once()
    {
        var (handler, propose) = Arrange(Row("ORB|AAA|BUY|2026-11-03"), Row("ORB|BBB|BUY|2026-11-03"), Row("VWAP|AAA|BUY|2026-11-03"), Row("OLD|AAA|BUY|2026-11-03", epoch: 0), Row("NOEDGE|AAA|BUY|2026-11-03", counts: false));

        var outcome = (await handler.HandleAsync(new EvaluateDecayCommand(), Ct)).Value;

        outcome.ShouldBe(new DecayRunOutcome(2, 2, 0, 2, 0, 0));
        await propose.Received(1).HandleAsync(Arg.Is<ProposeKillCommand>(c => c.Strategy == "ORB"), Ct);
        await propose.Received(1).HandleAsync(Arg.Is<ProposeKillCommand>(c => c.Strategy == "VWAP"), Ct);
        await propose.DidNotReceive().HandleAsync(Arg.Is<ProposeKillCommand>(c => c.Strategy == "OLD" || c.Strategy == "NOEDGE"), Ct);
    }

    [Fact]
    public async Task A_failed_strategy_is_counted_and_does_not_stop_the_others()
    {
        var (handler, propose) = Arrange(Row("ORB|AAA|BUY|2026-11-03"), Row("VWAP|AAA|BUY|2026-11-03"));
        propose.HandleAsync(Arg.Is<ProposeKillCommand>(c => c.Strategy == "ORB"), Ct).Returns(ValueTask.FromResult(Result.Failure<ProposeKillOutcome>(Error.Validation("paper.invalid", "no"))));

        var outcome = (await handler.HandleAsync(new EvaluateDecayCommand(), Ct)).Value;

        (outcome.Failed, outcome.Checked).ShouldBe((1, 1));
    }

    [Fact]
    public async Task A_strategy_whose_evaluation_throws_is_counted_and_logged_and_the_others_are_still_evaluated()
    {
        var (handler, propose) = Arrange(Row("ORB|AAA|BUY|2026-11-03"), Row("VWAP|AAA|BUY|2026-11-03"), Row("ZZZ|AAA|BUY|2026-11-03"));
        propose.HandleAsync(Arg.Is<ProposeKillCommand>(c => c.Strategy == "ORB"), Ct).Returns<ValueTask<Result<ProposeKillOutcome>>>(_ => throw new InvalidOperationException("the store is down"));

        var outcome = (await handler.HandleAsync(new EvaluateDecayCommand(), Ct)).Value;

        (outcome.Strategies, outcome.Checked, outcome.Failed).ShouldBe((3, 2, 1));
        await propose.Received(1).HandleAsync(Arg.Is<ProposeKillCommand>(c => c.Strategy == "VWAP"), Ct);
        await propose.Received(1).HandleAsync(Arg.Is<ProposeKillCommand>(c => c.Strategy == "ZZZ"), Ct);
    }

    [Fact]
    public async Task A_failed_automatic_apply_is_counted_as_a_failure_and_a_retry_that_applies_is_counted_as_applied()
    {
        var (handler, propose) = Arrange(Row("ORB|AAA|BUY|2026-11-03"), Row("VWAP|AAA|BUY|2026-11-03"));
        propose.HandleAsync(Arg.Is<ProposeKillCommand>(c => c.Strategy == "ORB"), Ct).Returns(ValueTask.FromResult(Result.Success(new ProposeKillOutcome("ORB", false, [1L], 0, 0, 1))));
        propose.HandleAsync(Arg.Is<ProposeKillCommand>(c => c.Strategy == "VWAP"), Ct).Returns(ValueTask.FromResult(Result.Success(new ProposeKillOutcome("VWAP", true, [], 1, 0))));

        var outcome = (await handler.HandleAsync(new EvaluateDecayCommand(), Ct)).Value;

        outcome.ShouldBe(new DecayRunOutcome(2, 1, 1, 1, 1, 1));
    }

    [Fact]
    public async Task Rows_written_before_the_first_epoch_are_evaluated_like_the_decay_report_reads_them()
    {
        var (_, propose) = Arrange();
        var ledger = Substitute.For<IPaperLedgerData>();
        ledger.EpochAsync(null, Ct).Returns((EpochRow?)null);
        var shadow = Substitute.For<IShadowResultReader>();
        shadow.ResultsAsync(Ct).Returns([Row("ORB|AAA|BUY|2026-11-03", epoch: null), Row("VWAP|AAA|BUY|2026-11-03", epoch: 1)]);

        var outcome = (await new EvaluateDecayHandler(shadow, ledger, propose, NullLogger<EvaluateDecayHandler>.Instance).HandleAsync(new EvaluateDecayCommand(), Ct)).Value;

        outcome.ShouldBe(new DecayRunOutcome(1, 1, 0, 1, 0, 0), "the strategy with epoch-less rows is evaluated and its proposal counted; the row of an epoch is not this series");
        await propose.Received(1).HandleAsync(Arg.Is<ProposeKillCommand>(c => c.Strategy == "ORB"), Ct);
        await propose.DidNotReceive().HandleAsync(Arg.Is<ProposeKillCommand>(c => c.Strategy == "VWAP"), Ct);
    }

    [Fact]
    public async Task Without_a_paper_epoch_and_without_rows_there_is_nothing_to_do()
    {
        var shadow = Substitute.For<IShadowResultReader>();
        shadow.ResultsAsync(Ct).Returns([]);
        var ledger = Substitute.For<IPaperLedgerData>();
        ledger.EpochAsync(null, Ct).Returns((EpochRow?)null);
        var propose = Substitute.For<ICommandHandler<ProposeKillCommand, ProposeKillOutcome>>();

        var outcome = (await new EvaluateDecayHandler(shadow, ledger, propose, NullLogger<EvaluateDecayHandler>.Instance).HandleAsync(new EvaluateDecayCommand(), Ct)).Value;

        outcome.Strategies.ShouldBe(0);
        await propose.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public void The_service_starts_paused_follows_the_shadow_grading_and_raises_no_schedule_issue()
    {
        var d = DecayMonitorService.Definition;

        d.Id.ShouldBe("decay-monitor");
        d.Defaults.Enabled.ShouldBeTrue();
        d.Defaults.Schedule.ShouldBe(Schedule.Daily(new TimeOnly(18, 0)));
        d.Constraints.MustFollow.ShouldBe([ShadowGradeService.Id]);
        var subjects = new[] { XDrishti.Application.Market.Bars.Eod.EodService.Definition, ShadowGradeService.Definition, d }.Select(x => new RuleSubject(x, x.Defaults)).ToList();

        ScheduleRules.Evaluate(subjects).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_reports_its_counts_and_fails_when_a_strategy_failed_or_the_run_was_refused()
    {
        var handler = Substitute.For<ICommandHandler<EvaluateDecayCommand, DecayRunOutcome>>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<EvaluateDecayCommand, DecayRunOutcome>)).Returns(handler);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        var context = new ServiceRunContext(Guid.NewGuid(), ServiceRunTrigger.Scheduled, 1, "system", new Dictionary<string, JsonElement>(), (_, _) => ValueTask.CompletedTask);
        handler.HandleAsync(Arg.Any<EvaluateDecayCommand>(), Ct).Returns(
            Result.Success(new DecayRunOutcome(2, 2, 0, 1, 0, 0)),
            Result.Success(new DecayRunOutcome(2, 1, 0, 0, 0, 1)),
            Result.Failure<DecayRunOutcome>(Error.Validation("x", "refused")));
        var service = new DecayMonitorService(factory);

        var ok = await service.RunAsync(context, Ct);
        var partly = await service.RunAsync(context, Ct);
        var refused = await service.RunAsync(context, Ct);

        ok.Status.ShouldBe(ServiceRunStatus.Succeeded);
        ok.Message.ShouldNotBeNull().ShouldContain("1 proposal(s)");
        partly.Status.ShouldBe(ServiceRunStatus.Failed);
        refused.Status.ShouldBe(ServiceRunStatus.Failed);
    }

    [Fact]
    public async Task The_proposals_are_listed_with_the_setting_beside_them_and_a_bad_state_or_limit_is_refused()
    {
        var f = new KillFixture { ReportResult = Result.Success(KillFixture.View(KillFixture.Kill("drawdown", "fires"))) };
        await f.WalkAsync(KillFixture.Cell, XDrishti.Domain.Learning.CellState.Candidate, XDrishti.Domain.Learning.CellState.Paper);
        await f.ProposeAsync();
        f.Switch = new KillSwitch(3, false);
        var handler = new GetKillProposalsHandler(f.Store, f.Settings, new KillOptions());

        var view = (await handler.HandleAsync(new GetKillProposalsQuery("proposed", "ORB"), Ct)).Value;
        var badState = await handler.HandleAsync(new GetKillProposalsQuery("done", null), Ct);
        var badLimit = await handler.HandleAsync(new GetKillProposalsQuery(null, null, 201), Ct);

        view.Items.Single().Rule.ShouldBe("drawdown");
        view.Settings.ShouldBe(new KillSettingsView(false, 3, 1.5, 2, new KillOptions().SnapshotHash()));
        badState.IsFailure.ShouldBeTrue();
        badLimit.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task The_settings_view_carries_the_actor_of_the_switch_row_so_a_system_default_is_not_shown_as_the_owner()
    {
        var f = new KillFixture();
        f.Switch = new KillSwitch(9, true, KillSwitch.SystemDefaultActor);
        var handler = new GetKillProposalsHandler(f.Store, f.Settings, new KillOptions());

        var view = (await handler.HandleAsync(new GetKillProposalsQuery(null, null), Ct)).Value;

        (view.Settings.Auto, view.Settings.SettingId, view.Settings.Actor).ShouldBe((true, 9L, "system:default"));
    }

    [Theory]
    [InlineData(1.5, 2, true)]
    [InlineData(3, 1, true)]
    [InlineData(2.25, 1.5, true)]
    [InlineData(1.49, 2, false)]
    [InlineData(3.01, 2, false)]
    [InlineData(1.5, 2.01, false)]
    [InlineData(1.5, 0.99, false)]
    [InlineData(double.NaN, 2, false)]
    [InlineData(1.5, double.PositiveInfinity, false)]
    public void The_kill_thresholds_can_only_be_tightened_in_both_directions(double multiplier, double factor, bool allowed)
    {
        new KillOptions(multiplier, factor).Problems().Count.ShouldBe(allowed ? 0 : 1);
    }

    [Fact]
    public void The_defaults_are_the_specs_proposals_and_the_snapshot_hash_follows_the_values()
    {
        var o = new KillOptions();

        (o.StressMultiplier, o.DrawdownFactor).ShouldBe((1.5, 2.0));
        o.KeyText().ShouldBe("stress=1.5/drawdownFactor=2");
        o.SnapshotHash().Length.ShouldBe(64);
        o.SnapshotHash().ShouldNotBe(new KillOptions(2, 2).SnapshotHash());
    }
}
