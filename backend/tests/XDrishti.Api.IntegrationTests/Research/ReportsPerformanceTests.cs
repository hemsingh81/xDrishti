using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Spec P8-06, non-functional: the report endpoints are measured on a synthetic run, not assumed. A run of 20,000 trades always runs (the figures are summed by hand from the run and compared). The 500,000-trade measurement is explicit (run with the explicit
/// filter of the runner): it stores the run in a scratch database, then takes <c>XD_REPORT_SAMPLES</c> (default 20) samples of every report, each in a scope of its own like a request, and prints the median, the 95th percentile and the largest in milliseconds. A sample is the use
/// case plus the JSON of its answer; the HTTP hop, the session check and the network are not in it. The target of 1 s p95 is the plan's UNVERIFIED target; the result is written in ADR 0100, met or not.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReportsPerformanceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Say(string line)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        Console.WriteLine(line);
    }

    [Fact]
    public async Task A_run_of_20000_trades_gives_the_figures_summed_by_hand()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_report_small", Ct);
        var run = StoredRunFixtures.Synthetic(20_000, costed: false);
        (await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask())).IsSuccess.ShouldBeTrue();

        using var scope = scratch.Host.Services.CreateScope();
        var summary = await Ok(scope.ServiceProvider.GetRequiredService<IQueryHandler<GetReportSummaryQuery, Result<SummaryView>>>().HandleAsync(new GetReportSummaryQuery(ReportQuery.Default), Ct));
        var groups = await Ok(scope.ServiceProvider.GetRequiredService<IQueryHandler<GetReportGroupsQuery, Result<GroupsView>>>().HandleAsync(new GetReportGroupsQuery(ReportQuery.Default with { Lens = [XDrishti.Domain.Reporting.ReportDimension.Side] }), Ct));

        summary.All.N.ShouldBe(run.Trades.Count);
        summary.All.TotalR.Number!.Value.ShouldBe(run.Trades.Sum(t => t.Trade.RFilled), 1e-6);
        summary.Evidence.DefaultRunSet.ShouldBeTrue();
        groups.Groups.Sum(g => g.N).ShouldBe(run.Trades.Count);
    }

    [Fact(Explicit = true)]
    public async Task A_run_of_500000_trades_is_measured_for_the_95th_percentile_of_every_report()
    {
        var samples = int.Parse(Environment.GetEnvironmentVariable("XD_REPORT_SAMPLES") ?? "20", CultureInfo.InvariantCulture);
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_report_p95", Ct);
        var built = Stopwatch.StartNew();
        var run = StoredRunFixtures.Synthetic(500_000, costed: false);
        var toStore = StoreKit.ToStore(run);
        built.Stop();
        var write = Stopwatch.StartNew();
        (await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.StoreAsync(toStore, Ct).AsTask())).IsSuccess.ShouldBeTrue();
        write.Stop();
        await StoreKit.ExecuteAsync(scratch.ConnectionString, "VACUUM (ANALYZE) research.sim_trades", Ct);
        Say($"report-p95: {run.Trades.Count} trades built in {built.ElapsedMilliseconds} ms, stored in {write.ElapsedMilliseconds} ms; {samples} samples per report");

        await StagesAsync(scratch.Host.Services);
        var lens = ReportQuery.Default with { Lens = [XDrishti.Domain.Reporting.ReportDimension.Strategy, XDrishti.Domain.Reporting.ReportDimension.Side] };
        var pivot = ReportQuery.Default with { Lens = [XDrishti.Domain.Reporting.ReportDimension.Instrument, XDrishti.Domain.Reporting.ReportDimension.Month] };
        var cases = new (string Name, Func<IServiceProvider, Task<string>> Call)[]
        {
            ("summary", sp => Json(sp.GetRequiredService<IQueryHandler<GetReportSummaryQuery, Result<SummaryView>>>().HandleAsync(new GetReportSummaryQuery(ReportQuery.Default), Ct))),
            ("groups strategy,side", sp => Json(sp.GetRequiredService<IQueryHandler<GetReportGroupsQuery, Result<GroupsView>>>().HandleAsync(new GetReportGroupsQuery(lens), Ct))),
            ("groups month", sp => Json(sp.GetRequiredService<IQueryHandler<GetReportGroupsQuery, Result<GroupsView>>>().HandleAsync(new GetReportGroupsQuery(ReportQuery.Default with { Lens = [XDrishti.Domain.Reporting.ReportDimension.Month] }), Ct))),
            ("pivot instrument x month", sp => Json(sp.GetRequiredService<IQueryHandler<GetReportPivotQuery, Result<PivotView>>>().HandleAsync(new GetReportPivotQuery(pivot), Ct))),
            ("equity all", sp => Json(sp.GetRequiredService<IQueryHandler<GetReportEquityQuery, Result<EquityView>>>().HandleAsync(new GetReportEquityQuery(ReportQuery.Default), Ct))),
            ("distribution all", sp => Json(sp.GetRequiredService<IQueryHandler<GetReportDistributionQuery, Result<DistributionView>>>().HandleAsync(new GetReportDistributionQuery(ReportQuery.Default), Ct))),
            ("insights", sp => Json(sp.GetRequiredService<IQueryHandler<GetReportInsightsQuery, Result<InsightsView>>>().HandleAsync(new GetReportInsightsQuery(ReportQuery.Default), Ct))),
            ("what-works", sp => Json(sp.GetRequiredService<IQueryHandler<GetReportWhatWorksQuery, Result<WhatWorksView>>>().HandleAsync(new GetReportWhatWorksQuery(ReportQuery.Default), Ct))),
            ("trades page of 100", sp => Json(sp.GetRequiredService<IQueryHandler<PageReportTradesQuery, Result<TradesView>>>().HandleAsync(new PageReportTradesQuery(ReportQuery.Default), Ct))),
            ("runs", sp => Json(sp.GetRequiredService<IQueryHandler<ListReportRunsQuery, Result<RunsView>>>().HandleAsync(new ListReportRunsQuery(), Ct))),
        };
        foreach (var (name, call) in cases)
        {
            using (var warm = scratch.Host.Services.CreateScope())
            {
                await call(warm.ServiceProvider);
            }

            var times = new List<double>();
            long bytes = 0;
            for (var i = 0; i < samples; i++)
            {
                using var scope = scratch.Host.Services.CreateScope();
                var timer = Stopwatch.StartNew();
                bytes = (await call(scope.ServiceProvider)).Length;
                timer.Stop();
                times.Add(timer.Elapsed.TotalMilliseconds);
            }

            times.Sort();
            var p95 = times[Math.Min(times.Count - 1, (int)Math.Ceiling(times.Count * 0.95) - 1)];
            Say(string.Create(CultureInfo.InvariantCulture, $"report-p95: {name,-26} median {times[times.Count / 2],8:F0} ms   p95 {p95,8:F0} ms   max {times[^1],8:F0} ms   answer {bytes / 1024.0,9:F1} KiB"));
        }

        Say($"report-p95: managed memory after the run {GC.GetTotalMemory(forceFullCollection: false) / 1048576.0:F0} MiB; peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:F0} MiB");
    }

    /// <summary>Where one report spends its time: the list of runs, the stream of trades, the population rule, one grouping.</summary>
    private static async Task StagesAsync(IServiceProvider services)
    {
        for (var round = 0; round < 3; round++)
        {
            using var scope = services.CreateScope();
            var source = scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Reporting.IReportTradeSource>();
            var timer = Stopwatch.StartNew();
            var runs = await source.ListRunsAsync(Ct);
            var listed = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            var trades = new List<XDrishti.Domain.Reporting.ReportTrade>();
            await foreach (var trade in source.StreamTradesAsync([.. runs.Select(r => r.RunKey)], XDrishti.Application.Abstractions.Reporting.ReportTradeFilter.None, Ct))
            {
                trades.Add(trade);
            }

            var streamed = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            var population = XDrishti.Domain.Reporting.ReportPopulation.Create([.. runs.Select(r => r.ToReportRun())], trades, new XDrishti.Domain.Reporting.ReportOptions());
            var created = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            var grouped = population.Value.GroupBy([]);
            var grouping = timer.Elapsed.TotalMilliseconds;
            grouped.IsSuccess.ShouldBeTrue();
            Say(string.Create(CultureInfo.InvariantCulture, $"report-p95: stages round {round}: list runs {listed:F0} ms, stream {trades.Count} trades {streamed:F0} ms, population rule {created:F0} ms, one grouping {grouping:F0} ms"));
        }
    }

    private static async Task<T> Ok<T>(ValueTask<Result<T>> call)
    {
        var result = await call;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    private static async Task<string> Json<T>(ValueTask<Result<T>> call) => JsonSerializer.Serialize(await Ok(call), JsonSerializerOptions.Web);
}
