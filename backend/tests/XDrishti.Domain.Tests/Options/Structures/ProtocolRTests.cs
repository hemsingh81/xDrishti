using System.Text.Json;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Evaluation;
using XDrishti.Domain.Tests.OptionEvaluation;

namespace XDrishti.Domain.Tests.Options.Structures;

/// <summary>P14-10 criterion 4 and the protocol R table: the condor is judged on its own R (no cash twin), its minima are 6 quarters and 200 condors, the margin is widened by N = 4 (factor 1.5), a gross result is INCONCLUSIVE, the credit gates name what is missing.</summary>
public sealed class ProtocolRTests
{
    private static ProtocolOInput Input(IReadOnlyList<ExpressionPair> pairs, int trials = 4) =>
        new(pairs, new Dictionary<string, int>(), trials, ExprPairs.EpochFrom, null, ExpressionStudyOptions.Condor);

    /// <summary>Condors as the shared core reads them: R as the option's, a cash R of 0.</summary>
    private static List<ExpressionPair> Condors(ulong seed, int[] counts, double mean, bool net = true) =>
        [.. ExprPairs.Make(seed, counts, mean, 0.5, net).Select(p => p with { OptionR = p.D, CashR = 0 })];

    [Fact]
    public void A_planted_condor_edge_with_seven_quarters_and_enough_condors_is_a_keep_judged_on_its_own_R_with_protocol_R_words()
    {
        var result = ProtocolR.Evaluate(Input(Condors(1, ExprPairs.Even(7, 40), 0.4))).Value;

        result.Outcome.Verdict.ShouldBe(ExtensionVerdict.Keep, string.Join("; ", result.Outcome.Failed.Concat(result.Outcome.Missing)));
        using var evidence = JsonDocument.Parse(result.Outcome.EvidenceJson);
        evidence.RootElement.GetProperty("protocol").GetString().ShouldBe("R");
        result.Lines.ShouldContain(l => l.StartsWith("mean R (net rupees / maximum loss):", StringComparison.Ordinal));
        result.Lines.ShouldNotContain(l => l.Contains("futures", StringComparison.Ordinal));
        result.Outcome.Limits.ShouldContain(l => l.StartsWith("no cash twin", StringComparison.Ordinal));
        ProtocolR.Protocol.ShouldBe("R");
    }

    [Fact]
    public void The_margin_at_N_4_is_0075_R_and_a_placebo_is_not_a_keep()
    {
        var edge = ProtocolR.Evaluate(Input(Condors(2, ExprPairs.Even(7, 40), 0.4))).Value;
        var placebo = ProtocolR.Evaluate(Input(Condors(3, ExprPairs.Even(7, 40), 0.0))).Value;

        using var estimates = JsonDocument.Parse(edge.Outcome.EstimatesJson);
        estimates.RootElement.GetProperty("required").GetDouble().ShouldBe(0.075, 1e-9);
        placebo.Outcome.Verdict.ShouldNotBe(ExtensionVerdict.Keep);
    }

    [Fact]
    public void Five_evaluable_quarters_or_199_condors_is_NOT_EVALUABLE_naming_the_quantity()
    {
        var folds = ProtocolR.Evaluate(Input(Condors(4, [60, 60, 60, 60, 60, 3, 3], 0.4))).Value.Outcome;
        var count = ProtocolR.Evaluate(Input(Condors(5, [33, 33, 33, 33, 33, 34], 0.4))).Value.Outcome;
        var none = ProtocolR.Evaluate(Input([])).Value.Outcome;

        folds.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        folds.Missing.ShouldContain("folds 5 of 6");
        count.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        count.Missing.ShouldContain("condors 199 of 200");
        none.Missing.ShouldContain(m => m.StartsWith("no_condors", StringComparison.Ordinal));
    }

    [Fact]
    public void A_gross_condor_result_is_inconclusive_whichever_way_it_points()
    {
        var up = ProtocolR.Evaluate(Input(Condors(6, ExprPairs.Even(7, 40), 0.6, net: false))).Value.Outcome;
        var down = ProtocolR.Evaluate(Input(Condors(7, ExprPairs.Even(7, 40), -0.6, net: false))).Value.Outcome;

        up.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        down.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        down.Limits.ShouldContain("gross upper bound below margin");
    }

    [Fact]
    public void A_net_condor_loss_with_the_minima_met_is_a_drop()
    {
        var outcome = ProtocolR.Evaluate(Input(Condors(8, ExprPairs.Even(7, 40), -0.5))).Value.Outcome;

        outcome.Verdict.ShouldBe(ExtensionVerdict.Drop);
    }

    [Fact]
    public void The_hold_out_is_read_with_protocol_R_words_and_fewer_than_100_condors_is_NOT_EVALUABLE()
    {
        var start = new DateOnly(2025, 7, 1);
        var few = Condors(9, [40], 0.4).Select(p => p).ToList();

        var outcome = ProtocolR.HoldOut(few, ExpressionStudyOptions.Condor, new DateOnly(2024, 1, 1)).Value;

        outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        outcome.Missing.ShouldContain("hold-out condors 40 of 100");
        using var evidence = JsonDocument.Parse(outcome.EvidenceJson);
        evidence.RootElement.GetProperty("protocol").GetString().ShouldBe("R");
        start.ShouldBeGreaterThan(new DateOnly(2024, 1, 1));
    }

    [Fact]
    public void The_options_of_protocol_R_hash_apart_from_those_of_protocol_O()
    {
        ExpressionStudyOptions.Condor.Canonical().ShouldStartWith("protocol-r/1;");
        ExpressionStudyOptions.Default.Canonical().ShouldStartWith("protocol-o/1;");
        (ExpressionStudyOptions.Condor.MinPaired, ExpressionStudyOptions.Condor.MinFolds, ExpressionStudyOptions.Condor.MarginR).ShouldBe((200, 6, 0.05));
        ExpressionStudyOptions.Condor.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void The_gates_name_regime_unavailable_first_and_spread_not_measured_when_the_model_is_short_of_60_sessions_or_there_is_none()
    {
        StructureGate.Missing(regimeStored: true, 60, 60).ShouldBeEmpty();
        StructureGate.Missing(regimeStored: false, 60, 60).ShouldHaveSingleItem().ShouldStartWith("regime_unavailable");
        StructureGate.Missing(regimeStored: true, 59, 60).ShouldHaveSingleItem().ShouldContain("spread_not_measured (best measured model 59 of 60");
        var both = StructureGate.Missing(regimeStored: false, null, 60);
        both.Count.ShouldBe(2);
        both[0].ShouldStartWith("regime_unavailable");
        both[1].ShouldStartWith("spread_not_measured");

        var outcome = StructureGate.NotEvaluable("R", both);

        outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        outcome.Missing.ShouldBe(both);
    }
}
