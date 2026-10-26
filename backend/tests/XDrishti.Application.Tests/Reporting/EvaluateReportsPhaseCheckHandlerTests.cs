#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>Spec P8-17 over fake handlers: a refused read is NOT EVALUABLE and never MET, a refusal for overlap fails the default set, the walk lists a broken figure with its path, and every call is a read.</summary>
public sealed class EvaluateReportsPhaseCheckHandlerTests
{
    private sealed record Holder(MetricView Good, MetricView Bad, IReadOnlyList<MetricView> More, string ClientCode);

    private static IQueryHandler<TQuery, Result<TView>> Refusing<TQuery, TView>(string code)
        where TQuery : IQuery<Result<TView>>
    {
        var handler = Substitute.For<IQueryHandler<TQuery, Result<TView>>>();
        handler.HandleAsync(Arg.Any<TQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Failure<TView>(Error.Validation(code, "refused"))));
        return handler;
    }

    private static EvaluateReportsPhaseCheckHandler Handler(string code, IEndpointCatalogue? catalogue = null) => new(
        Refusing<ListReportRunsQuery, RunsView>(code),
        Refusing<GetReportSummaryQuery, SummaryView>(code),
        Refusing<GetReportGroupsQuery, GroupsView>(code),
        Refusing<GetReportPivotQuery, PivotView>(code),
        Refusing<GetReportEquityQuery, EquityView>(code),
        Refusing<GetReportDistributionQuery, DistributionView>(code),
        Refusing<GetReportMaeMfeQuery, ReportMaeMfeView>(code),
        Refusing<GetReportInsightsQuery, InsightsView>(code),
        Refusing<GetReportWhatWorksQuery, WhatWorksView>(code),
        Refusing<PageReportTradesQuery, TradesView>(code),
        catalogue ?? Substitute.For<IEndpointCatalogue>(),
        new FakeTimeProvider(),
        NullLogger<EvaluateReportsPhaseCheckHandler>.Instance);

    [Fact]
    public async Task Reports_that_cannot_be_read_are_not_evaluable_and_the_check_never_prints_met()
    {
        var result = await Handler("reports.too_large").HandleAsync(new EvaluateReportsPhaseCheckQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var report = result.Value.Report;
        report.Criteria.Count.ShouldBe(10);
        report.Criteria.Single(c => c.Number == 1).Verdict.ShouldBe("NOT EVALUABLE");
        report.Criteria.Single(c => c.Number == 2).Verdict.ShouldBe("NOT EVALUABLE");
        report.Criteria.Single(c => c.Number == 3).Verdict.ShouldBe("NOT EVALUABLE");
        report.Criteria.Single(c => c.Number == 5).Verdict.ShouldBe("NOT EVALUABLE");
        report.Headline.ShouldStartWith("P8 exit: NOT EVALUABLE");
        report.Answer.Kind.ShouldBe("no_runs");
        report.Limits.ShouldNotBeEmpty();
        result.Value.Text.ShouldContain("no stored run");
    }

    [Fact]
    public async Task With_no_stored_run_the_probes_and_the_route_check_still_run()
    {
        var catalogue = Substitute.For<IEndpointCatalogue>();
        catalogue.Routes().Returns([new ReportsPhaseRoute("GET", "/api/reports/runs")]);

        var result = await Handler("reports.too_large", catalogue).HandleAsync(new EvaluateReportsPhaseCheckQuery(), CancellationToken.None);

        var criteria = result.Value.Report.Criteria;
        criteria.Single(c => c.Number == 4).Verdict.ShouldBe("MET");
        criteria.Single(c => c.Number == 4).Caveat.ShouldBe("the default set was not checked: no stored run");
        criteria.Single(c => c.Number == 5).Verdict.ShouldStartWith("MET");
    }

    [Fact]
    public async Task A_default_set_refused_for_overlap_is_not_met()
    {
        var result = await Handler("reports.run_overlap").HandleAsync(new EvaluateReportsPhaseCheckQuery(), CancellationToken.None);

        result.Value.Report.Criteria.Single(c => c.Number == 4).Verdict.ShouldBe("NOT MET");
        result.Value.Report.Headline.ShouldBe("P8 exit: NOT MET");
    }

    [Fact]
    public void The_walk_lists_a_broken_figure_with_its_path_and_flags_sensitive_names()
    {
        var holder = new Holder(new MetricView("value", 1.0, null), new MetricView("insufficient", 2.0, "n 3"), [new MetricView("value", double.NaN, null)], "x");

        var scan = ReportViewScan.Of(holder, "root");

        scan.Metrics.ShouldBe(3);
        scan.Breaks.ShouldBe(["root.Bad: a number in state insufficient", "root.More[0]: state value without a finite number"]);
        scan.SensitiveNames.ShouldBe(["ClientCode"]);
    }

    [Fact]
    public void The_walk_hash_is_the_same_for_the_same_content_and_differs_for_another()
    {
        var a = new Holder(new MetricView("value", 1.0, null), new MetricView("empty", null, "none"), [], "x");

        ReportViewScan.Of(a, "r").Hash.ShouldBe(ReportViewScan.Of(a with { }, "r").Hash);
        ReportViewScan.Of(a, "r").Hash.ShouldNotBe(ReportViewScan.Of(a with { Good = new MetricView("value", 1.5, null) }, "r").Hash);
    }
}
