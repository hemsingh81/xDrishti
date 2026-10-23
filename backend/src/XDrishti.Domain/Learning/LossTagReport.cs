using System.Globalization;
using System.Text;

namespace XDrishti.Domain.Learning;

/// <summary>The loss tags of closed trades per cell, the worst losses with the numbers behind each tag, the growing tags, and what is not built (P6-15).</summary>
/// <param name="Options">The thresholds used.</param>
/// <param name="Trades">Closed trades given.</param>
/// <param name="Losses">Of them, losses (R below 0).</param>
/// <param name="Cells">One entry per cell, ordered by key.</param>
/// <param name="Worst">The worst losses (lowest R first, ties by key), at most <c>ShowWorst</c>.</param>
/// <param name="Comparisons">Cell and tag pairs the growing-tag detector compared.</param>
/// <param name="Limits">Sentences that must be printed with the result.</param>
public sealed record LossTagReport(
    LossTagOptions Options,
    int Trades,
    int Losses,
    IReadOnlyList<CellLossTags> Cells,
    IReadOnlyList<TaggedLoss> Worst,
    int Comparisons,
    IReadOnlyList<string> Limits)
{
    /// <summary>Tags that the data cannot support yet and are therefore never assigned.</summary>
    public static IReadOnlyList<string> NotBuilt { get; } =
    [
        "against_regime: needs the regime of each trade (P6-02)",
        "news_results: needs an event calendar (P15)",
        "execution_gap and decision_drag: need real fills and recorded decisions (P7, P10, P11)",
        "wrong time of day versus the cell: the learned window is a proposal only (P6-14); LateEntry is a fixed cut-off",
    ];

    /// <summary>A fixed plain-text rendering; the same report gives the same text.</summary>
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var kinds = Enum.GetValues<LossTagKind>();
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(c, $"loss tags ({LossTagger.RuleVersion}): {Trades} closed trades, {Losses} losses"));
        foreach (var cell in Cells)
        {
            sb.AppendLine(string.Create(c, $"{cell.CellKey}: {cell.Trades} trades, {cell.Losses} losses"));
            var parts = kinds.Select((k, i) => string.Create(c, $"{k} {cell.Counts[i]}")).Append(string.Create(c, $"untagged {cell.Untagged}"));
            sb.AppendLine("  " + string.Join(", ", parts));
            foreach (var g in cell.Growing)
            {
                sb.AppendLine(string.Create(c, $"  growing: {g.Kind} {g.EarlierCount}/{g.Window} -> {g.RecentCount}/{g.Window} of the last two windows of losses (candidate lesson, none opened)"));
            }
        }

        if (Worst.Count > 0)
        {
            sb.AppendLine("worst losses:");
            foreach (var w in Worst)
            {
                sb.AppendLine(string.Create(c, $"  {w.Key} [{w.CellKey}] {w.R:F2} R: {(w.Tags.Count == 0 ? "untagged" : string.Empty)}"));
                foreach (var t in w.Tags)
                {
                    sb.AppendLine($"    {t.Kind}: {t.Detail}");
                }
            }
        }

        foreach (var item in NotBuilt)
        {
            sb.AppendLine("not built: " + item);
        }

        foreach (var limit in Limits)
        {
            sb.AppendLine("limit: " + limit);
        }

        return sb.ToString();
    }
}
