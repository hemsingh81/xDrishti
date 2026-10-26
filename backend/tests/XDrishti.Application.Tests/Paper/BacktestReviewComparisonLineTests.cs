#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Paper;

/// <summary>Spec P10-06 part B: the weekly review's backtest line reads the P10-04 report; a report with no placed mean says so with its reason and prints no mean, band or interval; unconfirmed costs stay visible; nothing is an instruction to trade.</summary>
public sealed class BacktestReviewComparisonLineTests
{
    private readonly IQueryHandler<GetBacktestVsPaperQuery, Result<BacktestVsPaperView>> _comparison = Substitute.For<IQueryHandler<GetBacktestVsPaperQuery, Result<BacktestVsPaperView>>>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private BacktestReviewComparisonLine Line() => new(_comparison, NullLogger<BacktestReviewComparisonLine>.Instance);

    private static MetricView Num(double v) => new("value", v, null);

    private static BacktestVsPaperView View(long? epoch, MetricView mean, MetricView p05, MetricView p95, string? position, bool confirmed, string? warning = null, int n = 61)
    {
        var none = new MetricView("insufficient", null, "x");
        var verdict = new CompareVerdictView(confirmed ? "met" : "not_evaluable", confirmed ? "ok" : "not evaluable: costs not confirmed");
        return new BacktestVsPaperView(
            epoch, n, new CompareCountsView(n, 0, 0, 0, 0), "net", new CompareChargeSheetView("flat-10", confirmed ? new DateOnly(2026, 10, 30) : null),
            new ComparePaperView(mean, new CompareIntervalView(none, none), none), new CompareBandView(p05, none, p95, none, position, 10_000, null), new CompareDrawdownView(none, none, none, null),
            new CompareCriteriaView(verdict, verdict), warning, [], [], new CompareSensitivityView("s", 0, none), new CompareEvidenceView(null, null, null, null, null, [], "s"), []);
    }

    private void Returns(BacktestVsPaperView view) =>
        _comparison.HandleAsync(Arg.Any<GetBacktestVsPaperQuery>(), Ct).Returns(ValueTask.FromResult(Result.Success(view)));

    [Fact]
    public async Task Too_few_trades_says_so_with_the_reason_and_no_mean_band_or_interval()
    {
        var gone = new MetricView("insufficient", null, "12 counted trades; at least 30 are needed");
        Returns(View(1, gone, gone, gone, null, false, n: 12));

        var line = await Line().LineAsync(1, Ct);

        line.ShouldStartWith("insufficient: no position against the backtest band is given (12 counted trades; at least 30 are needed).");
        line.ShouldContain("Costs are not confirmed");
        line.ShouldNotContain("R ");
        line.ShouldNotContain("band [");
        line.ShouldContain("not an instruction to trade");
    }

    [Theory]
    [InlineData("unavailable", "no cell snapshot before the epoch started")]
    [InlineData("not_evaluable", "basis differs in 2 cells")]
    public async Task An_unavailable_or_not_evaluable_report_says_its_state_and_reason(string state, string reason)
    {
        var gone = new MetricView(state, null, reason);
        Returns(View(1, gone, gone, gone, null, true));

        var line = await Line().LineAsync(1, Ct);

        line.ShouldStartWith(state + ": no position against the backtest band is given (" + reason + ").");
        line.ShouldNotContain("Costs are not confirmed");
    }

    [Fact]
    public async Task A_placed_mean_is_printed_with_position_verdicts_and_the_costs_note()
    {
        Returns(View(1, Num(0.1234), Num(-0.3), Num(0.3), "within", false, "paper is clearly worse than the backtest allows: stop and review before more trades"));

        var line = await Line().LineAsync(1, Ct);

        line.ShouldContain("Paper mean +0.123R over 61 counted trades is within the backtest band [-0.300R, +0.300R]");
        line.ShouldContain("expectancy not_evaluable, drawdown not_evaluable");
        line.ShouldContain("Costs are not confirmed by the owner");
        line.ShouldContain("EARLY WARNING:");
        line.ShouldEndWith("Simulated on both sides; not an instruction to trade.");
    }

    [Fact]
    public async Task No_epoch_and_a_failed_read_are_unavailable_lines()
    {
        Returns(View(null, Num(0), Num(0), Num(0), null, false));
        var none = await Line().LineAsync(1, Ct);
        _comparison.HandleAsync(Arg.Any<GetBacktestVsPaperQuery>(), Ct).Returns(ValueTask.FromResult(Result.Failure<BacktestVsPaperView>(PaperErrors.NotFound("epoch"))));
        var failed = await Line().LineAsync(1, Ct);

        none.ShouldStartWith("unavailable: no paper epoch yet");
        failed.ShouldStartWith("unavailable: the backtest comparison could not be read (paper.not_found)");
    }
}
