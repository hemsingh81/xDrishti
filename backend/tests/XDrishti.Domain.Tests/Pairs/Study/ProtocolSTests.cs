using System.Text.Json;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.PairResearch;

namespace XDrishti.Domain.Tests.Pairs.Study;

/// <summary>P14-13 protocol S: the verdict rows with unavailable inputs, the margin widened by Factor(N), the planted effect and the placebo, concentration, gross and honest states, the hold-out cut, determinism.</summary>
public sealed class ProtocolSTests
{
    private static ProtocolSResult Evaluate(List<PairTradeSample> trades, int trials = 2006, PairStudyOptions? options = null) =>
        ProtocolS.Evaluate(PairTrades.Input(trades, trials, options)).Value;

    [Fact]
    public void A_planted_net_effect_with_seven_quarters_and_over_300_trades_is_a_keep_at_N_2006()
    {
        var result = Evaluate(PairTrades.Make(1, 0.6));

        result.Outcome.Verdict.ShouldBe(ExtensionVerdict.Keep, string.Join("; ", result.Outcome.Failed.Concat(result.Outcome.Missing)));
        result.Outcome.FoldsEvaluable.ShouldBe(7);
        result.Outcome.Rows.ShouldBeGreaterThan(300);
        result.HoldOutStart.ShouldBe(new DateOnly(2024, 9, 2));
        result.EmbargoDropped.ShouldBe(5);
    }

    [Fact]
    public void The_margin_is_widened_by_the_factor_of_the_ledger_count_and_the_evidence_prints_it()
    {
        var result = Evaluate(PairTrades.Make(1, 0.6));

        using var estimates = JsonDocument.Parse(result.Outcome.EstimatesJson);
        using var evidence = JsonDocument.Parse(result.Outcome.EvidenceJson);
        estimates.RootElement.GetProperty("required").GetDouble().ShouldBe(0.05 * MultipleTesting.Factor(2006), 1e-9);
        evidence.RootElement.GetProperty("ledger_trials").GetInt32().ShouldBe(2006);
        evidence.RootElement.GetProperty("factor").GetDouble().ShouldBe(3.74, 0.005);
        evidence.RootElement.GetProperty("basis").GetString().ShouldBe("net");
        evidence.RootElement.GetProperty("variant").GetString().ShouldBe("e2.0/L5");
    }

    [Fact]
    public void An_effect_under_the_widened_margin_is_not_a_keep_though_it_would_be_at_N_1()
    {
        var trades = PairTrades.Make(2, 0.16, 0.5);

        Evaluate(trades, trials: 1).Outcome.Verdict.ShouldBe(ExtensionVerdict.Keep);
        var widened = Evaluate(trades, trials: 2006).Outcome;

        widened.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        widened.Failed.ShouldContain(f => f.Contains("lower bound", StringComparison.Ordinal) && f.Contains("below 0.187", StringComparison.Ordinal));
    }

    [Fact]
    public void Fewer_than_six_evaluable_quarters_is_NOT_EVALUABLE_and_names_the_quarters_and_the_trades()
    {
        var result = Evaluate(PairTrades.Make(3, 0.6, quarters: 7));

        result.Outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        result.Outcome.Missing.ShouldBe(["quarters 5 of 6", "pair trades 295 of 300"]);
    }

    [Fact]
    public void Under_300_pooled_trades_is_NOT_EVALUABLE_and_names_them()
    {
        var result = Evaluate(PairTrades.Make(4, 0.6, perQuarter: 40));

        result.Outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        result.Outcome.Missing.ShouldBe(["pair trades 275 of 300"]);
        result.Outcome.FoldsEvaluable.ShouldBe(7);
    }

