using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>P13-05 criterion 11: the extension group is additive; without it the rows and the id are byte-identical to what they were before (the champion's lineage stays valid).</summary>
public sealed class TrainingDataBuilderExtensionTests
{
    private static readonly DateOnly Day = new(2025, 3, 3);

    private static readonly ExtensionGroup Group = new("md", "md-1", ["e1", "e2"]);

    private static TripleBarrierLabel Label(string key, int label) =>
        new(key, label, label == 1 ? BarrierOutcome.Target : BarrierOutcome.Stop, LabelBasis.Net, label == 1 ? 1.0 : -1.0, 30, 1, 1_000, 2_800, false);

    private static TrainingSet Set(params TripleBarrierLabel[] labels) =>
        new("a1", "tb-1", ["r"], LabelBasis.Net, Day, Day, labels.Count(l => l.Label == 1), labels.Count(l => l.Label == 0), 0, 0, [.. labels.OrderBy(l => l.Key, StringComparer.Ordinal)]);

    private static TradeFeatureRow Stored(string key, double fill = 1.0) =>
        new("r", key, "ORB", "15m", "BUY", Day, 1_000, Enumerable.Repeat(fill, TradeFeatureCatalog.StoredCount).ToArray());

    private static Dictionary<string, double[]> Cells(params string[] keys) => keys.ToDictionary(k => k, _ => new double[] { 12, 0.55, 0.4, 0.2, 0 }, StringComparer.Ordinal);

    // The id text exactly as TrainingDataBuilder wrote it before P13-05: the reference the new code must still equal.
    private static string ReferenceId(TrainingData data, string? provenance)
    {
        var text = new StringBuilder();
        text.Append("training-data/1\n").Append(data.LabelSetId).Append('\n').Append("tf-1").Append('\n').Append(provenance).Append('\n');
        foreach (var name in data.Required)
        {
            text.Append(name).Append(',');
        }

        text.Append('\n');
        foreach (var row in data.Rows)
        {
            text.Append(row.TradeKey);
            foreach (var v in row.Values)
            {
                text.Append('|').Append(double.IsNaN(v) ? "nan" : v.ToString("R", CultureInfo.InvariantCulture));
            }

            text.Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private const string GoldenId = "d97fd9049f3ea8adb3901fa57795f52d1d3ee5a4611de94504500cacc0847961";

    [Fact]
    public void Without_a_group_the_id_is_the_id_it_always_was()
    {
        var data = TrainingDataBuilder.Build(Set(Label("t1", 1), Label("t2", 0)), [Stored("t1"), Stored("t2", 2.0)], Cells("t1", "t2"), null, "src=sim").Value;

        data.Id.ShouldBe(ReferenceId(data, "src=sim"));
        data.Id.ShouldBe(GoldenId);
        data.Extension.ShouldBeNull();
        data.DroppedNoExtension.ShouldBe(0);
        data.Rows.ShouldAllBe(r => r.Values.Count == TradeFeatureCatalog.All.Count);
    }

    [Fact]
    public void With_a_group_the_values_follow_the_catalog_the_id_differs_and_a_trade_without_an_extension_row_is_dropped_and_counted()
    {
        var set = Set(Label("t1", 1), Label("t2", 0), Label("t3", 1));
        var plain = TrainingDataBuilder.Build(set, [Stored("t1"), Stored("t2"), Stored("t3")], Cells("t1", "t2", "t3"), null, "src=sim").Value;
        var values = new Dictionary<string, double[]>(StringComparer.Ordinal) { ["t1"] = [0.5, double.NaN], ["t2"] = [0.25, 4.0] };

        var data = TrainingDataBuilder.Build(set, [Stored("t1"), Stored("t2"), Stored("t3")], Cells("t1", "t2", "t3"), new ExtensionGroupInput(Group, values), null, "src=sim").Value;

        data.Id.ShouldNotBe(plain.Id);
        data.Rows.Select(r => r.TradeKey).ShouldBe(["t1", "t2"]);
        data.DroppedNoExtension.ShouldBe(1);
        data.Extension.ShouldNotBeNull().Version.ShouldBe("md-1");
        data.Rows[0].Values.Count.ShouldBe(TradeFeatureCatalog.All.Count + 2);
        data.Rows[0].Values[TradeFeatureCatalog.All.Count].ShouldBe(0.5);
        double.IsNaN(data.Rows[0].Values[TradeFeatureCatalog.All.Count + 1]).ShouldBeTrue("an unknown extension value stays NaN, never filled");
        data.Rows[1].Values[TradeFeatureCatalog.All.Count + 1].ShouldBe(4.0);
        data.Rows[0].Values.Take(TradeFeatureCatalog.All.Count).ShouldBe(plain.Rows[0].Values);
    }

    [Fact]
    public void A_required_extension_value_unknown_drops_the_trade_and_is_counted_by_name()
    {
        var set = Set(Label("t1", 1), Label("t2", 0));
        var values = new Dictionary<string, double[]>(StringComparer.Ordinal) { ["t1"] = [0.5, double.NaN], ["t2"] = [0.25, 4.0] };

        var data = TrainingDataBuilder.Build(set, [Stored("t1"), Stored("t2")], Cells("t1", "t2"), new ExtensionGroupInput(Group, values, ["e2"])).Value;

        data.Rows.Select(r => r.TradeKey).ShouldBe(["t2"]);
        data.DroppedUnknown.ShouldBe(1);
        data.Extension!.UnknownByName["e2"].ShouldBe(1);
    }

    [Fact]
    public void The_extension_version_and_names_are_part_of_the_id()
    {
        var set = Set(Label("t1", 1));
        var values = new Dictionary<string, double[]>(StringComparer.Ordinal) { ["t1"] = [0.5, 1.0] };
        string Id(ExtensionGroup g) => TrainingDataBuilder.Build(set, [Stored("t1")], Cells("t1"), new ExtensionGroupInput(g, values)).Value.Id;

        Id(Group).ShouldBe(Id(Group));
        Id(new ExtensionGroup("md", "md-2", ["e1", "e2"])).ShouldNotBe(Id(Group));
        Id(new ExtensionGroup("md", "md-1", ["e1", "e3"])).ShouldNotBe(Id(Group));
    }

    [Fact]
    public void A_group_that_repeats_a_catalog_name_or_requires_a_name_it_does_not_have_is_refused()
    {
        var set = Set(Label("t1", 1));
        var values = new Dictionary<string, double[]>(StringComparer.Ordinal) { ["t1"] = [0.5, 1.0] };

        TrainingDataBuilder.Build(set, [Stored("t1")], Cells("t1"), new ExtensionGroupInput(new ExtensionGroup("md", "md-1", ["rsi14", "e2"]), values)).Error.Code.ShouldBe("training_data.extension");
        TrainingDataBuilder.Build(set, [Stored("t1")], Cells("t1"), new ExtensionGroupInput(Group, values, ["nope"])).Error.Code.ShouldBe("training_data.extension");
    }

    [Fact]
    public void An_extension_row_of_the_wrong_width_is_a_bad_row_and_not_a_missing_one()
    {
        var set = Set(Label("t1", 1));
        var values = new Dictionary<string, double[]>(StringComparer.Ordinal) { ["t1"] = [0.5] };

        var result = TrainingDataBuilder.Build(set, [Stored("t1")], Cells("t1"), new ExtensionGroupInput(Group, values));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("training_data.bad_row");
    }
}
