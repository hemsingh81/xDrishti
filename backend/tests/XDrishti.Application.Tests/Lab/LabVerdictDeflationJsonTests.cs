using System.Text.Json;
using XDrishti.Application.Lab;

namespace XDrishti.Application.Tests.Lab;

/// <summary>CF-03 acceptance criterion 17: a stored <c>lab_results</c> document written before the new members reads "not computed"; one with them round-trips.</summary>
public sealed class LabVerdictDeflationJsonTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Old = """
        {"outcome":"MARGINAL","variantCount":4,"adjustmentFactor":1.5,
         "evidence":{"oosTrades":88,"expectancyR":0.31,"profitFactor":1.45,"folds":8,"positiveFolds":6,"maxDrawdownR":5.5,"calibrationErrorPp":0,"method":"walk_forward"},
         "gates":[],"limits":[],"cells":[],"fullPeriod":[],"labels":[],
         "robustness":{"sensitivity":{"timeframe":"15m","exit":"FIXED_RR{rr=2}","base":null,"parameters":[],"label":"l"},
           "monteCarlo":{"trades":88,"draws":1000,"seed":"1","observedMeanR":0.3,"observedMaxDrawdownR":4,"meanP05":0.1,"meanP50":0.3,"meanP95":0.5,"drawdownP50":3,"drawdownP95":6,"shareNotPositive":0.01},
           "byYear":[],"signFlipBaseline":null,"byRegime":"unavailable","limits":[]}}
        """;

    [Fact]
    public void An_old_document_reads_without_the_deflated_sharpe_ratio_or_the_block_result_and_keeps_the_monte_carlo()
    {
        var v = JsonSerializer.Deserialize<LabVerdictView>(Old, Json)!;

        v.DeflatedSharpe.ShouldBeNull();
        v.Robustness!.MonteCarloBlock.ShouldBeNull();
        v.Robustness.MonteCarlo!.Draws.ShouldBe(1000);
    }

    [Fact]
    public void A_document_with_the_new_members_round_trips()
    {
        var old = JsonSerializer.Deserialize<LabVerdictView>(Old, Json)!;
        var v = old with
        {
            DeflatedSharpe = new DeflatedSharpeView("ReportOnly", 5, 5, 0.05, 0.09, 0.15, 80, 0.1, 3.2, 0.72, 0.95, "nEff", "ReportOnly/0.95/stored/NEff", "n"),
            Robustness = old.Robustness! with { MonteCarloBlock = new MonteCarloBlockView("ReportOnly", 40, 3, 1000, "9", 0.3, 0.1, 0.3, 0.5, 3, 6, 0.01, 1.4, "ReportOnly/1000/auto", "n") },
        };

        var back = JsonSerializer.Deserialize<LabVerdictView>(JsonSerializer.Serialize(v, Json), Json)!;

        back.DeflatedSharpe.ShouldBe(v.DeflatedSharpe);
        back.Robustness!.MonteCarloBlock.ShouldBe(v.Robustness.MonteCarloBlock);
    }
}