    [Fact]
    public void A_quarter_with_under_20_trades_is_not_evaluable_and_its_trades_are_not_pooled()
    {
        var trades = PairTrades.Make(5, 0.6, quarters: 9, perQuarter: 60).Where(t => t.Date.Year != 2023 || t.Date.Month > 3 || t.Date.Day <= 5).ToList();

        var result = Evaluate(trades);

        result.Outcome.FoldsTotal.ShouldBe(7);
        result.Outcome.FoldsEvaluable.ShouldBe(6);
        using var evidence = JsonDocument.Parse(result.Outcome.EvidenceJson);
        evidence.RootElement.GetProperty("quarters")[0].GetProperty("evaluable").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void A_non_value_is_the_text_unavailable_in_the_json_and_never_a_digit_and_the_limits_state_the_ban_list()
    {
        var result = Evaluate(PairTrades.Make(6, 0.6, quarters: 7)).Outcome;

        result.EstimatesJson.ShouldContain("\"day_lower\":\"unavailable\"");
        result.EstimatesJson.ShouldContain("\"session_upper\":\"unavailable\"");
        result.EstimatesJson.ShouldContain("\"required\":\"unavailable\"");
        result.EstimatesJson.ShouldContain("\"max_drawdown_r\":\"unavailable\"");
        result.EvidenceJson.ShouldNotContain("NaN");
        result.EvidenceJson.ShouldNotContain("Infinity");
        result.Limits.ShouldContain(l => l.StartsWith("ban_list: unavailable", StringComparison.Ordinal));
        result.Limits.ShouldContain(l => l.Contains("sector: as of today", StringComparison.Ordinal));
        result.Limits.ShouldContain(l => l.Contains("asm_gsm: as of today", StringComparison.Ordinal));
    }

    [Fact]
    public void No_trades_at_all_is_NOT_EVALUABLE_with_zero_counted()
    {
        var result = Evaluate([]);

        result.Outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        result.Outcome.Missing.ShouldBe(["quarters 0 of 6", "pair trades 0 of 300"]);
        result.HoldOutStart.ShouldBeNull();
    }

    [Fact]
    public void A_gross_result_that_would_be_a_keep_is_inconclusive_with_the_reason()
    {
        var result = Evaluate(PairTrades.Make(1, 0.6, net: false)).Outcome;

        result.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        result.Failed.ShouldContain(f => f.StartsWith("gross:", StringComparison.Ordinal));
        result.EvidenceJson.ShouldContain("\"basis\":\"gross\"");
        result.Limits.ShouldContain(l => l.Contains("a gross KEEP is impossible", StringComparison.Ordinal));
    }

    [Fact]
    public void A_gross_result_whose_upper_bound_is_under_the_margin_is_a_valid_DROP()
    {
        var result = Evaluate(PairTrades.Make(7, -0.4, net: false)).Outcome;

        result.Verdict.ShouldBe(ExtensionVerdict.Drop);
    }

    [Fact]
    public void A_net_loser_is_a_DROP_and_a_net_result_with_an_upper_bound_over_the_margin_is_not()
    {
        Evaluate(PairTrades.Make(8, -0.4)).Outcome.Verdict.ShouldBe(ExtensionVerdict.Drop);
        Evaluate(PairTrades.Make(9, 0.10, 1.0, perQuarter: 60)).Outcome.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
    }

    [Fact]
    public void One_pair_making_30_percent_of_the_net_profit_is_not_a_keep_and_the_reason_is_named()
    {
        var trades = PairTrades.Make(10, 0.6, pairShift: p => p.StartsWith("NSE:Y0|", StringComparison.Ordinal) ? 2.0 : 0.0);

        var outcome = Evaluate(trades).Outcome;

        outcome.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        outcome.Failed.ShouldContain(f => f.Contains("pair NSE:Y0|NSE:X makes", StringComparison.Ordinal) && f.Contains("of the net profit", StringComparison.Ordinal));
        using var estimates = JsonDocument.Parse(outcome.EstimatesJson);
        estimates.RootElement.GetProperty("top_pair_share").GetDouble().ShouldBeGreaterThan(0.25);
    }

    [Fact]
    public void Mean_R_above_zero_in_only_half_the_quarters_is_not_a_keep()
    {
        var trades = PairTrades.Make(11, 0.6, 0.3).Select(t => (t.Date.Month <= 6 && t.Date.Year == 2023) || (t.Date.Month > 6 && t.Date.Year == 2024 && t.Date.Month <= 8) ? t with { R = -Math.Abs(t.R), NetInr = -Math.Abs(t.NetInr!.Value) } : t).ToList();

        var outcome = Evaluate(trades, trials: 1).Outcome;

        outcome.Verdict.ShouldNotBe(ExtensionVerdict.Keep);
        outcome.Failed.ShouldContain(f => f.Contains("quarters", StringComparison.Ordinal));
    }

    [Fact]
    public void The_placebo_is_never_a_keep_at_N_2006_over_200_seeds_and_rarely_one_at_N_1()
    {
        var keepsWidened = 0;
        var keepsPlain = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var trades = PairTrades.Make(1000 + seed, 0.0);
            keepsWidened += Evaluate(trades, 2006).Outcome.Verdict == ExtensionVerdict.Keep ? 1 : 0;
            keepsPlain += Evaluate(trades, 1).Outcome.Verdict == ExtensionVerdict.Keep ? 1 : 0;
        }

        keepsWidened.ShouldBe(0);
        keepsPlain.ShouldBeLessThanOrEqualTo(10);
    }

