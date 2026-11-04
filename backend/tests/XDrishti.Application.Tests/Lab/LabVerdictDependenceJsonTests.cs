using System.Text.Json;
using XDrishti.Application.Lab;

namespace XDrishti.Application.Tests.Lab;

/// <summary>CF-01 acceptance criterion 11: a stored <c>lab_results</c> document written before the block reads "not computed"; one with the block round-trips.</summary>
public sealed class LabVerdictDependenceJsonTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // a P5-04 document as it was stored: no "dependence" member and no notes
    private const string Old = """
        {"outcome":"MARGINAL","variantCount":4,"adjustmentFactor":1.5,
         "evidence":{"oosTrades":88,"expectancyR":0.31,"profitFactor":1.45,"folds":8,"positiveFolds":6,"maxDrawdownR":5.5,"calibrationErrorPp":0,"method":"walk_forward"},
         "gates":[{"name":"oos_trades","required":30,"adjusted":45,"actual":88,"passed":true,"reason":"met"}],
         "limits":["calibration gate not applicable: no probability model (given 0)"],"cells":[],"fullPeriod":[],"labels":["x"]}
        """;

    [Fact]
    public void An_old_document_reads_without_the_block()
    {
        var v = JsonSerializer.Deserialize<LabVerdictView>(Old, Json)!;

        v.Outcome.ShouldBe("MARGINAL");
        v.Evidence.OosTrades.ShouldBe(88);
        v.Evidence.Dependence.ShouldBeNull();
    }

    [Fact]
    public void A_document_with_the_block_round_trips()
    {
        var v = JsonSerializer.Deserialize<LabVerdictView>(Old, Json)! with
        {
            Evidence = JsonSerializer.Deserialize<LabVerdictView>(Old, Json)!.Evidence with
            {
                Dependence = new XDrishti.Application.Confidence.DependenceView("ReportOnly", 88, 21.5, 4.1, "cluster", 396, 0.5, 273, 1119, 75.6, false, string.Empty, "s", 0.15, "ReportOnly/20"),
            },
        };

        var back = JsonSerializer.Deserialize<LabVerdictView>(JsonSerializer.Serialize(v, Json), Json)!;

        back.Evidence.Dependence.ShouldBe(v.Evidence.Dependence);
    }
}
