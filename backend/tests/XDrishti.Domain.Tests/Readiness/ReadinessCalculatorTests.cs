using XDrishti.Domain.Readiness;

namespace XDrishti.Domain.Tests.Readiness;

/// <summary>CF-11 part 1, AC4 to AC7 (ADR 0221): the score is the minimum of the sub-scores, the hard fails turn it into 0 with the fault named, unavailable is never a pass, and the labels cut at the spec's numbers.</summary>
public sealed class ReadinessCalculatorTests
{
    private static readonly ReadinessOptions Options = ReadinessInputsFixture.Options;

    private static ReadinessScore Score(ScoreInputs inputs, HardFailInputs? faults = null) => ReadinessCalculator.Compute(inputs, faults ?? HardFailInputs.AllClear, Options);

    [Fact]
    public void The_research_hand_example_scores_20_2_and_the_weakest_link_is_the_power()
    {
        // n_eff 45 (60.7), lower bound 0.05 (25), power 38.08 % (20.2), inside the band (50), ECE 6 pp (40), 5 of 6 folds and no alarm (100)
        var inputs = new ScoreInputs
        {
            ForwardNEff = 45,
            ForwardMeanR = 0.05 + (1.645 / Math.Sqrt(45)),
            ForwardSdR = 1.0,
            Band = BandPosition.InsideBand,
            ModelApplies = true,
            EcePp = 6,
            Folds = 6,
            PositiveFolds = 5,
            DriftAlarm = false,
        };

        var score = Score(inputs);

        score.SubScores.Select(s => Math.Round(s.Score!.Value, 1)).ShouldBe([60.7, 25, 20.2, 50, 40, 100]);
        score.Score.ShouldBe(20.2, 0.05);
        score.Label.ShouldBe(ReadinessLabel.NotReady);
        score.Weakest!.Name.ShouldBe(SubScore.PowerName);
        score.HardFails.ShouldBeEmpty();
    }

    [Fact]
    public void The_score_is_the_minimum_and_not_the_mean()
    {
        var inputs = ReadinessInputsFixture.Perfect() with { EcePp = 7 };

        var score = Score(inputs);

        score.SubScores.Select(s => s.Score!.Value).ShouldBe([100, 100, 100, 100, 30, 100]);
        score.Score.ShouldBe(30, 1e-9);
        score.SubScores.Average(s => s.Score!.Value).ShouldBe(88.33, 0.01);
        score.Weakest!.Name.ShouldBe(SubScore.CalibrationName);
    }

    [Fact]
    public void A_calibration_that_does_not_apply_is_left_out_of_the_minimum_and_listed()
    {
        var inputs = ReadinessInputsFixture.Perfect() with { ModelApplies = false, EcePp = null };

        var score = Score(inputs);

        score.Score.ShouldBe(100);
        score.HardFails.ShouldBeEmpty();
        score.SubScores.Single(s => s.Name == SubScore.CalibrationName).State.ShouldBe(SubScoreState.NotApplicable);
        score.NotApplicable.ShouldContain(n => n.StartsWith(SubScore.CalibrationName, StringComparison.Ordinal));
    }

    [Fact]
    public void Perfect_inputs_with_no_fault_score_100_and_are_ready()
    {
        var score = Score(ReadinessInputsFixture.Perfect());

        score.Score.ShouldBe(100);
        score.Label.ShouldBe(ReadinessLabel.ReadyForNextPhase);
        score.HardFails.ShouldBeEmpty();
    }

    public static TheoryData<string, HardFailInputs> EachTerm => new()
    {
        { ReadinessCalculator.IntegrityName, HardFailInputs.AllClear with { Integrity = FaultState.Fault } },
        { ReadinessCalculator.CostSheetName, HardFailInputs.AllClear with { CostSheet = FaultState.Fault } },
        { ReadinessCalculator.FailureBoundaryName, HardFailInputs.AllClear with { FailureBoundary = FaultState.Fault } },
        { ReadinessCalculator.HoldOutName, HardFailInputs.AllClear with { HoldOutReused = FaultState.Fault } },
        { ReadinessCalculator.TierName, HardFailInputs.AllClear with { TierCOnly = FaultState.Fault } },
        { ReadinessCalculator.KillName, HardFailInputs.AllClear with { KillDemotion = FaultState.Fault } },
    };

