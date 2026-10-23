#pragma warning disable CA2012 // NSubstitute setup and verification calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Planning;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Planning;

/// <summary>The I/O wiring of the real gate classes (spec P7-08): which port is asked with which argument, and what is made of the answer.</summary>
public sealed class NightlyGateWiringTests
{
    private static readonly DateOnly Friday = new(2026, 10, 23);

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly GateContext Context = new(Friday, new DateOnly(2026, 10, 26), new TradingCalendar(), new DateTimeOffset(2026, 10, 23, 13, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceRun Succeeded(string id)
    {
        var run = ServiceRun.Start(id, ServiceRunTrigger.Scheduled, 1, new DateTimeOffset(2026, 10, 23, 16, 0, 0, Ist));
        run.Succeed(new DateTimeOffset(2026, 10, 23, 16, 5, 0, Ist));
        return run;
    }

    [Fact]
    public async Task The_eod_gate_reads_the_latest_twenty_runs_of_the_eod_service()
    {
        var runs = Substitute.For<IServiceRunStore>();
        runs.ListAsync("eod", 0, 20, Ct).Returns(ValueTask.FromResult<IReadOnlyList<ServiceRun>>([Succeeded("eod")]));

        var result = await new EodGate(runs).CheckAsync(Context, Ct);

        result.Status.ShouldBe(GateStatus.Pass);
        await runs.Received(1).ListAsync("eod", 0, 20, Ct);
    }

    [Fact]
    public async Task The_portfolio_sync_gate_reads_the_trades_and_ledger_service_and_accepts_the_no_accounts_skip()
    {
        var skipped = ServiceRun.Skipped("portfolio-broker-history", ServiceRunTrigger.Scheduled, "No Portfolio account is connected to Dhan", new DateTimeOffset(2026, 10, 23, 16, 30, 0, Ist));
        var runs = Substitute.For<IServiceRunStore>();
        runs.ListAsync("portfolio-broker-history", 0, 20, Ct).Returns(ValueTask.FromResult<IReadOnlyList<ServiceRun>>([skipped]));

        var result = await new PortfolioSyncGate(runs).CheckAsync(Context, Ct);

        result.Status.ShouldBe(GateStatus.Pass);
    }

    [Fact]
    public async Task The_fresh_data_gate_asks_for_the_totals_with_the_session_midnight_in_india_time()
    {
        var bars = Substitute.For<IBarQueries>();
        bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Ct).Returns(new BarTotals(100, 1000, null, new DateTimeOffset(2026, 10, 23, 15, 29, 0, Ist), 0, 0));

        var result = await new FreshDataGate(bars).CheckAsync(Context, Ct);

        result.Status.ShouldBe(GateStatus.Pass);
        await bars.Received(1).GetBarTotalsAsync(new DateTimeOffset(2026, 10, 23, 0, 0, 0, Ist), Ct);
    }

    [Fact]
    public async Task The_quality_gate_reads_the_last_finished_full_run()
    {
        var quality = Substitute.For<IQualityQueries>();
        var run = new QualityRunView(Guid.NewGuid(), "daily", "succeeded", new DateTimeOffset(2026, 10, 23, 15, 55, 0, Ist), new DateTimeOffset(2026, 10, 23, 16, 0, 0, Ist), 1, 100, 100, 0, 0, 1, null, null, null);
        quality.GetLastFinishedFullRunAsync(Ct).Returns(run);

        var result = await new QualityGate(quality).CheckAsync(Context, Ct);

        result.Status.ShouldBe(GateStatus.Pass);
        await quality.Received(1).GetLastFinishedFullRunAsync(Ct);
    }

    [Fact]
    public async Task The_selection_inputs_gate_reads_the_newest_as_of_date_without_listing_rows()
    {
        var cells = Substitute.For<ICellStatsStore>();
        cells.NewestAsOfAsync(Ct).Returns(Friday);

        var result = await new SelectionInputsGate(cells).CheckAsync(Context, Ct);

        result.Status.ShouldBe(GateStatus.Pass);
        await cells.DidNotReceive().ListLatestAsync(Arg.Any<string?>(), Arg.Any<string?>(), Ct);
    }

    [Fact]
    public async Task The_selection_inputs_gate_is_unavailable_without_a_snapshot()
    {
        var cells = Substitute.For<ICellStatsStore>();
        cells.NewestAsOfAsync(Ct).Returns((DateOnly?)null);

        (await new SelectionInputsGate(cells).CheckAsync(Context, Ct)).Status.ShouldBe(GateStatus.Unavailable);
    }
}
