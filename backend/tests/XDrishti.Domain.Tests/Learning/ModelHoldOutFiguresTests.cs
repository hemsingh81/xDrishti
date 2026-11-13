using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-10e part 3 (ADR 0285): the figures of a model's one look at the hold-out.</summary>
public sealed class ModelHoldOutFiguresTests
{
    private static readonly DateOnly Start = new(2026, 11, 2);

    private static List<ModelHoldOutRow> Rows(int days, int perDay, Func<int, double> pWin, Func<int, bool> won, Func<int, double> r, double cellRate = 0.5)
    {
        var rows = new List<ModelHoldOutRow>();
        var n = 0;
        for (var d = 0; d < days; d++)
        {
            for (var k = 0; k < perDay; k++, n++)
            {
                rows.Add(new ModelHoldOutRow("t" + n.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), Start.AddDays(d), pWin(n), won(n), r(n), cellRate));
            }
        }

        return rows;
    }

    [Fact]
    public void A_model_that_separates_winners_beats_the_baseline_with_a_high_probability()
    {
        var rows = Rows(30, 5, n => n % 2 == 0 ? 0.8 : 0.2, n => n % 2 == 0, n => n % 2 == 0 ? 1 : -1);

        var result = ModelHoldOutFigures.Compute(rows, 0.5, 2000, 0.9, 10, "k").Value;

        result.Rows.ShouldBe(150);
        result.Days.ShouldBe(30);
        result.LogLossModel.ShouldBeLessThan(result.LogLossBaseline);
        result.ProbabilityBetter.ShouldNotBeNull().ShouldBeGreaterThan(0.99);
        result.PolicyEffectR.ShouldBeGreaterThan(0, "taking only the model's winners lifts R per candidate");
    }

    [Fact]
    public void A_model_that_is_noise_does_not_beat_the_baseline()
    {
        var rows = Rows(30, 5, n => n % 3 == 0 ? 0.9 : 0.1, n => n % 2 == 0, n => n % 2 == 0 ? 1 : -1);

        var result = ModelHoldOutFigures.Compute(rows, 0.5, 2000, 0.9, 10, "k").Value;

        result.LogLossModel.ShouldBeGreaterThan(result.LogLossBaseline);
        result.ProbabilityBetter.ShouldNotBeNull().ShouldBeLessThan(0.5);
    }

    [Fact]
    public void The_better_baseline_is_the_one_with_the_lower_log_loss_so_it_is_never_picked_in_the_models_favour()
    {
        // the cell rate tells the truth (0.9 for winning cells), the base rate does not
        var rows = Rows(20, 5, _ => 0.6, n => n % 2 == 0, _ => 0).Select(r => r with { CellRate = r.Won ? 0.9 : 0.1 }).ToList();

        var result = ModelHoldOutFigures.Compute(rows, 0.5, 2000, 0.9, 10, "k").Value;

        result.BaselineName.ShouldBe(ModelHoldOutFigures.CellRate);
    }

    [Fact]
    public void The_take_rule_effect_is_the_kept_R_minus_taking_all_per_candidate()
    {
        var rows = new List<ModelHoldOutRow>
        {
            new("a", Start, 0.9, true, 2, 0.5),
            new("b", Start.AddDays(1), 0.1, false, -1, 0.5),
        };

        var result = ModelHoldOutFigures.Compute(rows, 0.5, 2000, 0.9, 10, "k").Value;

        // kept {a}: 2; all: 1; per candidate (2 - 1) / 2
        result.PolicyEffectR.ShouldBe(0.5, 1e-12);
    }

    [Fact]
    public void Too_few_trade_days_leave_the_bootstrap_figures_unavailable_never_a_pass()
    {
        var rows = Rows(3, 40, n => n % 2 == 0 ? 0.8 : 0.2, n => n % 2 == 0, n => 1);

        var result = ModelHoldOutFigures.Compute(rows, 0.5, 2000, 0.9, 10, "k").Value;

        result.ProbabilityBetter.ShouldBeNull();
        result.GainLow.ShouldBeNull();
        result.Rows.ShouldBe(120);
    }

    [Fact]
    public void No_rows_is_a_failure_with_its_code()
    {
        var result = ModelHoldOutFigures.Compute([], 0.5, 2000, 0.9, 10, "k");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ModelHoldOutFigures.NoRowsCode);
    }

    [Fact]
    public void The_same_rows_and_seed_repeat_exactly()
    {
        var rows = Rows(20, 5, n => n % 2 == 0 ? 0.7 : 0.3, n => n % 3 != 0, n => 0.1);

        ModelHoldOutFigures.Compute(rows, 0.5, 2000, 0.9, 10, "k").Value
            .ShouldBe(ModelHoldOutFigures.Compute([.. rows.AsEnumerable().Reverse()], 0.5, 2000, 0.9, 10, "k").Value);
    }
}
