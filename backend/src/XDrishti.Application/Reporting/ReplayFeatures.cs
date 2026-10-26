using XDrishti.Domain.Learning;

namespace XDrishti.Application.Reporting;

/// <summary>The stored features of a trade as a view (P8-11, D7): from the stored row only; a NaN is unavailable with its reason, never a zero; a row made for another decision instant is not shown.</summary>
internal static class ReplayFeatures
{
    public const string NoRow = "No decision-time features stored for this trade (run `xd learn features`).";

    private const string ContextReason = "market context not built (P3-01d/e)";

    private const string WarmupReason = "unknown at the decision (not enough history)";

    public static ReplayFeaturesView Build(TradeFeatureRow? row, long decisionEpochSeconds)
    {
        if (row is null)
        {
            return new ReplayFeaturesView("unavailable", TradeFeatureCatalog.Version, NoRow, []);
        }

        if (row.DecisionEpochSeconds != decisionEpochSeconds)
        {
            return new ReplayFeaturesView("unavailable", TradeFeatureCatalog.Version, "The stored features were made for another decision instant than this trade's; they are not shown.", []);
        }

        var items = new List<ReplayFeatureItemView>(TradeFeatureCatalog.StoredCount);
        for (var i = 0; i < TradeFeatureCatalog.StoredCount && i < row.Values.Count; i++)
        {
            var def = TradeFeatureCatalog.All[i];
            var source = def.Source.ToString().ToLowerInvariant();
            var v = row.Values[i];
            items.Add(double.IsFinite(v)
                ? new ReplayFeatureItemView(def.Name, source, "value", v, null)
                : new ReplayFeatureItemView(def.Name, source, "unavailable", null, def.Source == TradeFeatureSource.Context ? ContextReason : WarmupReason));
        }

        return new ReplayFeaturesView("value", TradeFeatureCatalog.Version, null, items);
    }
}