    [Theory]
    [MemberData(nameof(EachTerm))]
    public void Each_hard_fail_set_alone_gives_0_not_ready_and_names_itself(string name, HardFailInputs faults)
    {
        var score = Score(ReadinessInputsFixture.Perfect(), faults);

        score.Score.ShouldBe(0);
        score.Label.ShouldBe(ReadinessLabel.NotReady);
        score.HardFails.ShouldHaveSingleItem().ShouldBe(name);
        score.RawMinimum.ShouldBe(100, "the score without the fault is still shown");
    }

    [Fact]
    public void An_unavailable_input_of_an_applicable_sub_score_is_a_hard_fail()
    {
        var score = Score(ReadinessInputsFixture.Perfect() with { ForwardNEff = null });

        score.Score.ShouldBe(0);
        score.Label.ShouldBe(ReadinessLabel.NotReady);
        score.HardFails.ShouldNotBeEmpty();
        score.Weakest!.State.ShouldBe(SubScoreState.Unavailable);
    }

    [Theory]
    [InlineData(FaultState.Unavailable)]
    public void A_term_whose_input_is_unavailable_is_a_hard_fail_and_never_a_pass(FaultState state)
    {
        var score = Score(ReadinessInputsFixture.Perfect(), HardFailInputs.AllClear with { TierCOnly = state });

        score.Score.ShouldBe(0);
        score.HardFails.ShouldHaveSingleItem().ShouldContain("unavailable");
    }

    [Fact]
    public void A_term_that_does_not_apply_is_not_a_hard_fail_and_is_listed()
    {
        var score = Score(ReadinessInputsFixture.Perfect(), HardFailInputs.AllClear with { KillDemotion = FaultState.NotApplicable });

        score.Score.ShouldBe(100);
        score.HardFails.ShouldBeEmpty();
        score.NotApplicable.ShouldContain(ReadinessCalculator.KillName);
    }

    [Fact]
    public void The_default_hard_fail_inputs_are_unavailable_and_so_are_a_hard_fail()
    {
        var score = Score(ReadinessInputsFixture.Perfect(), new HardFailInputs());

        score.Score.ShouldBe(0);
        score.HardFails.Count.ShouldBe(6, "integrity, cost sheet, failure boundary, hold-out, tier and the kill term");
    }

    [Theory]
    [InlineData(39.9, ReadinessLabel.NotReady)]
    [InlineData(40, ReadinessLabel.Building)]
    [InlineData(69.9, ReadinessLabel.Building)]
    [InlineData(70, ReadinessLabel.UsableWithAdvice)]
    [InlineData(84.9, ReadinessLabel.UsableWithAdvice)]
    [InlineData(85, ReadinessLabel.ReadyForNextPhase)]
    [InlineData(100, ReadinessLabel.ReadyForNextPhase)]
    [InlineData(0, ReadinessLabel.NotReady)]
    [InlineData(double.NaN, ReadinessLabel.NotReady)]
    public void The_labels_cut_at_40_70_and_85_and_a_cut_off_takes_the_higher_label(double score, ReadinessLabel expected)
    {
        ReadinessLabels.Of(score, Options).ShouldBe(expected);
    }

    [Fact]
    public void A_raised_cut_off_gives_a_lower_label_for_the_same_score()
    {
        var strict = Options with { UsableFrom = 80, ReadyFrom = 90 };

        ReadinessLabels.Of(75, Options).ShouldBe(ReadinessLabel.UsableWithAdvice);
        ReadinessLabels.Of(75, strict).ShouldBe(ReadinessLabel.Building);
        ReadinessLabels.Of(87, strict).ShouldBe(ReadinessLabel.UsableWithAdvice);
    }

    [Fact]
    public void The_label_words_are_the_specs()
    {
        ReadinessLabels.Text(ReadinessLabel.NotReady).ShouldBe("Not ready");
        ReadinessLabels.Text(ReadinessLabel.Building).ShouldBe("Building");
        ReadinessLabels.Text(ReadinessLabel.UsableWithAdvice).ShouldBe("Usable with advice");
        ReadinessLabels.Text(ReadinessLabel.ReadyForNextPhase).ShouldBe("Ready for the next phase");
    }

