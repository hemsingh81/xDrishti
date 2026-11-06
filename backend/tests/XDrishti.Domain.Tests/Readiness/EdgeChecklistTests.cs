using XDrishti.Domain.Lab;
using XDrishti.Domain.Readiness;

namespace XDrishti.Domain.Tests.Readiness;

/// <summary>CF-11 part 1, AC1 and the checklist of D1 (ADR 0221): 15 rows, each pass, fail or unavailable; unavailable is never a pass; boundary equalities pass ("at least").</summary>
public sealed class EdgeChecklistTests
{
    private static readonly ReadinessOptions Options = ReadinessInputsFixture.Options;

    private static IReadOnlyList<ChecklistRow> Rows(ChecklistInputs inputs) => EdgeChecklist.Evaluate(inputs, Options);

    private static RowStatus Row(ChecklistInputs inputs, int number) => Rows(inputs).Single(r => r.Number == number).Status;

    [Fact]
    public void There_are_15_rows_in_order_and_with_nothing_known_every_one_is_unavailable()
    {
        var rows = Rows(new ChecklistInputs());

        rows.Select(r => r.Number).ShouldBe(Enumerable.Range(1, 15));
        rows.ShouldAllBe(r => r.Status == RowStatus.Unavailable);
        rows.ShouldAllBe(r => r.Producer.Length > 0 && r.Threshold.Length > 0);
    }

    [Fact]
    public void With_every_input_good_every_row_passes()
    {
        Rows(ReadinessInputsFixture.Passing()).ShouldAllBe(r => r.Status == RowStatus.Pass);
    }

    [Theory]
    [InlineData(2, 0.1875, 0.2, true, true)]
    [InlineData(8, 0.2625, 0.2, false, true)]
    [InlineData(64, 0.375, 0.2, false, false)]
    public void The_research_example_320_trades_at_plus_0_2_R_passes_the_t_row_and_row_3_only_at_few_trials(int trials, double need, double mean, bool row3, bool row6)
    {
        var factor = MultipleTesting.Factor(trials);
        var inputs = ReadinessInputsFixture.Passing() with { Factor = factor, ExpectancyR = mean };

        var rows = Rows(inputs);

        (factor * 0.15).ShouldBe(need, 1e-12);
        rows.Single(r => r.Number == 3).Status.ShouldBe(row3 ? RowStatus.Pass : RowStatus.Fail);
        rows.Single(r => r.Number == 6).Status.ShouldBe(row6 ? RowStatus.Pass : RowStatus.Fail);
        rows.Single(r => r.Number == 2).Status.ShouldBe(RowStatus.Pass);
    }

    [Fact]
    public void The_t_statistic_of_the_research_example_is_3_578()
    {
        var row = Rows(ReadinessInputsFixture.Passing() with { Factor = 1.25 }).Single(r => r.Number == 6);

        row.Value.ShouldBe("t 3.578");
    }

    [Theory]
    [InlineData(0.1875, true)]
    [InlineData(0.18749, false)]
    public void Row_3_passes_exactly_on_the_boundary(double expectancy, bool passes)
    {
        Row(ReadinessInputsFixture.Passing() with { Factor = 1.25, ExpectancyR = expectancy }, 3).ShouldBe(passes ? RowStatus.Pass : RowStatus.Fail);
    }

    [Theory]
    [InlineData(0.25, true)]
    [InlineData(0.2499, false)]
    public void Row_6_passes_exactly_on_the_boundary(double mean, bool passes)
    {
        // n_eff 100, sd 1: t = mean x 10; the bar is 2.0 x 1.25 = 2.5
        var inputs = ReadinessInputsFixture.Passing() with { Factor = 1.25, NEff = 100, SdR = 1, ExpectancyR = mean };

        Row(inputs, 6).ShouldBe(passes ? RowStatus.Pass : RowStatus.Fail);
    }

    [Theory]
    [InlineData(300.0, 30, RowStatus.Pass)]
    [InlineData(299.9, 30, RowStatus.Fail)]
    [InlineData(320.0, 29, RowStatus.Fail)]
    [InlineData(299.9, null, RowStatus.Fail)]
    [InlineData(null, 30, RowStatus.Unavailable)]
    [InlineData(320.0, null, RowStatus.Unavailable)]
    public void Row_2_needs_300_independent_trades_and_30_per_cell(double? nEff, int? perCell, RowStatus expected)
    {
        Row(ReadinessInputsFixture.Passing() with { NEff = nEff, MinTradesPerCell = perCell }, 2).ShouldBe(expected);
    }

    [Fact]
    public void Row_3_and_row_6_are_unavailable_without_the_factor_or_the_spread()
    {
        Row(ReadinessInputsFixture.Passing() with { Factor = null }, 3).ShouldBe(RowStatus.Unavailable);
        Row(ReadinessInputsFixture.Passing() with { Factor = null }, 6).ShouldBe(RowStatus.Unavailable);
        Row(ReadinessInputsFixture.Passing() with { SdR = null }, 6).ShouldBe(RowStatus.Unavailable);
        Row(ReadinessInputsFixture.Passing() with { SdR = 0 }, 6).ShouldBe(RowStatus.Unavailable);
        Row(ReadinessInputsFixture.Passing() with { ExpectancyR = null }, 3).ShouldBe(RowStatus.Unavailable);
    }

