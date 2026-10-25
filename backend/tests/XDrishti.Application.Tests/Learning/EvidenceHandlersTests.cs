#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The smart-rule evidence handler over a fake plan store (P8-13): validation, version choice, counts and non-values.</summary>
public sealed class EvidenceHandlersTests
{
    private static readonly DateOnly Day = new(2026, 3, 10);
    private static readonly DateTimeOffset At = new(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
    private readonly IPlanStore _plans = Substitute.For<IPlanStore>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlanRow Plan(long id, int version) => new(id, new NewPlan("k" + id, Day, At, version, new string('b', 64), "basis", false, "test", At, []), PlanState.Published, 1, 1, 0);

    private static PlanItemRow Item(long id, long plan, TicketGroup group, string[] reasons, string[] unavailable) =>
        new(id, plan, new NewPlanItem((int)id, group, "NSE:X", "BUY", "ORB", new string('a', 64), "t", "s", "g", "v", reasons, new PlanEvidence("c", 30, "r", "b", null, null, unavailable, null), []), TicketState.Proposed);

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public async Task Plans_outside_1_to_60_are_refused(int plans)
    {
        var result = await new GetSmartRulesEvidenceHandler(_plans).HandleAsync(new GetSmartRulesEvidenceQuery(plans), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("learning.invalid");
    }

    [Fact]
    public async Task No_plan_gives_five_empty_rules_without_a_count_and_every_outcome_is_a_named_non_value()
    {
        _plans.ListAsync(null, null, Arg.Any<int>(), Ct).Returns([]);

        var view = (await new GetSmartRulesEvidenceHandler(_plans).HandleAsync(new GetSmartRulesEvidenceQuery(30), Ct)).Value;

        view.State.ShouldBe("empty");
        view.Rules.Count.ShouldBe(5);
        view.Rules.ShouldAllBe(r => r.State == "empty" && r.UnavailableOn == null && r.Moved == null && r.RanOn == null);
        view.Rules.ShouldAllBe(r => r.Outcome.State == "not_evaluable" && r.Outcome.Value == null && r.Outcome.Reason.Length > 0 && r.Outcome.Command.Length > 0);
    }

    [Fact]
    public async Task The_newest_version_of_a_session_is_counted_and_a_flagged_plan_is_listed()
    {
        _plans.ListAsync(null, null, Arg.Any<int>(), Ct).Returns([Plan(1, 1), Plan(2, 2)]);
        _plans.ItemsAsync(2, Ct).Returns([Item(21, 2, TicketGroup.Reserve, ["gate_no_trade"], ["gate_unavailable", "rule_removed_half"])]);

        var view = (await new GetSmartRulesEvidenceHandler(_plans).HandleAsync(new GetSmartRulesEvidenceQuery(30), Ct)).Value;

        view.PlansRead.ShouldBe(1);
        var gate = view.Rules.Single(r => r.Id == "gate");
        (gate.State, gate.UnavailableOn, gate.Moved, gate.RanOn).ShouldBe(("observed", 1, 1, null));
        view.Rules.Single(r => r.Id == "meta").State.ShouldBe("not_recorded");
        view.Rules.Single(r => r.Id == "meta").Moved.ShouldBeNull();
        view.FlaggedPlans.ShouldBe([new FlaggedPlanView(2, Day, "rule_removed_half")]);
        await _plans.DidNotReceive().ItemsAsync(1, Ct);
    }

    [Fact]
    public async Task Loss_tags_with_no_usable_run_are_empty_and_name_the_skipped_run()
    {
        var source = Substitute.For<XDrishti.Application.Abstractions.Reporting.IReportTradeSource>();
        var run = new XDrishti.Application.Abstractions.Reporting.ReportRunInfo(new string('a', 64), "ORB", "basket", "b", "basket:x", Day, Day, null, null, null, null, null, 10, 0, At);
        source.ListRunsAsync(Ct).Returns([run]);
        var analyze = Substitute.For<IQueryHandler<AnalyzeLossTagsQuery, Result<LossTagReport>>>();
        var handler = new GetLossTagsHandler(source, new XDrishti.Application.Reporting.ReportGate(), analyze);

        var view = (await handler.HandleAsync(new GetLossTagsQuery([]), Ct)).Value;

        view.State.ShouldBe("empty");
        view.Cells.ShouldBeEmpty();
        view.Evidence.Skipped.ShouldHaveSingleItem().ShouldContain("not recorded");
        view.NotBuilt.ShouldNotBeEmpty();
        await analyze.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    private static string Key(char c) => new(c, 64);

    private static XDrishti.Application.Abstractions.Reporting.ReportRunInfo Run(char key, string strategy = "ORB", string? sheet = null, string? style = "intraday", string? exit = "FIXED_RR", int closed = 10) =>
        new(Key(key), strategy, "basket", "b", "basket:" + strategy, Day, Day, sheet, style, null, exit, null, closed, 0, At);

    private static LossTagReport Report(int losses, int window = 30) =>
        new(new LossTagOptions(GrowingWindow: window), losses, losses, [new CellLossTags("C", losses, losses, [0, 0, 0, 0, 0, 0, losses], 0, [], [0, 0, 0, 0, 0, 0, -1.0 * losses])], [], 0, []);

    private static (GetLossTagsHandler Handler, IQueryHandler<AnalyzeLossTagsQuery, Result<LossTagReport>> Analyze) LossTags(IReadOnlyList<XDrishti.Application.Abstractions.Reporting.ReportRunInfo> runs, Result<LossTagReport> answer)
    {
        var source = Substitute.For<XDrishti.Application.Abstractions.Reporting.IReportTradeSource>();
        source.ListRunsAsync(Ct).Returns(runs);
        var analyze = Substitute.For<IQueryHandler<AnalyzeLossTagsQuery, Result<LossTagReport>>>();
        analyze.HandleAsync(Arg.Any<AnalyzeLossTagsQuery>(), Ct).Returns(answer);
        return (new GetLossTagsHandler(source, new XDrishti.Application.Reporting.ReportGate(), analyze), analyze);
    }

    [Fact]
    public async Task Runs_above_the_loss_tag_cap_are_refused_before_anything_is_read()
    {
        var (handler, analyze) = LossTags([Run('a', closed: GetLossTagsHandler.MaxClosedTrades + 1)], Result.Success(Report(1)));

        var result = await handler.HandleAsync(new GetLossTagsQuery([]), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("reports.too_large");
        await analyze.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task The_default_set_uses_the_runs_that_record_style_and_exit_and_skips_the_others()
    {
        var (handler, analyze) = LossTags([Run('a'), Run('b', strategy: "GAP", style: null)], Result.Success(Report(3)));

        var view = (await handler.HandleAsync(new GetLossTagsQuery([]), Ct)).Value;

        await analyze.Received(1).HandleAsync(Arg.Is<AnalyzeLossTagsQuery>(q => q.Sources.Count == 1 && q.Sources[0].RunKey == Key('a') && q.Sources[0].Style == "intraday" && q.Sources[0].Exit == "FIXED_RR"), Ct);
        view.Evidence.DefaultRunSet.ShouldBeTrue();
        view.Evidence.Skipped.ShouldHaveSingleItem().ShouldContain("not recorded");
    }

    [Fact]
    public async Task Named_runs_pass_through_with_an_exit_label_that_holds_a_colon_and_a_missing_run_is_not_found()
    {
        var (handler, analyze) = LossTags([Run('a')], Result.Failure<LossTagReport>(LearningErrors.RunNotFound(Key('b'))));

        var result = await handler.HandleAsync(new GetLossTagsQuery([new CellSource(Key('a'), "swing", "X:Y"), new CellSource(Key('b'), "swing", "Z")]), Ct);

        await analyze.Received(1).HandleAsync(Arg.Is<AnalyzeLossTagsQuery>(q => q.Sources[0].Exit == "X:Y" && q.Sources.Count == 2), Ct);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
    }

    [Theory]
    [InlineData("net", "s", "s")]
    [InlineData("gross", null, null)]
    [InlineData("mixed", "s", null)]
    public async Task The_basis_is_net_gross_or_mixed_by_the_runs_used(string expected, string? first, string? second)
    {
        var (handler, _) = LossTags([Run('a', "ORB", first), Run('b', "GAP", second)], Result.Success(Report(3)));

        var view = (await handler.HandleAsync(new GetLossTagsQuery([new CellSource(Key('a'), "intraday", "E"), new CellSource(Key('b'), "intraday", "E")]), Ct)).Value;

        view.Evidence.Basis.ShouldBe(expected);
    }

    [Theory]
    [InlineData(59, "not_evaluable")]
    [InlineData(60, "evaluated")]
    public async Task Growing_tags_are_evaluated_only_with_two_windows_of_losses(int losses, string state)
    {
        var (handler, _) = LossTags([Run('a')], Result.Success(Report(losses)));

        var cell = (await handler.HandleAsync(new GetLossTagsQuery([]), Ct)).Value.Cells.Single();

        cell.GrowingState.ShouldBe(state);
        (cell.GrowingReason is null).ShouldBe(state == "evaluated");
        cell.Tags[^1].LostR.ShouldBe(-1.0 * losses);
    }

    [Fact]
    public void The_rule_facts_have_one_entry_per_rule_of_the_counter()
    {
        GetSmartRulesEvidenceHandler.Rules.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(SmartRulesEvidence.RuleIds.OrderBy(k => k, StringComparer.Ordinal));
    }
}