    [Fact]
    public void The_result_does_not_depend_on_the_order_of_the_trades()
    {
        var trades = PairTrades.Make(12, 0.3);
        var shuffled = trades.OrderBy(t => t.R).ToList();

        var a = Evaluate(trades).Outcome;
        var b = Evaluate(shuffled).Outcome;

        b.EstimatesJson.ShouldBe(a.EstimatesJson);
        b.EvidenceJson.ShouldBe(a.EvidenceJson);
        b.Verdict.ShouldBe(a.Verdict);
    }

    [Fact]
    public void The_development_verdict_never_reads_the_hold_out_trades()
    {
        var trades = PairTrades.Make(13, 0.6);
        var wild = trades.Select(t => t.Date >= new DateOnly(2024, 9, 2) ? t with { R = 1000, NetInr = 600_000m } : t).ToList();

        var a = Evaluate(trades).Outcome;
        var b = Evaluate(wild).Outcome;

        b.EstimatesJson.ShouldBe(a.EstimatesJson);
        b.EvidenceJson.ShouldBe(a.EvidenceJson);
    }

    [Fact]
    public void The_hold_out_is_the_last_six_months_and_the_five_dates_before_it_are_the_embargo()
    {
        var trades = PairTrades.Make(14, 0.1);

        var (development, start, dropped) = ProtocolS.Cut(trades, PairStudyOptions.Default);

        start.ShouldBe(new DateOnly(2024, 9, 2));
        dropped.ShouldBe(5);
        development.Max(t => t.Date).ShouldBe(new DateOnly(2024, 8, 24));
        development.Count.ShouldBe(trades.Count(t => t.Date < new DateOnly(2024, 8, 25)));
    }

