using NSubstitute;
using XDrishti.Application.Confidence;
using XDrishti.Application.Lab;
using XDrishti.Application.Paper;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Readiness;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>CF-11 part 1 (ADR 0221): the scorecard assembled from scripted producers. A producer that does not exist is an input that does not exist, and an input that does not exist is a fail, never a pass.</summary>
public sealed class ScorecardReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ScorecardView> CardAsync(ReadinessFixture f) => await f.Reader.BuildAsync(ReadinessFixture.Strategy, 1, Ct);

    private static string Status(ScorecardView card, int row) => card.Rows.Single(r => r.Number == row).Status;

    [Fact]
    public async Task With_every_producer_answering_and_nothing_wrong_every_row_passes_and_the_score_is_ready()
    {
        var f = new ReadinessFixture();

        var card = await CardAsync(f);

        card.Rows.Count.ShouldBe(15);
        card.Rows.Where(r => r.Status != "pass").Select(r => $"{r.Number}: {r.Value}").ShouldBeEmpty();
        card.RowsPassed.ShouldBe(15);
        card.GateR.Passed.ShouldBeTrue();
        card.GateS.Passed.ShouldBeTrue();
        card.Score.ShouldBe(100);
        card.Label.ShouldBe("ready");
        card.HardFails.ShouldBeEmpty();
        card.SubScores.Single(s => s.Name == SubScore.CalibrationName).State.ShouldBe("not_applicable", "no champion model, so no calibration");
        card.SubScores.Where(s => s.State == "scored").ShouldAllBe(s => s.Score > 0);
        card.Strategy.ShouldBe("ORB");
        card.Version.ShouldBe(1);
    }

    [Fact]
    public async Task The_real_gaps_say_unavailable_so_the_rows_without_a_producer_fail_and_the_card_is_not_ready()
    {
        var f = new ReadinessFixture { Gaps = new UnavailableReadinessGaps() };

        var card = await CardAsync(f);

        foreach (var row in new[] { 13, 14, 15 })
        {
            Status(card, row).ShouldBe("unavailable", $"row {row}");
        }

        Status(card, 10).ShouldBe("pass", "rows 10 and 11 read the Lab verdict since SC-S1, not the gaps");
        Status(card, 11).ShouldBe("pass");
        Status(card, 2).ShouldBe("pass");
        Status(card, 3).ShouldBe("pass");
        card.GateR.Passed.ShouldBeFalse();
        card.GateS.Passed.ShouldBeFalse();
        card.Score.ShouldBe(0);
        card.Label.ShouldBe("not_ready");
        card.HardFails.ShouldContain(h => h.Contains("integrity fault", StringComparison.Ordinal) && h.Contains("unavailable", StringComparison.Ordinal));
        card.HardFails.ShouldContain(h => h.Contains("tier C", StringComparison.Ordinal));
        card.Waiting.ShouldContain(w => w.Contains("row 13", StringComparison.Ordinal));
        card.Rows.Single(r => r.Number == 13).Value.ShouldContain("no same-universe benchmark producer");
        card.RawMinimum.ShouldBeGreaterThan(0, "the score without the faults is still shown");
    }

    [Fact]
    public async Task With_no_lab_run_the_lab_rows_are_unavailable_and_the_card_says_what_to_run()
    {
        var f = new ReadinessFixture { Verdict = null };

        var card = await CardAsync(f);

        foreach (var row in new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 })
        {
            Status(card, row).ShouldNotBe("pass", $"row {row}");
        }

        card.Rows.Single(r => r.Number == 2).Value.ShouldContain("No finished full Lab run");
        card.GateR.Passed.ShouldBeFalse();
        card.Notes.ShouldContain(n => n.Contains("No finished full Lab run", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_strategy_with_no_version_reads_no_lab_result_at_all()
    {
        var f = new ReadinessFixture();

        var card = await f.Reader.BuildAsync(ReadinessFixture.Strategy, null, Ct);

        card.Version.ShouldBeNull();
        card.Rows.Single(r => r.Number == 1).Status.ShouldBe("unavailable");
        await f.Runs.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_missing_registration_is_a_fail_and_an_unreadable_one_is_unavailable()
    {
        var missing = new ReadinessFixture { RegistrationResult = Result.Failure<RegistrationView>(Error.NotFound("lab.not_found", "none")) };
        var unreadable = new ReadinessFixture { RegistrationResult = Result.Failure<RegistrationView>(Error.Upstream("x", "down")) };

        Status(await CardAsync(missing), 1).ShouldBe("fail");
        Status(await CardAsync(unreadable), 1).ShouldBe("unavailable");
    }

    [Fact]
    public async Task A_hold_out_not_yet_unlocked_is_unavailable_and_a_reused_one_is_a_hard_fail()
    {
        var none = new ReadinessFixture { HoldOutResults = [] };
        var reused = new ReadinessFixture();
        reused.HoldOutResults = [reused.HoldOutResults[0] with { ReusedWindow = true }];

        var noneCard = await CardAsync(none);
        var reusedCard = await CardAsync(reused);

        Status(noneCard, 12).ShouldBe("unavailable");
        noneCard.Rows.Single(r => r.Number == 12).Value.ShouldContain("not been unlocked");
        Status(reusedCard, 12).ShouldBe("fail");
        reusedCard.HardFails.ShouldContain(ReadinessCalculator.HoldOutName);
        reusedCard.Score.ShouldBe(0);
    }

    [Fact]
    public async Task A_producer_that_judged_fail_is_taken_at_its_word()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { Nulls = f.Verdict.Nulls! with { Mirror = f.Verdict.Nulls.Mirror! with { Status = "fail" } } };

        var card = await CardAsync(f);

        Status(card, 8).ShouldBe("fail");
        card.GateR.Passed.ShouldBeFalse();
        card.GateR.Failing.Select(r => r.Number).ShouldBe([8]);
    }

    // ---- rows 10 and 11 from the Lab verdict (SC-S1, ADR 0222) ----

    private static readonly System.Text.Json.JsonSerializerOptions WebJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static QuarterView Q(int year, int quarter, double mean) => new(year, quarter, 50, mean);

    private static ReadinessFixture WithRobustness(Func<RobustnessView, RobustnessView> change)
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { Robustness = change(f.Verdict!.Robustness!) };
        return f;
    }

    [Fact]
    public async Task Row_10_passes_with_quarters_years_and_instruments_inside_their_limits()
    {
        var card = await CardAsync(new ReadinessFixture());

        var row = card.Rows.Single(r => r.Number == 10);

        row.Status.ShouldBe("pass");
        row.Value.ShouldContain("quarters 80 %");
        row.Value.ShouldContain("largest instrument 10 %");
    }

    [Fact]
    public async Task Row_10_fails_with_two_of_three_quarters_at_or_above_the_floor()
    {
        var f = WithRobustness(r => r with { ByQuarter = [Q(2025, 1, 0.1), Q(2025, 2, 0.2), Q(2025, 3, -0.4)] });

        var card = await CardAsync(f);

        Status(card, 10).ShouldBe("fail");
        card.Rows.Single(r => r.Number == 10).Value.ShouldContain("quarters 67 %");
    }

    [Fact]
    public async Task Row_10_passes_a_quarter_with_a_mean_of_exactly_minus_0_05()
    {
        var f = WithRobustness(r => r with { ByQuarter = [Q(2025, 1, 0.1), Q(2025, 2, -0.05)] });

        var card = await CardAsync(f);

        Status(card, 10).ShouldBe("pass");
    }

    [Fact]
    public async Task Row_10_fails_with_one_instrument_over_its_limit()
    {
        var f = WithRobustness(r => r with { MaxInstrumentShare = 0.4 });

        var card = await CardAsync(f);

        Status(card, 10).ShouldBe("fail");
    }

    [Fact]
    public async Task Row_10_is_unavailable_for_a_verdict_stored_before_the_figures_and_says_to_re_run()
    {
        var f = WithRobustness(r => r with { ByQuarter = null, MaxInstrumentShare = null });

        var card = await CardAsync(f);

        var row = card.Rows.Single(r => r.Number == 10);
        row.Status.ShouldBe("unavailable");
        row.Value.ShouldContain("produced before the scorecard sources existed; re-run");
        card.Waiting.ShouldContain(w => w.Contains("row 10", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Row_10_is_unavailable_when_the_verdict_has_no_robustness_block()
    {
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { Robustness = null };

        var card = await CardAsync(f);

        Status(card, 10).ShouldBe("unavailable");
        card.Rows.Single(r => r.Number == 10).Value.ShouldNotContain("re-run");
    }

    [Fact]
    public async Task Row_11_passes_when_the_observed_fall_is_within_the_90th_percentile_and_the_95th_within_budget()
    {
        var card = await CardAsync(new ReadinessFixture());

        var row = card.Rows.Single(r => r.Number == 11);

        row.Status.ShouldBe("pass");
        row.Value.ShouldContain("90th 9 R");
    }

    [Fact]
    public async Task Row_11_fails_when_the_observed_fall_is_above_the_90th_percentile()
    {
        var f = WithRobustness(r => r with { MonteCarlo = r.MonteCarlo! with { DrawdownP90 = 4 } });

        var card = await CardAsync(f);

        Status(card, 11).ShouldBe("fail");
    }

    [Fact]
    public async Task Row_11_is_unavailable_for_a_verdict_stored_before_the_90th_percentile_and_says_to_re_run()
    {
        var f = WithRobustness(r => r with { MonteCarlo = r.MonteCarlo! with { DrawdownP90 = null } });

        var card = await CardAsync(f);

        var row = card.Rows.Single(r => r.Number == 11);
        row.Status.ShouldBe("unavailable");
        row.Value.ShouldContain("produced before the scorecard sources existed; re-run");
    }

    [Fact]
    public async Task An_old_stored_verdict_without_the_new_members_reads_as_unavailable()
    {
        const string Old = """{"sensitivity":{"timeframe":"15m","exit":"FIXED_RR","base":null,"parameters":[],"label":""},"monteCarlo":{"trades":330,"draws":1000,"seed":"s","observedMeanR":0.2,"observedMaxDrawdownR":5,"meanP05":0.1,"meanP50":0.2,"meanP95":0.3,"drawdownP50":4,"drawdownP95":12,"shareNotPositive":0.1},"byYear":[],"signFlipBaseline":null,"byRegime":"","limits":[]}""";
        var old = System.Text.Json.JsonSerializer.Deserialize<RobustnessView>(Old, WebJson)!;
        old.MonteCarlo!.DrawdownP90.ShouldBeNull();
        old.ByQuarter.ShouldBeNull();
        old.MaxInstrumentShare.ShouldBeNull();

        var f = WithRobustness(_ => old);
        var card = await CardAsync(f);

        Status(card, 10).ShouldBe("unavailable");
        Status(card, 11).ShouldBe("unavailable");
    }

    [Fact]
    public async Task Row_3_uses_the_larger_of_the_gate_factor_and_the_honest_factor()
    {
        // the gates used 1.25 (2 trials); the honest count of looks gives 2.5: 0.15 x 2.5 = 0.375 is over the 0.20 expectancy
        var f = new ReadinessFixture();
        f.Verdict = f.Verdict! with { Trials = new TrialsView(1, 0, 0, 0, 0, 64, 2.5, 2, 1.25, false, "t") };

        var card = await CardAsync(f);

        Status(card, 3).ShouldBe("fail");
        card.Rows.Single(r => r.Number == 3).Threshold.ShouldContain("0.375");
    }

    [Fact]
    public async Task A_forward_series_gross_of_charges_is_not_offered_as_net()
    {
        var f = new ReadinessFixture { Confirmed = false };

        var card = await CardAsync(f);

        card.SubScores.Single(s => s.Name == SubScore.EffectName).State.ShouldBe("unavailable");
        card.HardFails.ShouldContain(ReadinessCalculator.CostSheetName);
        card.Notes.ShouldContain(n => n.Contains("gross of charges", StringComparison.Ordinal));
        Status(card, 15).ShouldBe("unavailable");
    }

    [Fact]
    public async Task The_cf09_failure_boundary_is_a_hard_fail_named()
    {
        var f = new ReadinessFixture { SequentialResult = Result.Success(ReadinessFixture.SequentialView("failure", 120)) };

        var card = await CardAsync(f);

        card.Score.ShouldBe(0);
        card.HardFails.ShouldContain(ReadinessCalculator.FailureBoundaryName);
        card.Label.ShouldBe("not_ready");
    }

    [Fact]
    public async Task An_unreadable_sequential_test_is_unavailable_and_so_a_hard_fail()
    {
        var f = new ReadinessFixture { SequentialResult = Result.Failure<SequentialStatusView>(Error.Validation("x", "bad")) };

        var card = await CardAsync(f);

        card.HardFails.ShouldContain(h => h.StartsWith(ReadinessCalculator.FailureBoundaryName, StringComparison.Ordinal) && h.Contains("unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_drift_alarm_takes_the_stability_sub_score_to_zero_and_a_cusum_that_is_not_judged_is_unavailable()
    {
        var alarm = new ReadinessFixture { DecayResult = Result.Success(ReadinessFixture.DecayView(alarm: true)) };
        var notJudged = new ReadinessFixture();
        notJudged.DecayResult = Result.Success(ReadinessFixture.DecayView() with { Edge = ReadinessFixture.DecayView().Edge with { Cusum = null } });

        (await CardAsync(alarm)).SubScores.Single(s => s.Name == SubScore.StabilityName).Score.ShouldBe(0);
        (await CardAsync(notJudged)).SubScores.Single(s => s.Name == SubScore.StabilityName).State.ShouldBe("unavailable");
    }

    [Fact]
    public async Task A_paused_cell_of_an_applied_kill_proposal_is_a_hard_fail_and_a_re_enabled_one_is_not()
    {
        const string cell = "ORB|15m|BUY|intraday|FIXED_RR|*";
        var f = new ReadinessFixture();
        f.KillsResult = Result.Success(new KillProposalsView([new KillProposalView(1, "ORB", "decay_twice", false, null, null, 100, "t", [cell], "h", new DateOnly(2026, 11, 1), DateTimeOffset.UnixEpoch, "applied", "owner:me@x.test", DateTimeOffset.UnixEpoch, null)], new KillSettingsView(false, null, 1.5, 2, "h"), []));
        await Walk(f, cell, CellState.Candidate, CellState.Paper, CellState.Paused);

        var paused = await CardAsync(f);
        await Walk(f, cell, CellState.Paper);
        var reEnabled = await CardAsync(f);

        paused.HardFails.ShouldContain(ReadinessCalculator.KillName);
        reEnabled.HardFails.ShouldNotContain(ReadinessCalculator.KillName);
    }

    [Fact]
    public async Task A_champion_model_makes_the_calibration_sub_score_apply_with_its_error()
    {
        var f = new ReadinessFixture { Champions = [ReadinessFixture.Champion(6)] };

        var card = await CardAsync(f);

        var calibration = card.SubScores.Single(s => s.Name == SubScore.CalibrationName);
        calibration.State.ShouldBe("scored");
        calibration.Score.ShouldBe(40);
    }

    [Fact]
    public async Task The_forward_mean_inside_the_five_percent_band_is_inside_and_far_below_it_is_below()
    {
        var inside = new ReadinessFixture();
        var below = new ReadinessFixture { DecayResult = Result.Success(ReadinessFixture.DecayView() with { Reference = new DecayReferenceView("available", null, 400, 1.5, 0.5, "net") }) };

        (await CardAsync(inside)).SubScores.Single(s => s.Name == SubScore.AgreementName).Score.ShouldBe(100, "the months are positive in the fixture's series");
        (await CardAsync(below)).SubScores.Single(s => s.Name == SubScore.AgreementName).Score.ShouldBe(0);
    }

    [Fact]
    public async Task No_net_reference_leaves_the_agreement_unavailable()
    {
        var f = new ReadinessFixture { DecayResult = Result.Success(ReadinessFixture.DecayView() with { Reference = new DecayReferenceView("unavailable", "none", null, null, null, null) }) };

        var card = await CardAsync(f);

        card.SubScores.Single(s => s.Name == SubScore.AgreementName).State.ShouldBe("unavailable");
        card.Score.ShouldBe(0);
    }

    [Fact]
    public async Task The_phase_check_is_read_once_for_any_number_of_cards()
    {
        var f = new ReadinessFixture();
        var forward = f.ForwardReader;

        await forward.ReadAsync("ORB", Ct);
        await forward.ReadAsync("ORB", Ct);

        await f.Phase.ReceivedWithAnyArgs(1).HandleAsync(default!, Ct);
    }

    [Theory]
    [InlineData("MET", 15)]
    [InlineData("NOT MET", 14)]
    public async Task Row_15_follows_the_phase_check_verdict(string verdict, int rowsPassed)
    {
        var f = new ReadinessFixture();
        f.PhaseResult = Result.Success(new PaperPhaseCheckView(new XDrishti.Domain.Paper.PaperPhaseReport(null, default, 0, [], verdict, verdict, [], [], null!, []), "t"));

        var card = await CardAsync(f);

        card.RowsPassed.ShouldBe(rowsPassed);
    }

    [Fact]
    public async Task The_settings_hash_and_text_name_the_thresholds_used()
    {
        var f = new ReadinessFixture { Options = new ReadinessOptions(MinNEff: 400) };

        var card = await CardAsync(f);

        card.SettingsHash.ShouldBe(f.Options.SnapshotHash());
        card.Settings.ShouldContain("nEff=400");
        Status(card, 2).ShouldBe("fail", "n_eff is 320 in the verdict, which is under the raised bar of 400");
    }

    private static async Task Walk(ReadinessFixture f, string cell, params CellState[] states)
    {
        foreach (var state in states)
        {
            var move = new Application.Learning.RecordCellTransitionCommand(cell, state, "setup", "test", new TransitionFacts(true, true, true, true, 20), ["run:x"]);
            var handler = new Application.Learning.RecordCellTransitionHandler(f.Log, new FakeReadinessGate(), f.Time, Microsoft.Extensions.Logging.Abstractions.NullLogger<Application.Learning.RecordCellTransitionHandler>.Instance);
            (await handler.HandleAsync(move, Ct)).IsSuccess.ShouldBeTrue();
        }
    }
}
