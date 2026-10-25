#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Reporting;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>P8-10 part b: the figures of a stored exit choice are values only with a record figure and at least the minimum sample (30 trades); the boundary is hand-checked.</summary>
public sealed class ListReportExitSelectionsHandlerTests
{
    private static ReportExitSelectionInfo Info(IReadOnlyList<ReportPooledFigure> pooled, IReadOnlyList<ReportSelectionFold> folds) =>
        new("k", "c", "ORB", "15m", "BUY", "intraday", "*", "RFilled", "A", null, "no stable winner", null, "fewer than 2 usable folds", 0, 0, 0, null, 0, pooled, folds, ["in-sample"], DateTimeOffset.UnixEpoch);

    private static async Task<ExitSelectionView> RunAsync(ReportExitSelectionInfo info)
    {
        var source = Substitute.For<IReportComparisonSource>();
        source.ListExitSelectionsAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ReportExitSelectionInfo>>([info]));

        var result = await new ListReportExitSelectionsHandler(source).HandleAsync(new ListReportExitSelectionsQuery(), CancellationToken.None);

        return result.Value.Selections.Single();
    }

    [Theory]
    [InlineData(29, "insufficient")]
    [InlineData(30, "value")]
    public async Task A_pooled_figure_is_a_value_only_from_the_minimum_sample(int trades, string state)
    {
        var view = await RunAsync(Info([new ReportPooledFigure("p", trades, 0.2, 0.1)], []));

        view.Pooled.Single().MeanR.State.ShouldBe(state);
        (view.Pooled.Single().Score.Number is null).ShouldBe(state != "value");
    }

    [Fact]
    public async Task A_figure_the_record_lacks_is_unavailable_and_a_fold_without_a_winner_is_empty_with_no_number()
    {
        var view = await RunAsync(Info(
            [new ReportPooledFigure("p", 100, null, null)],
            [new ReportSelectionFold(1, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), null, 0, 0.5, true)]));

        view.Pooled.Single().MeanR.State.ShouldBe("unavailable");
        view.Folds.Single().WinnerTestScore.State.ShouldBe("empty");
        view.Folds.Single().WinnerTestScore.Number.ShouldBeNull();
        view.Stable.ShouldBeNull();
        view.ChosenCandidateId.ShouldBeNull();
    }

    [Theory]
    [InlineData(40, 0, "value")]
    [InlineData(40, 50, "insufficient")]
    [InlineData(50, 50, "value")]
    public async Task The_larger_of_the_report_minimum_and_the_records_own_applies(int trades, int recordMin, string state)
    {
        var info = Info([new ReportPooledFigure("p", trades, 0.2, 0.1)], []) with { MinTrades = recordMin };

        var view = await RunAsync(info);

        view.Pooled.Single().MeanR.State.ShouldBe(state);
        view.MinTrades.ShouldBe(Math.Max(30, recordMin));
    }
}