    [Fact]
    public void The_hold_out_is_a_keep_with_100_trades_a_positive_mean_and_a_lower_bound_above_zero()
    {
        var keep = ProtocolS.HoldOut(PairTrades.Make(15, 0.6), PairTrades.Fast).Value;
        var flat = ProtocolS.HoldOut(PairTrades.Make(16, 0.0), PairTrades.Fast).Value;
        var thin = ProtocolS.HoldOut(PairTrades.Make(17, 0.6, quarters: 9, perQuarter: 40), PairTrades.Fast).Value;

        keep.Verdict.ShouldBe(ExtensionVerdict.Keep);
        flat.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        thin.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        thin.Missing.ShouldBe(["hold-out pair trades 80 of 100"]);
        keep.Limits.ShouldContain(l => l.Contains("read once", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bad_input_is_refused_not_thrown()
    {
        var bad = PairTrades.Make(18, 0.1);
        bad[3] = bad[3] with { R = double.NaN };

        ProtocolS.Evaluate(PairTrades.Input(bad)).IsFailure.ShouldBeTrue();
        ProtocolS.Evaluate(PairTrades.Input(PairTrades.Make(18, 0.1), trials: 0)).IsFailure.ShouldBeTrue();
        ProtocolS.HoldOut(bad, PairTrades.Fast).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_variant_grid_and_formation_counts_are_in_the_evidence_with_the_half_life_distribution()
    {
        var input = PairTrades.Input(PairTrades.Make(19, 0.6)) with
        {
            Grid = [new PairGridLine("e2.0/L5", 415, 0.6), new PairGridLine("e1.5/L5", 0, null)],
            Formations = [new PairFormationStat("2024Q1", 100, 98, 2, 3, [2.0, 5.0, 9.0]), new PairFormationStat("2024Q2", 100, 100, 0, 0, [])],
        };

        var outcome = ProtocolS.Evaluate(input).Value.Outcome;

        using var evidence = JsonDocument.Parse(outcome.EvidenceJson);
        var formations = evidence.RootElement.GetProperty("formations");
        formations.GetProperty("share_without_selected_pair").GetDouble().ShouldBe(0.5);
        formations.GetProperty("half_life_median").GetDouble().ShouldBe(5.0);
        formations.GetProperty("half_life_max").GetDouble().ShouldBe(9.0);
        evidence.RootElement.GetProperty("variant_grid")[0].GetProperty("mean_r").GetString().ShouldBe("unavailable");
        evidence.RootElement.GetProperty("turnover_per_trade_inr").GetDouble().ShouldBe(200_000.0);
    }

    [Fact]
    public void A_gross_or_cost_unavailable_trade_in_the_hold_out_makes_the_confirmation_inconclusive_never_a_keep()
    {
        var trades = PairTrades.Make(15, 0.6);
        var oneGross = trades.Select((t, i) => t.Date >= new DateOnly(2024, 9, 2) && i % 40 == 0 ? t with { NetInr = null } : t).ToList();

        var net = ProtocolS.HoldOut(trades, PairTrades.Fast).Value;
        var mixed = ProtocolS.HoldOut(oneGross, PairTrades.Fast).Value;

        net.Verdict.ShouldBe(ExtensionVerdict.Keep);
        mixed.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        mixed.Failed.ShouldContain(f => f.Contains("gross or its costs are unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pooled_result_without_a_net_profit_says_the_concentration_cannot_be_computed()
    {
        var outcome = Evaluate(PairTrades.Make(8, -0.4), trials: 1).Outcome;

        outcome.Failed.ShouldContain("no net profit; concentration not computable");
    }

    [Fact]
    public void The_lab_gates_are_the_labs_own_defaults_and_the_calibration_gate_is_reported_as_not_applicable()
    {
        PairStudyOptions.Default.Gates.ShouldBe(QualificationGates.Default);
        Evaluate(PairTrades.Make(1, 0.6)).Outcome.EvidenceJson.ShouldContain("\"calibration_gate\":\"not applicable (no probability)\"");
    }

    [Fact]
    public void A_hold_out_start_given_by_the_caller_is_used_and_trades_after_it_are_not_needed()
    {
        var all = PairTrades.Make(14, 0.6);
        var start = ProtocolS.Cut(all, PairTrades.Fast).HoldOutStart;

        var withAll = ProtocolS.Evaluate(PairTrades.Input(all)).Value.Outcome;
        var devOnly = ProtocolS.Evaluate(PairTrades.Input([.. all.Where(t => t.Date < start)]) with { HoldOutStart = start }).Value.Outcome;

        devOnly.EstimatesJson.ShouldBe(withAll.EstimatesJson);
        devOnly.EvidenceJson.ShouldBe(withAll.EvidenceJson);
    }
}