    [Fact]
    public void What_would_move_the_evidence_is_computed_from_the_anchor()
    {
        var score = Score(ReadinessInputsFixture.Perfect() with { ForwardNEff = 45 });

        score.SubScores.Single(s => s.Name == SubScore.EvidenceName).WhatWouldMoveIt.ShouldBe("55 more effective trades reach 100");
    }

    [Fact]
    public void What_would_move_the_other_sub_scores_names_the_distance_to_the_anchor()
    {
        var inputs = ReadinessInputsFixture.Perfect() with { ForwardNEff = 45, EcePp = 6, PositiveMonthShare = 0.5, ForwardMeanR = 0.05 + (1.645 / Math.Sqrt(45)) };

        var subs = Score(inputs).SubScores.ToDictionary(s => s.Name, s => s.WhatWouldMoveIt);

        subs[SubScore.EffectName].ShouldBe("the lower 90 % bound must rise by 0.1 R to reach 0.15 R");
        subs[SubScore.PowerName].ShouldBe("about 155 effective trades (110 more) reach 80 % power");
        subs[SubScore.AgreementName].ShouldBe("at least 70 % of months with 5 or more trades must be positive");
        subs[SubScore.CalibrationName].ShouldBe("the calibration error must fall by 3 pp to reach 3 pp");
        subs[SubScore.StabilityName].ShouldBeNull();
    }

    [Theory]
    [InlineData(6, 3, false, 0)]
    [InlineData(6, 4, false, 50)]
    [InlineData(6, 5, false, 100)]
    [InlineData(6, 6, true, 0)]
    [InlineData(6, 6, false, 100)]
    [InlineData(4, 4, false, 50)]
    [InlineData(5, 5, false, 50)]
    [InlineData(2, 1, false, 0)]
    public void Stability_follows_the_folds_and_the_alarm_and_is_capped_at_50_below_six_folds(int folds, int positive, bool alarm, double expected)
    {
        var score = Score(ReadinessInputsFixture.Perfect() with { Folds = folds, PositiveFolds = positive, DriftAlarm = alarm });

        score.SubScores.Single(s => s.Name == SubScore.StabilityName).Score.ShouldBe(expected);
    }

    [Theory]
    [InlineData(BandPosition.BelowBand, 0.9, 0)]
    [InlineData(BandPosition.InsideBand, 0.7, 100)]
    [InlineData(BandPosition.InsideBand, 0.69, 50)]
    [InlineData(BandPosition.InsideBand, null, 50)]
    public void Backtest_agreement_is_0_below_the_band_50_inside_and_100_with_positive_months(BandPosition band, double? months, double expected)
    {
        var score = Score(ReadinessInputsFixture.Perfect() with { Band = band, PositiveMonthShare = months });

        score.SubScores.Single(s => s.Name == SubScore.AgreementName).Score.ShouldBe(expected);
    }

    [Fact]
    public void The_score_is_the_minimum_of_the_scored_sub_scores_or_0_and_stays_between_0_and_100()
    {
        var random = new Random(11);
        for (var i = 0; i < 300; i++)
        {
            var inputs = new ScoreInputs
            {
                ForwardNEff = random.NextDouble() * 300,
                ForwardMeanR = (random.NextDouble() * 1.5) - 0.5,
                ForwardSdR = 0.2 + (random.NextDouble() * 2),
                Band = random.Next(2) == 0 ? BandPosition.BelowBand : BandPosition.InsideBand,
                PositiveMonthShare = random.NextDouble(),
                ModelApplies = random.Next(2) == 0,
                EcePp = random.NextDouble() * 15,
                Folds = random.Next(1, 9),
                PositiveFolds = 0,
                DriftAlarm = random.Next(2) == 0,
            };
            inputs = inputs with { PositiveFolds = random.Next(0, inputs.Folds!.Value + 1) };

            var score = Score(inputs);

            score.Score.ShouldBeInRange(0, 100);
            score.RawMinimum.ShouldBe(score.SubScores.Where(s => s.State == SubScoreState.Scored).Min(s => s.Score!.Value));
            score.Score.ShouldBe(score.HardFails.Count > 0 ? 0 : score.RawMinimum);
        }
    }
}
