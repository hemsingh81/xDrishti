using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Learning;

public sealed class TrainingDataBuilderTests
{
    private static readonly DateOnly Day = new(2025, 3, 3);

    private static TripleBarrierLabel Label(string key, int label, double r = 1.0) =>
        new(key, label, label == 1 ? BarrierOutcome.Target : BarrierOutcome.Stop, LabelBasis.Net, r, 30, 1, 1_000, 2_800, false);

    private static TrainingSet Set(params TripleBarrierLabel[] labels) =>
        new("a1", "tb-1", ["r"], LabelBasis.Net, Day, Day, labels.Count(l => l.Label == 1), labels.Count(l => l.Label == 0), 0, 0, [.. labels.OrderBy(l => l.Key, StringComparer.Ordinal)]);

    private static TradeFeatureRow Stored(string key, double fill = 1.0, int? unknownAt = null)
    {
        var values = Enumerable.Repeat(fill, TradeFeatureCatalog.StoredCount).ToArray();
        if (unknownAt is { } i)
        {
            values[i] = double.NaN;
        }

        return new TradeFeatureRow("r", key, "ORB", "15m", "BUY", Day, 1_000, values);
    }

    private static Dictionary<string, double[]> Cells(params string[] keys) =>
        keys.ToDictionary(k => k, _ => new double[] { 12, 0.55, 0.4, 0.2, 0 }, StringComparer.Ordinal);

    [Fact]
    public void A_labelled_trade_with_features_and_cell_values_becomes_a_row_of_the_whole_catalog()
    {
        var result = TrainingDataBuilder.Build(Set(Label("t1", 1), Label("t2", 0)), [Stored("t1"), Stored("t2")], Cells("t1", "t2"));

        result.IsSuccess.ShouldBeTrue();
        var data = result.Value;
        data.Rows.Count.ShouldBe(2);
        data.Rows[0].Values.Count.ShouldBe(TradeFeatureCatalog.All.Count);
        data.Rows[0].Values[TradeFeatureCatalog.IndexOf("cell_n")].ShouldBe(12);
        data.Positives.ShouldBe(1);
        data.Negatives.ShouldBe(1);
        data.FeatureVersion.ShouldBe("tf-1");
        data.LabelSetId.ShouldBe("a1");
    }

    [Fact]
    public void A_labelled_trade_without_a_feature_row_is_dropped_and_counted()
    {
        var data = TrainingDataBuilder.Build(Set(Label("t1", 1), Label("t2", 0)), [Stored("t1")], Cells("t1", "t2")).Value;

        data.Rows.Select(r => r.TradeKey).ShouldBe(["t1"]);
        data.DroppedNoFeatures.ShouldBe(1);
        data.DroppedUnknown.ShouldBe(0);
        data.Labelled.ShouldBe(2);
    }

    [Fact]
    public void A_row_with_a_required_feature_unknown_is_dropped_and_the_feature_is_named_in_the_count()
    {
        var rsi = TradeFeatureCatalog.IndexOf("rsi14");

        var data = TrainingDataBuilder.Build(Set(Label("t1", 1), Label("t2", 0)), [Stored("t1"), Stored("t2", unknownAt: rsi)], Cells("t1", "t2")).Value;

        data.Rows.Count.ShouldBe(1);
        data.DroppedUnknown.ShouldBe(1);
        data.UnknownByFeature["rsi14"].ShouldBe(1);
        data.UnknownByFeature["adx14"].ShouldBe(0);
    }

    [Fact]
    public void A_context_feature_unknown_does_not_drop_a_row_by_default_but_does_when_required()
    {
        var vix = TradeFeatureCatalog.IndexOf("vix");
        var stored = new[] { Stored("t1", unknownAt: vix) };

        var byDefault = TrainingDataBuilder.Build(Set(Label("t1", 1)), stored, Cells("t1"));
        var required = TrainingDataBuilder.Build(Set(Label("t1", 1)), stored, Cells("t1"), ["vix"]);

        byDefault.IsSuccess.ShouldBeTrue();
        double.IsNaN(byDefault.Value.Rows[0].Values[vix]).ShouldBeTrue("an unknown optional value is kept as unknown, never filled");
        required.IsFailure.ShouldBeTrue();
        required.Error.Code.ShouldBe("training_data.empty");
    }

    [Fact]
    public void An_unknown_expectancy_of_an_empty_cell_does_not_drop_the_row()
    {
        var cells = new Dictionary<string, double[]> { ["t1"] = [0, 0.5, 0.2, double.NaN, 0] };

        var data = TrainingDataBuilder.Build(Set(Label("t1", 1)), [Stored("t1")], cells);

        data.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_name_that_is_not_in_the_catalog_is_refused()
    {
        var result = TrainingDataBuilder.Build(Set(Label("t1", 1)), [Stored("t1")], Cells("t1"), ["nonsense"]);

        result.Error.Code.ShouldBe("training_data.unknown_feature");
    }

    [Fact]
    public void A_stored_row_of_the_wrong_length_or_a_duplicate_key_is_refused()
    {
        var short1 = new TradeFeatureRow("r", "t1", "ORB", "15m", "BUY", Day, 1_000, [1.0, 2.0]);

        TrainingDataBuilder.Build(Set(Label("t1", 1)), [short1], Cells("t1")).Error.Code.ShouldBe("training_data.bad_row");
        TrainingDataBuilder.Build(Set(Label("t1", 1)), [Stored("t1"), Stored("t1")], Cells("t1")).Error.Code.ShouldBe("training_data.duplicate_key");
    }

    [Fact]
    public void The_id_is_stable_and_follows_the_order_of_nothing_but_changes_with_any_value_or_the_required_set()
    {
        var set = Set(Label("t1", 1), Label("t2", 0));
        var a = TrainingDataBuilder.Build(set, [Stored("t1"), Stored("t2")], Cells("t1", "t2")).Value;
        var again = TrainingDataBuilder.Build(set, [Stored("t2"), Stored("t1")], Cells("t2", "t1")).Value;
        var otherValue = TrainingDataBuilder.Build(set, [Stored("t1"), Stored("t2", fill: 2.0)], Cells("t1", "t2")).Value;
        var otherRequired = TrainingDataBuilder.Build(set, [Stored("t1"), Stored("t2")], Cells("t1", "t2"), ["rsi14"]).Value;

        again.Id.ShouldBe(a.Id);
        otherValue.Id.ShouldNotBe(a.Id);
        otherRequired.Id.ShouldNotBe(a.Id);
        a.Id.Length.ShouldBe(64);
    }

    [Fact]
    public void Nothing_to_join_is_an_empty_failure_that_counts_what_was_lost()
    {
        var result = TrainingDataBuilder.Build(Set(Label("t1", 1)), [], Cells("t1"));

        result.Error.Code.ShouldBe("training_data.empty");
        result.Error.Message.ShouldContain("1 without features");
    }
}