    [Fact]
    public void A_fail_in_one_part_wins_over_an_unavailable_part_in_a_two_part_row()
    {
        Row(ReadinessInputsFixture.Passing() with { CostStress15 = RowStatus.Fail, CostStress2 = RowStatus.Unavailable }, 4).ShouldBe(RowStatus.Fail);
        Row(ReadinessInputsFixture.Passing() with { CostStress15 = RowStatus.Pass, CostStress2 = RowStatus.Unavailable }, 4).ShouldBe(RowStatus.Unavailable);
        Row(ReadinessInputsFixture.Passing() with { Placebo = RowStatus.Pass, Mirror = RowStatus.Fail }, 8).ShouldBe(RowStatus.Fail);
        Row(ReadinessInputsFixture.Passing() with { Placebo = RowStatus.Unavailable }, 8).ShouldBe(RowStatus.Unavailable);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(12)]
    public void A_row_another_slice_judges_takes_its_word_and_unavailable_is_not_a_pass(int row)
    {
        ChecklistInputs With(RowStatus s) => row switch
        {
            5 => ReadinessInputsFixture.Passing() with { FillDelay = s },
            7 => ReadinessInputsFixture.Passing() with { RandomEntry = s },
            9 => ReadinessInputsFixture.Passing() with { Plateau = s },
            _ => ReadinessInputsFixture.Passing() with { HoldOut = s },
        };

        Row(With(RowStatus.Pass), row).ShouldBe(RowStatus.Pass);
        Row(With(RowStatus.Fail), row).ShouldBe(RowStatus.Fail);
        Row(With(RowStatus.Unavailable), row).ShouldBe(RowStatus.Unavailable);
    }

    [Theory]
    [InlineData(0.70, 0.60, 0.15, RowStatus.Pass)]
    [InlineData(0.69, 0.60, 0.15, RowStatus.Fail)]
    [InlineData(0.70, 0.61, 0.15, RowStatus.Fail)]
    [InlineData(0.70, 0.60, 0.1501, RowStatus.Fail)]
    [InlineData(null, 0.50, 0.10, RowStatus.Unavailable)]
    [InlineData(null, 0.70, 0.10, RowStatus.Fail)]
    public void Row_10_judges_the_quarters_the_year_and_the_instrument(double? quarters, double? year, double? instrument, RowStatus expected)
    {
        Row(ReadinessInputsFixture.Passing() with { QuarterShare = quarters, MaxYearShare = year, MaxInstrumentShare = instrument }, 10).ShouldBe(expected);
    }

    [Theory]
    [InlineData(8.0, 8.0, 20.0, RowStatus.Pass)]
    [InlineData(8.1, 8.0, 20.0, RowStatus.Fail)]
    [InlineData(5.0, 8.0, 20.01, RowStatus.Fail)]
    [InlineData(5.0, null, 12.0, RowStatus.Unavailable)]
    [InlineData(5.0, null, 21.0, RowStatus.Fail)]
    [InlineData(null, 8.0, 12.0, RowStatus.Unavailable)]
    public void Row_11_needs_the_observed_drawdown_within_the_90th_percentile_and_the_95th_under_the_budget(double? observed, double? p90, double? p95, RowStatus expected)
    {
        Row(ReadinessInputsFixture.Passing() with { ObservedMaxDrawdownR = observed, McDrawdownP90 = p90, McDrawdownP95 = p95 }, 11).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0.275, RowStatus.Pass)]
    [InlineData(0.2751, RowStatus.Fail)]
    public void Row_15_needs_the_forward_mean_within_1_5_standard_errors_of_the_out_of_sample_mean(double forward, RowStatus expected)
    {
        // oos mean 0.20, SE 0.05: 1.5 SE = 0.075
        Row(ReadinessInputsFixture.Passing() with { ForwardMeanR = forward }, 15).ShouldBe(expected);
    }

    [Fact]
    public void Row_15_needs_all_three_parts()
    {
        Row(ReadinessInputsFixture.Passing() with { TwentyTradeCheckPassed = null }, 15).ShouldBe(RowStatus.Unavailable);
        Row(ReadinessInputsFixture.Passing() with { PhaseCheckMet = false }, 15).ShouldBe(RowStatus.Fail);
        Row(ReadinessInputsFixture.Passing() with { ForwardStandardError = null }, 15).ShouldBe(RowStatus.Unavailable);
        Row(ReadinessInputsFixture.Passing() with { ForwardStandardError = 0 }, 15).ShouldBe(RowStatus.Unavailable);
    }

    [Fact]
    public void Rows_1_13_and_14_are_the_flags_of_their_producers()
    {
        Row(ReadinessInputsFixture.Passing() with { Registered = false }, 1).ShouldBe(RowStatus.Fail);
        Row(ReadinessInputsFixture.Passing() with { BenchmarkBeaten = null }, 13).ShouldBe(RowStatus.Unavailable);
        Row(ReadinessInputsFixture.Passing() with { CapacityOk = false }, 14).ShouldBe(RowStatus.Fail);
    }

    [Fact]
    public void A_producer_note_is_shown_beside_a_row_that_is_not_a_pass_and_only_there()
    {
        var notes = new Dictionary<int, string> { [13] = "no benchmark producer yet", [1] = "should not show" };

        var rows = Rows(ReadinessInputsFixture.Passing() with { BenchmarkBeaten = null, Notes = notes });

        rows.Single(r => r.Number == 13).Value.ShouldContain("no benchmark producer yet");
        rows.Single(r => r.Number == 1).Value.ShouldNotContain("should not show");
    }

    [Fact]
    public void A_stricter_setting_fails_a_row_that_the_default_passes()
    {
        var strict = Options with { MinNEff = 400 };

        EdgeChecklist.Evaluate(ReadinessInputsFixture.Passing(), strict).Single(r => r.Number == 2).Status.ShouldBe(RowStatus.Fail);
    }
}
