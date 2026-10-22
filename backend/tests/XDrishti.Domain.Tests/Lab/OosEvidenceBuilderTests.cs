using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>Spec P5-04, criteria 4, 5 and 7: the evidence is built from the winners' test-window trades only, pooled by window.</summary>
public sealed class OosEvidenceBuilderTests
{
    private static readonly ExitCandidate A = new("FIXED_RR", [new ExitParameterValue("rr", [2])]);

    private static readonly ExitCandidate B = new("FIXED_RR", [new ExitParameterValue("rr", [3])]);

    private static DateOnly D(int month, int day) => new(2025, month, day);

    private static SelectionTrade T(string c, DateOnly entry, double r) => new(c, entry, entry.AddDays(1), r);

    private static SelectionInput Input(params SelectionTrade[] trades) => new("X 15m BUY intraday *", "RNet", false, "e", [A, B], trades, [], new SelectionOptions());

    private static SelectionFold Fold(int i, DateOnly start, DateOnly end, string? winner) =>
        new(i, start, end, null, 0, 0, 0, 0, winner, false, false, false, null, 0, null, false, []);

    private static ExitSelection Selection(SelectionInput input, params SelectionFold[] folds) =>
        new(input, A.Id, null, null, 0, folds, null, false, false, A.Id, null, [], new SelectionStability(null, string.Empty, 0, null, 0, 0, null, 0), []);

    [Fact]
    public void Only_the_trades_of_the_winner_inside_its_test_window_count_and_the_numbers_are_exact()
    {
        var input = Input(
            T(A.Id, D(7, 2), 2), T(A.Id, D(7, 3), -1), // winner A in window 1: +2 -1
            T(B.Id, D(8, 5), -2), T(B.Id, D(8, 6), 0.5), // winner B in window 2
            T(A.Id, D(6, 1), 9), T(B.Id, D(7, 2), 9)); // outside a window / not the winner: ignored
        var selection = Selection(input, Fold(1, D(7, 1), D(7, 31), A.Id), Fold(2, D(8, 1), D(8, 31), B.Id));

        var pool = OosEvidenceBuilder.Pool([(input, selection)], 0);

        pool.Evidence.OosTrades.ShouldBe(4);
        pool.Evidence.ExpectancyR.ShouldBe(-0.125, 1e-9); // (2 - 1 - 2 + 0.5) / 4
        pool.Evidence.ProfitFactor.ShouldBe(2.5 / 3, 1e-9);
        pool.Evidence.MaxDrawdownR.ShouldBe(3, 1e-9); // cumulative 2, 1, -1, -0.5: from the peak 2 to -1
        pool.Evidence.Folds.ShouldBe(2);
        pool.Evidence.PositiveFolds.ShouldBe(1);
        pool.Evidence.Method.ShouldBe(OosEvidence.WalkForward);
        pool.Windows.Select(w => w.Trades).ShouldBe([2, 2]);
    }

    [Fact]
    public void A_fold_without_a_winner_and_a_run_without_cells_give_no_trades()
    {
        var input = Input(T(A.Id, D(7, 2), 2));

        OosEvidenceBuilder.Pool([(input, Selection(input, Fold(1, D(7, 1), D(7, 31), null)))], 0).Evidence.OosTrades.ShouldBe(0);
        var none = OosEvidenceBuilder.Pool([], 0).Evidence;
        none.OosTrades.ShouldBe(0);
        none.Folds.ShouldBe(0);
        none.ProfitFactor.ShouldBe(0);
    }

    [Fact]
    public void Cells_with_the_same_test_window_pool_into_one_fold_and_a_run_with_no_loss_has_an_infinite_profit_factor()
    {
        var one = Input(T(A.Id, D(7, 2), 1));
        var two = Input(T(B.Id, D(7, 5), 2));
        var cells = new[] { (one, Selection(one, Fold(1, D(7, 1), D(7, 31), A.Id))), (two, Selection(two, Fold(1, D(7, 1), D(7, 31), B.Id))) };

        var pool = OosEvidenceBuilder.Pool(cells, 0);

        pool.Windows.Count.ShouldBe(1);
        pool.Evidence.Folds.ShouldBe(1);
        pool.Evidence.OosTrades.ShouldBe(2);
        double.IsPositiveInfinity(pool.Evidence.ProfitFactor).ShouldBeTrue();
        pool.Evidence.MaxDrawdownR.ShouldBe(0);
    }

    [Fact]
    public void The_pool_is_deterministic()
    {
        var input = Input(T(A.Id, D(7, 2), 2), T(A.Id, D(7, 3), -1));
        var selection = Selection(input, Fold(1, D(7, 1), D(7, 31), A.Id));

        OosEvidenceBuilder.Pool([(input, selection)], 0).Evidence.ShouldBe(OosEvidenceBuilder.Pool([(input, selection)], 0).Evidence);
    }

    [Fact]
    public void A_run_with_no_trades_fails_the_evaluator_with_a_reason_per_gate()
    {
        var none = OosEvidenceBuilder.Pool([], 0).Evidence;

        var verdict = VerdictEvaluator.Evaluate(QualificationGates.Default, none, 1, 6);

        verdict.Outcome.ShouldBe(VerdictOutcome.Fail);
        verdict.Gates.ShouldAllBe(g => g.Reason.Length > 0);
    }
}
